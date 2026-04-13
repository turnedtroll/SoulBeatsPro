using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SoulBeatsPro;

internal static class AutoUpdater
{
    private const string GitHubOwner = "turnedtroll";
    private const string GitHubRepo = "SoulBeatsPro";
    private const string ExeName = "SoulBeatsPro.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SoulBeatsPro", CurrentVersion.ToString()));
        return c;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// Check GitHub for the latest release. Returns (tag, download url) or null.
    public static async Task<(Version version, string tag, string downloadUrl)?> CheckForUpdateAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            // Strip leading 'v' from tag like "v1.2.0"
            var versionStr = tag.TrimStart('v', 'V');

            if (!Version.TryParse(versionStr, out var remoteVersion))
                return null;

            if (remoteVersion <= CurrentVersion)
                return null;

            // Find the .exe asset
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                {
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    return (remoteVersion, tag, downloadUrl);
                }
            }
        }
        catch
        {
            // Network error, rate limit, etc — silently ignore
        }

        return null;
    }

    /// Download the new exe and replace the running one via a batch script that
    /// waits for this process to exit, swaps the files, and relaunches.
    public static async Task<bool> DownloadAndApplyAsync(string downloadUrl, Action<int>? onProgress = null)
    {
        try
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe)) return false;

            var dir = Path.GetDirectoryName(currentExe)!;
            var tempExe = Path.Combine(dir, ExeName + ".update");
            var oldExe = currentExe + ".old";
            var batchPath = Path.Combine(dir, "_update.cmd");

            // Download to temp file
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(tempExe);

            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (totalBytes > 0)
                    onProgress?.Invoke((int)(downloaded * 100 / totalBytes));
            }

            // Write a batch script that:
            //  1) Waits for this process to exit
            //  2) Renames current exe -> .old
            //  3) Renames downloaded update -> current exe name
            //  4) Deletes the .old file
            //  5) Relaunches the new exe
            //  6) Cleans itself up
            var script = $"""
                @echo off
                echo Updating SoulBeats Pro...
                :wait
                tasklist /FI "PID eq {Environment.ProcessId}" 2>NUL | find "{Environment.ProcessId}" >NUL
                if not errorlevel 1 (
                    timeout /t 1 /nobreak >NUL
                    goto wait
                )
                if exist "{oldExe}" del /f "{oldExe}"
                move /y "{currentExe}" "{oldExe}"
                move /y "{tempExe}" "{currentExe}"
                del /f "{oldExe}"
                start "" "{currentExe}"
                del /f "%~f0"
                """;

            await File.WriteAllTextAsync(batchPath, script);

            // Launch the updater script hidden and exit
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            Application.Exit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// Clean up leftover files from a previous update.
    public static void CleanupOldFiles()
    {
        try
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe)) return;

            var dir = Path.GetDirectoryName(currentExe)!;
            var oldExe = currentExe + ".old";
            var batch = Path.Combine(dir, "_update.cmd");

            if (File.Exists(oldExe)) File.Delete(oldExe);
            if (File.Exists(batch)) File.Delete(batch);
        }
        catch { }
    }
}
