using System.Diagnostics;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoulBeatsPro;

/// <summary>
/// Collects machine / runtime info that gets reported to the admin panel:
///  - Hardware fingerprint (motherboard, disk, MAC) → stable DeviceId
///  - GPU + driver info
///  - Windows uptime / user idle time
///  - Roblox process state (running, window title, place id if available)
///  - Public IP + ISP / ASN / country (ip-api.com, cached)
///  - Ping latency to Roblox
///
/// All methods are best-effort and swallow exceptions — a missing probe should
/// never break the app. Results are cached when the underlying source is slow
/// (WMI, HTTP) and fresh when it's cheap (TickCount, Process list).
/// </summary>
internal static class SystemProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    // ── Cached values ──────────────────────────────────────────

    private static string? _cachedMotherboardSerial;
    private static string? _cachedDiskSerial;
    private static string? _cachedMacHash;
    private static string? _cachedDeviceId;
    private static GpuInfo? _cachedGpu;
    private static IpInfo? _cachedIpInfo;

    // ── Hardware fingerprint ───────────────────────────────────

    public static string GetMotherboardSerial()
    {
        if (_cachedMotherboardSerial != null) return _cachedMotherboardSerial;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                var s = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(s) && s != "None" && s != "Default string")
                    return _cachedMotherboardSerial = s;
            }
        }
        catch { }
        return _cachedMotherboardSerial = "";
    }

    public static string GetDiskSerial()
    {
        if (_cachedDiskSerial != null) return _cachedDiskSerial;
        try
        {
            // Prefer the first fixed (non-removable) disk
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber, MediaType FROM Win32_DiskDrive WHERE MediaType LIKE 'Fixed%'");
            foreach (var obj in searcher.Get())
            {
                var s = obj["SerialNumber"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return _cachedDiskSerial = s;
            }
        }
        catch { }
        return _cachedDiskSerial = "";
    }

    public static string GetPrimaryMacHash()
    {
        if (_cachedMacHash != null) return _cachedMacHash;
        try
        {
            // Pick the first "real" up-and-running adapter that isn't a loopback or virtual.
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(n => !LooksVirtual(n.Description))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            var mac = nic?.GetPhysicalAddress().GetAddressBytes();
            if (mac == null || mac.Length == 0) return _cachedMacHash = "";

            var hash = SHA256.HashData(mac);
            return _cachedMacHash = Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch { return _cachedMacHash = ""; }
    }

    private static bool LooksVirtual(string description)
    {
        if (string.IsNullOrEmpty(description)) return false;
        var d = description.ToLowerInvariant();
        return d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v")
            || d.Contains("virtualbox") || d.Contains("vethernet") || d.Contains("loopback")
            || d.Contains("pseudo") || d.Contains("tap-") || d.Contains("wintun")
            || d.Contains("tailscale") || d.Contains("zerotier");
    }

    /// <summary>
    /// Stable 16-char device ID derived from motherboard + disk + MAC. Same machine
    /// always produces the same ID; different machines produce different IDs. Used
    /// by the admin panel to dedupe one friend running the macro on two PCs.
    /// </summary>
    public static string GetDeviceId()
    {
        if (_cachedDeviceId != null) return _cachedDeviceId;
        var seed = $"{GetMotherboardSerial()}|{GetDiskSerial()}|{GetPrimaryMacHash()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return _cachedDeviceId = Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    // ── GPU info ───────────────────────────────────────────────

    public sealed class GpuInfo
    {
        public string Name { get; init; } = "";
        public string DriverVersion { get; init; } = "";
        public string DriverDate { get; init; } = "";
        public string Ram { get; init; } = "";
    }

    // Name fragments that identify virtual/streaming/VR display adapters. These
    // enumerate in WMI alongside the real GPU on rigs with Quest Link, Parsec,
    // Sunshine/Moonlight, etc., and a dumb "first result" probe picks them.
    private static readonly string[] VirtualGpuNameFragments =
    [
        "meta",          // Meta Virtual Monitor (Quest Link)
        "parsec",        // Parsec virtual display driver
        "moonlight",     // Sunshine/Moonlight virtual display
        "virtual display",
        "virtual monitor",
        "idd",           // IddSampleDriver (indirect display driver)
        "usb display",   // DisplayLink
        "remote display"
    ];

    public static GpuInfo GetGpuInfo()
    {
        if (_cachedGpu != null) return _cachedGpu;
        try
        {
            // PNPDeviceID lets us tell a real discrete/integrated GPU apart from
            // a software display driver. Real GPUs enumerate under PCI\VEN_...;
            // virtual adapters (Quest Link, Parsec, IddSampleDriver) enumerate
            // under ROOT\ or SWD\ so we filter those out.
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID FROM Win32_VideoController");

            GpuInfo? fallback = null;
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "";
                var pnp = obj["PNPDeviceID"]?.ToString() ?? "";

                // Skip generic fallback adapters
                if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) continue;

                // Skip named-as-virtual adapters (Meta, Parsec, DisplayLink, etc.)
                bool isVirtualByName = false;
                foreach (var frag in VirtualGpuNameFragments)
                {
                    if (name.Contains(frag, StringComparison.OrdinalIgnoreCase))
                    {
                        isVirtualByName = true;
                        break;
                    }
                }
                if (isVirtualByName) continue;

                var driverVersion = obj["DriverVersion"]?.ToString()?.Trim() ?? "";
                var driverDateRaw = obj["DriverDate"]?.ToString() ?? "";
                var driverDate = "";
                if (driverDateRaw.Length >= 8)
                {
                    // WMI CIM date: yyyymmddHHMMSS.mmmmmm+UUU
                    driverDate = $"{driverDateRaw[..4]}-{driverDateRaw.Substring(4, 2)}-{driverDateRaw.Substring(6, 2)}";
                }

                var ram = "";
                if (uint.TryParse(obj["AdapterRAM"]?.ToString(), out var bytes))
                    ram = $"{bytes / (1024 * 1024 * 1024.0):F1} GB";

                var info = new GpuInfo
                {
                    Name = name,
                    DriverVersion = driverVersion,
                    DriverDate = driverDate,
                    Ram = ram
                };

                // Prefer PCI\-backed adapters (real hardware). Everything else
                // (ROOT\, SWD\, etc.) is kept as a last-ditch fallback in case
                // this is a weird system with no PCI GPU reported.
                if (pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                    return _cachedGpu = info;

                fallback ??= info;
            }

            if (fallback != null) return _cachedGpu = fallback;
        }
        catch { }
        return _cachedGpu = new GpuInfo();
    }

    // ── Uptime / idle ──────────────────────────────────────────

    public static long GetWindowsUptimeSeconds() => Environment.TickCount64 / 1000;

    public static string FormatDuration(long seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m {seconds % 60}s";
        if (seconds < 86400) return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
        return $"{seconds / 86400}d {(seconds % 86400) / 3600}h";
    }

    public static int GetIdleSeconds()
    {
        try
        {
            var lii = new NativeApi.LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeApi.LASTINPUTINFO>()
            };
            if (!NativeApi.GetLastInputInfo(ref lii)) return 0;
            // TickCount wraps every ~49 days but so does dwTime, and the subtraction
            // is correct modulo 2^32, so cast through uint to get the right delta.
            uint delta = (uint)Environment.TickCount - lii.dwTime;
            return (int)(delta / 1000);
        }
        catch { return 0; }
    }

    // ── Roblox process watch ──────────────────────────────────

    public sealed class RobloxStatus
    {
        public bool Running { get; init; }
        public string WindowTitle { get; init; } = "";
        public string PlaceId { get; init; } = "";
    }

    private static readonly string[] RobloxProcessNames = ["RobloxPlayerBeta", "Windows10Universal", "RobloxPlayer"];
    private static readonly Regex PlaceIdRegex = new(@"placeId[=:]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RobloxStatus GetRobloxStatus()
    {
        try
        {
            Process? proc = null;
            foreach (var name in RobloxProcessNames)
            {
                var found = Process.GetProcessesByName(name).FirstOrDefault();
                if (found != null) { proc = found; break; }
            }

            if (proc == null) return new RobloxStatus();

            var title = "";
            try { title = proc.MainWindowTitle ?? ""; } catch { }

            // Try to extract the place id from the process command line via WMI.
            var placeId = "";
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {proc.Id}");
                foreach (var obj in searcher.Get())
                {
                    var cmd = obj["CommandLine"]?.ToString() ?? "";
                    var m = PlaceIdRegex.Match(cmd);
                    if (m.Success) { placeId = m.Groups[1].Value; break; }
                }
            }
            catch { }

            return new RobloxStatus
            {
                Running = true,
                WindowTitle = title.Length > 120 ? title[..120] : title,
                PlaceId = placeId
            };
        }
        catch { return new RobloxStatus(); }
    }

    // ── Public IP / ISP / ASN / country ───────────────────────

    public sealed class IpInfo
    {
        public string PublicIp { get; init; } = "";
        public string Country { get; init; } = "";
        public string Region { get; init; } = "";
        public string City { get; init; } = "";
        public string Isp { get; init; } = "";
        public string AsNumber { get; init; } = "";
    }

    /// <summary>
    /// Looks up public IP + ISP + country from ip-api.com. Result is cached
    /// for the lifetime of the process — ISPs rarely change mid-session and
    /// the free endpoint is rate-limited to 45 req/min.
    /// </summary>
    public static async Task<IpInfo> GetIpInfoAsync()
    {
        if (_cachedIpInfo != null) return _cachedIpInfo;
        try
        {
            // fields= filters to the ones we need; saves bandwidth and keeps us
            // under the free rate limit. http (not https) is required on the free tier.
            var resp = await Http.GetStringAsync(
                "http://ip-api.com/json/?fields=status,country,regionName,city,isp,as,query");

            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() == "success")
            {
                return _cachedIpInfo = new IpInfo
                {
                    PublicIp = GetStr(root, "query"),
                    Country = GetStr(root, "country"),
                    Region = GetStr(root, "regionName"),
                    City = GetStr(root, "city"),
                    Isp = GetStr(root, "isp"),
                    AsNumber = GetStr(root, "as")
                };
            }
        }
        catch { }
        return _cachedIpInfo = new IpInfo();
    }

    private static string GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    // ── Roblox ping ───────────────────────────────────────────

    public static async Task<int> GetRobloxPingMsAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("www.roblox.com", 2000);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
        }
        catch { return -1; }
    }
}
