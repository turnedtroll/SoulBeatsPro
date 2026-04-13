using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoulBeatsPro;

/// <summary>
/// Loads admin connection settings from an encrypted config that is
/// embedded inside the exe as a resource. On first run the encrypted
/// blob is extracted to a hidden folder with a machine-unique name
/// so it cannot be casually discovered.
/// </summary>
internal sealed class AdminConfig
{
    public string ServerHost { get; init; } = "";
    public int ServerPort { get; init; } = 9500;
    public string AuthToken { get; init; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerHost) && !string.IsNullOrWhiteSpace(AuthToken);

    /// Hidden folder unique to this machine — lives in LocalAppData
    /// with a name derived from the hardware so it looks like a
    /// system cache folder and is different on every PC.
    internal static readonly string HiddenDir = BuildHiddenDir();
    private static readonly string HiddenPath = Path.Combine(HiddenDir, "cache.db");

    // NOTE: Instance must be declared AFTER HiddenDir/HiddenPath. Static field initializers
    // run in textual order — Load() reads HiddenPath and HiddenDir, so those must be
    // assigned before Load() runs, otherwise the extraction silently fails with a null path
    // and IsConfigured ends up false (which kills the admin TCP connection).
    public static AdminConfig Instance { get; } = Load();

    private static string BuildHiddenDir()
    {
        // Derive a folder name from machine-specific info so it's
        // consistent across runs but unique per PC
        var seed = $"{Environment.MachineName}|{Environment.UserName}|SBP";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var id = Convert.ToHexString(hash)[..16].ToLowerInvariant();
        // Looks like a system cache folder: "mscache_a3f2b1c9e8d04f17"
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            $"mscache_{id}");
    }

    private static AdminConfig Load()
    {
        try
        {
            // Always extract the embedded config so auto-updates
            // deliver the latest server host / auth token
            ExtractEmbeddedConfig();

            if (File.Exists(HiddenPath))
            {
                var encrypted = File.ReadAllBytes(HiddenPath);
                var json = AdminCrypto.Decrypt(encrypted);
                return JsonSerializer.Deserialize<AdminConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new AdminConfig();
            }
        }
        catch { }
        return new AdminConfig();
    }

    private static void ExtractEmbeddedConfig()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SoulBeatsPro.admin.dat");
            if (stream == null) return; // no admin.dat was embedded in this build

            Directory.CreateDirectory(HiddenDir);

            // Write the encrypted blob
            using (var fs = File.Create(HiddenPath))
                stream.CopyTo(fs);

            // Hide the folder and file
            try
            {
                new DirectoryInfo(HiddenDir).Attributes |= FileAttributes.Hidden | FileAttributes.System;
                File.SetAttributes(HiddenPath, FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
        }
        catch { }
    }
}

/// <summary>
/// AES-256 encryption for admin config files.
/// Shared between the macro (decrypt) and admin panel (encrypt).
/// </summary>
internal static class AdminCrypto
{
    private static byte[] DeriveKey()
    {
        var parts = new[] { "SoulB", "eats", "Pro", "#Adm", "in@", "2024", "!Sec", "ure" };
        var combined = string.Concat(parts);
        var salt = Encoding.UTF8.GetBytes("SBP_AdminConfig_Salt_v1");
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(combined), salt, 100_000, HashAlgorithmName.SHA256, 32);
    }

    public static byte[] Encrypt(string plainText)
    {
        var key = DeriveKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV);

        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }

        return ms.ToArray();
    }

    public static string Decrypt(byte[] cipherData)
    {
        var key = DeriveKey();
        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[16];
        Array.Copy(cipherData, 0, iv, 0, 16);
        aes.IV = iv;

        using var ms = new MemoryStream(cipherData, 16, cipherData.Length - 16);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }
}
