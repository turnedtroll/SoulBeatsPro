using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoulBeatsPro;

/// <summary>
/// Tracks usage data and sends it to a Discord webhook.
/// Also logs locally to %LOCALAPPDATA%\bigbart\telemetry\ as backup.
/// Webhook URL is loaded from Secrets.cs (gitignored).
/// </summary>
internal sealed class TelemetryManager
{
    private static string WebhookUrl => Secrets.DiscordWebhookUrl;

    private static readonly HttpClient Http = new();
    private static readonly string TelemetryDir = Path.Combine(ConfigManager.ConfigDir, "telemetry");
    private static readonly string SessionLogPath = Path.Combine(TelemetryDir, "sessions.jsonl");
    private static readonly string CrashLogPath = Path.Combine(TelemetryDir, "crashes.jsonl");
    private static readonly string FeedbackLogPath = Path.Combine(TelemetryDir, "feedback.jsonl");

    // NOTE: must be declared AFTER the path fields above. Static field initializers run in
    // declaration order, and the ctor touches TelemetryDir — if Instance came first, TelemetryDir
    // would still be null when Directory.CreateDirectory is called.
    public static TelemetryManager Instance { get; } = new();

    // ── Session state ──────────────────────────────────────────
    public string UserId { get; }
    public string PcName { get; }
    public string OsUser { get; }
    public string ScreenRes { get; }
    public int TotalSessions { get; }
    public string InstallDate { get; }
    public DateTime SessionStart { get; } = DateTime.UtcNow;
    public Dictionary<string, int> FeatureUsage { get; } = new();
    public List<string> EventLog { get; } = [];

    private static string UserIdPath => Path.Combine(ConfigManager.ConfigDir, "uid.txt");
    private static string StatsPath => Path.Combine(ConfigManager.ConfigDir, "stats.json");

    private TelemetryManager()
    {
        Directory.CreateDirectory(TelemetryDir);

        UserId = LoadOrCreateUserId();
        PcName = Environment.MachineName;
        OsUser = Environment.UserName;

        var screen = Screen.PrimaryScreen?.Bounds;
        ScreenRes = screen != null ? $"{screen.Value.Width}x{screen.Value.Height}" : "Unknown";

        var stats = LoadStats();
        TotalSessions = stats.totalSessions + 1;
        InstallDate = stats.installDate;
        SaveStats(TotalSessions, InstallDate);
    }

    // ── User ID ────────────────────────────────────────────────

    private static string LoadOrCreateUserId()
    {
        try
        {
            if (File.Exists(UserIdPath))
            {
                var id = File.ReadAllText(UserIdPath).Trim();
                if (id.Length > 0) return id;
            }
        }
        catch { }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
        var id2 = Convert.ToHexString(hash)[..12].ToLowerInvariant();

        try { File.WriteAllText(UserIdPath, id2); }
        catch { }

        return id2;
    }

    // ── Session stats persistence ──────────────────────────────

    private static (int totalSessions, string installDate) LoadStats()
    {
        try
        {
            if (File.Exists(StatsPath))
            {
                var json = File.ReadAllText(StatsPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var sessions = root.GetProperty("totalSessions").GetInt32();
                var install = root.GetProperty("installDate").GetString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
                return (sessions, install);
            }
        }
        catch { }

        return (0, DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    private static void SaveStats(int totalSessions, string installDate)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { totalSessions, installDate });
            File.WriteAllText(StatsPath, json);
        }
        catch { }
    }

    // ── System info ────────────────────────────────────────────

    public static string GetCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
                return obj["Name"]?.ToString()?.Trim() ?? "Unknown";
        }
        catch { }
        return "Unknown";
    }

    public static string GetTotalRam()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out var bytes))
                    return $"{bytes / (1024 * 1024 * 1024.0):F1} GB";
            }
        }
        catch { }
        return "Unknown";
    }

    // ── Feature tracking ───────────────────────────────────────

    public void TrackFeature(string feature)
    {
        if (FeatureUsage.ContainsKey(feature))
            FeatureUsage[feature]++;
        else
            FeatureUsage[feature] = 1;
    }

    public string GetSessionDuration()
    {
        var elapsed = DateTime.UtcNow - SessionStart;
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        return $"{elapsed.Seconds}s";
    }

    // ── Discord webhook messages ───────────────────────────────

    public async Task SendSessionStartAsync()
    {
        var embed = new
        {
            title = "Session Started",
            color = 0x4CAF50,
            fields = new object[]
            {
                new { name = "User ID", value = $"`{UserId}`", inline = true },
                new { name = "PC Name", value = PcName, inline = true },
                new { name = "OS User", value = OsUser, inline = true },
                new { name = "Version", value = $"v{AutoUpdater.CurrentVersion.ToString(3)}", inline = true },
                new { name = "Game Mode", value = ConfigManager.Instance.GameMode.ActiveGame, inline = true },
                new { name = "Session #", value = $"{TotalSessions}", inline = true },
                new { name = "OS", value = Environment.OSVersion.VersionString, inline = true },
                new { name = "Screen", value = ScreenRes, inline = true },
                new { name = ".NET", value = Environment.Version.ToString(), inline = true },
                new { name = "CPU", value = GetCpuName(), inline = false },
                new { name = "RAM", value = GetTotalRam(), inline = true },
                new { name = "Install Date", value = InstallDate, inline = true }
            },
            timestamp = DateTime.UtcNow.ToString("o")
        };

        await SendWebhookAsync(embed);
        LogLocal(SessionLogPath, new { type = "session_start", timestamp = DateTime.UtcNow.ToString("o"), userId = UserId, pcName = PcName });
        LogEvent("Session started");
    }

    public async Task SendSessionEndAsync()
    {
        var featureList = FeatureUsage.Count > 0
            ? string.Join("\n", FeatureUsage.Select(kv => $"{kv.Key}: **{kv.Value}x**"))
            : "None";

        var embed = new
        {
            title = "Session Ended",
            color = 0xFF9800,
            fields = new object[]
            {
                new { name = "User ID", value = $"`{UserId}`", inline = true },
                new { name = "PC Name", value = PcName, inline = true },
                new { name = "Duration", value = GetSessionDuration(), inline = true },
                new { name = "Version", value = $"v{AutoUpdater.CurrentVersion.ToString(3)}", inline = true },
                new { name = "Game Mode", value = ConfigManager.Instance.GameMode.ActiveGame, inline = true },
                new { name = "Session #", value = $"{TotalSessions}", inline = true },
                new { name = "Features Used", value = featureList, inline = false }
            },
            timestamp = DateTime.UtcNow.ToString("o")
        };

        await SendWebhookAsync(embed);
        LogLocal(SessionLogPath, new { type = "session_end", timestamp = DateTime.UtcNow.ToString("o"), userId = UserId, duration = GetSessionDuration(), features = FeatureUsage });
        LogEvent("Session ended");
    }

    public async Task SendCrashReportAsync(Exception ex)
    {
        var stackTrace = ex.StackTrace ?? "No stack trace";
        if (stackTrace.Length > 900) stackTrace = stackTrace[..900] + "...";

        // Grab the last 50 log lines so the crash report has context leading
        // up to the exception, not just the stack trace.
        var breadcrumbs = Log.GetBreadcrumbs(50);
        var breadcrumbText = breadcrumbs.Length > 0
            ? string.Join("\n", breadcrumbs)
            : "(none)";
        // Discord embed fields cap at 1024 chars
        if (breadcrumbText.Length > 1000) breadcrumbText = "..." + breadcrumbText[^997..];

        var embed = new
        {
            title = "Crash Report",
            color = 0xFF0000,
            fields = new object[]
            {
                new { name = "User ID", value = $"`{UserId}`", inline = true },
                new { name = "PC Name", value = PcName, inline = true },
                new { name = "Version", value = $"v{AutoUpdater.CurrentVersion.ToString(3)}", inline = true },
                new { name = "OS", value = Environment.OSVersion.VersionString, inline = true },
                new { name = "Screen", value = ScreenRes, inline = true },
                new { name = "Session Duration", value = GetSessionDuration(), inline = true },
                new { name = "Exception", value = $"`{ex.GetType().Name}: {ex.Message}`", inline = false },
                new { name = "Stack Trace", value = $"```\n{stackTrace}\n```", inline = false },
                new { name = "Breadcrumbs (last 50 log lines)", value = $"```\n{breadcrumbText}\n```", inline = false }
            },
            timestamp = DateTime.UtcNow.ToString("o")
        };

        await SendWebhookAsync(embed);
        LogLocal(CrashLogPath, new
        {
            type = "crash",
            timestamp = DateTime.UtcNow.ToString("o"),
            userId = UserId,
            exception = ex.Message,
            stackTrace = ex.StackTrace,
            breadcrumbs
        });
        LogEvent($"Crash: {ex.GetType().Name}");
    }

    public async Task SendFeedbackAsync(string message)
    {
        var embed = new
        {
            title = "User Feedback",
            color = 0x2196F3,
            fields = new object[]
            {
                new { name = "User ID", value = $"`{UserId}`", inline = true },
                new { name = "PC Name", value = PcName, inline = true },
                new { name = "Version", value = $"v{AutoUpdater.CurrentVersion.ToString(3)}", inline = true },
                new { name = "Session Duration", value = GetSessionDuration(), inline = true },
                new { name = "Game Mode", value = ConfigManager.Instance.GameMode.ActiveGame, inline = true },
                new { name = "Message", value = message, inline = false }
            },
            timestamp = DateTime.UtcNow.ToString("o")
        };

        await SendWebhookAsync(embed);
        LogLocal(FeedbackLogPath, new { type = "feedback", timestamp = DateTime.UtcNow.ToString("o"), userId = UserId, message });
        LogEvent("Feedback sent");
    }

    // ── Webhook transport ──────────────────────────────────────

    private async Task SendWebhookAsync(object embed)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl)) return;

        try
        {
            var payload = new { embeds = new[] { embed } };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await Http.PostAsync(WebhookUrl, content);
        }
        catch
        {
            // Silently fail — telemetry should never break the app
        }
    }

    // ── Local logging backup ───────────────────────────────────

    private static void LogLocal(string path, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);
            File.AppendAllText(path, json + Environment.NewLine);
        }
        catch { }
    }

    private void LogEvent(string msg)
    {
        EventLog.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }
}
