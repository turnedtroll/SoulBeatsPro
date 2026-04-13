using System.Text;
using System.Text.Json;

namespace SoulBeatsAdmin;

/// <summary>
/// Main admin dashboard with:
/// - Server management (start/stop TCP server)
/// - Live session viewer (connected clients)
/// - Remote control (start/stop/pause macro, request screenshot)
/// - File push (send files to clients)
/// - Announcements (broadcast to clients + save locally)
/// - Settings (server port, upload dir, password)
/// </summary>
internal sealed class DashboardForm : Form
{
    /// <summary>
    /// The macro version this admin panel considers "current". Clients on any
    /// older version are highlighted as stale in the Live Sessions view so you
    /// can tell at a glance who hasn't auto-updated.
    /// Bump this whenever you release a new macro version.
    /// </summary>
    private const string LatestExpectedVersion = "v1.0.9";

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulBeatsAdmin");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string AnnouncementsPath = Path.Combine(ConfigDir, "announcements.json");

    // Colors
    private static readonly Color BgDark = Color.FromArgb(0x1A, 0x1A, 0x2E);
    private static readonly Color BgSidebar = Color.FromArgb(0x14, 0x14, 0x24);
    private static readonly Color BgCard = Color.FromArgb(0x22, 0x22, 0x38);
    private static readonly Color BgInput = Color.FromArgb(0x1E, 0x1E, 0x30);
    private static readonly Color AccentGreen = Color.FromArgb(0x4C, 0xAF, 0x50);
    private static readonly Color AccentBlue = Color.FromArgb(0x21, 0x96, 0xF3);
    private static readonly Color AccentOrange = Color.FromArgb(0xFF, 0x98, 0x00);
    private static readonly Color AccentRed = Color.FromArgb(0xF4, 0x43, 0x36);
    private static readonly Color AccentPurple = Color.FromArgb(0x9C, 0x27, 0xB0);
    private static readonly Color TextPrimary = Color.FromArgb(0xE8, 0xE8, 0xF0);
    private static readonly Color TextSecondary = Color.FromArgb(0x90, 0x90, 0xA8);
    private static readonly Color TextMuted = Color.FromArgb(0x60, 0x60, 0x78);
    private static readonly Color SidebarHover = Color.FromArgb(0x20, 0x20, 0x36);
    private static readonly Color SidebarActive = Color.FromArgb(0x28, 0x28, 0x42);
    private static readonly Color BorderColor = Color.FromArgb(0x30, 0x30, 0x48);

    // ── State ─────────────────────────────────────────────────
    private readonly AdminServer _server = new();
    private int _serverPort;
    private string _authToken;
    // Optional public endpoint used when generating admin.dat. Lets the admin
    // point clients at a tunnel (e.g. playit.gg) or a port-forwarded WAN IP
    // instead of the auto-detected LAN IP. Empty host => use local IP.
    private string _publicHost;
    private int _publicPort;
    private readonly Panel _sidebar;
    private readonly Panel _contentPanel;
    private readonly Button[] _navButtons;
    private readonly Label _serverStatusLabel;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // Live Sessions controls
    private ListView? _clientListView;
    private Label? _lblClientDetail;

    // Screenshot viewer
    private PictureBox? _screenshotBox;

    // Log tail viewer — single floating window, reused per client
    private Form? _logTailForm;
    private TextBox? _logTailBox;
    private ConnectedClient? _logTailClient;

    // Remote file viewer — single floating window, reused per client.
    // Shows everything pushed to the currently-selected client's recv dir,
    // lets the admin hit Refresh to re-query or Open to shell-execute on the
    // client PC (used by the Discord-requested game-asset workflow).
    private Form? _pushedFilesForm;
    private ListView? _pushedFilesList;
    private Label? _pushedFilesStatus;
    private Label? _pushedFilesHeader;
    private ConnectedClient? _pushedFilesClient;

    public DashboardForm()
    {
        Directory.CreateDirectory(ConfigDir);
        var cfg = LoadConfig();
        _serverPort = cfg.serverPort;
        _authToken = cfg.authToken;
        _publicHost = cfg.publicHost;
        _publicPort = cfg.publicPort;

        Text = "SoulBeats Admin Panel";
        Size = new Size(1000, 680);
        MinimumSize = new Size(850, 550);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        // ── Sidebar ───────────────────────────────────────────
        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 200,
            BackColor = BgSidebar
        };
        Controls.Add(_sidebar);

        // Brand
        var brand = new Label
        {
            Text = "SoulBeats",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = AccentGreen,
            Location = new Point(16, 14),
            AutoSize = true
        };
        _sidebar.Controls.Add(brand);

        var brandSub = new Label
        {
            Text = "Admin Control Panel",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextMuted,
            Location = new Point(18, 40),
            AutoSize = true
        };
        _sidebar.Controls.Add(brandSub);

        // Server status in sidebar
        _serverStatusLabel = new Label
        {
            Text = "Server: OFFLINE",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = AccentRed,
            Location = new Point(18, 60),
            AutoSize = true
        };
        _sidebar.Controls.Add(_serverStatusLabel);

        var sep = new Panel { Location = new Point(16, 82), Size = new Size(168, 1), BackColor = BorderColor };
        _sidebar.Controls.Add(sep);

        // Nav buttons
        string[] sections = ["Dashboard", "Live Sessions", "Remote Control", "Push Files", "Announcements", "Settings"];
        _navButtons = new Button[sections.Length];

        int navY = 94;
        for (int i = 0; i < sections.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = $"   {sections[i]}",
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(8, navY),
                Size = new Size(184, 34),
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = TextSecondary,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Padding(8, 0, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = SidebarHover;
            btn.FlatAppearance.MouseDownBackColor = SidebarActive;
            btn.Click += (_, _) => ShowSection(idx);
            _sidebar.Controls.Add(btn);
            _navButtons[i] = btn;
            navY += 38;
        }

        // ── Content panel ─────────────────────────────────────
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            Padding = new Padding(24)
        };
        Controls.Add(_contentPanel);
        _contentPanel.BringToFront();

        // Wire server events
        _server.ClientConnected += c => SafeInvoke(() => OnClientChanged());
        _server.ClientDisconnected += c => SafeInvoke(() => OnClientChanged());
        _server.ClientUpdated += c => SafeInvoke(() => OnClientUpdated(c));
        _server.ScreenshotReceived += (c, img) => SafeInvoke(() => ShowScreenshot(c, img));
        _server.ClientRejected += reason => SafeInvoke(() =>
            _serverStatusLabel.Text = $"Rejected: {reason[..Math.Min(reason.Length, 40)]}");
        _server.ConfigSnapshotReceived += (c, json) => SafeInvoke(() => ShowConfigSnapshot(c, json));
        _server.LogLineReceived += (c, line) => SafeInvoke(() => AppendLogTail(c, line));
        _server.RobloxPingReceived += (c, ms) => SafeInvoke(() => OnClientUpdated(c));
        _server.PushedFilesReceived += c => SafeInvoke(() => OnPushedFilesReceived(c));
        _server.FileOpResultReceived += (c, r) => SafeInvoke(() => OnFileOpResult(c, r));

        // Refresh timer
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshClientList();
        _refreshTimer.Start();

        // Auto-start server
        StartServer();
        ShowSection(0);
    }

    private void SafeInvoke(Action action)
    {
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    // ── Server management ─────────────────────────────────────

    private void StartServer()
    {
        try
        {
            // Generate a token on first run so the user doesn't have to
            if (string.IsNullOrWhiteSpace(_authToken))
            {
                _authToken = Guid.NewGuid().ToString("N");
                SaveConfig(_serverPort, _authToken, _publicHost, _publicPort);
            }

            _server.Start(_serverPort, _authToken);
            _serverStatusLabel.Text = $"Server: PORT {_serverPort}";
            _serverStatusLabel.ForeColor = AccentGreen;
        }
        catch (Exception ex)
        {
            _serverStatusLabel.Text = $"Server: FAILED";
            _serverStatusLabel.ForeColor = AccentRed;
            MessageBox.Show($"Failed to start server: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Section switching ─────────────────────────────────────

    private void ShowSection(int index)
    {
        _contentPanel.Controls.Clear();
        _screenshotBox = null;

        for (int i = 0; i < _navButtons.Length; i++)
        {
            _navButtons[i].BackColor = i == index ? SidebarActive : Color.Transparent;
            _navButtons[i].ForeColor = i == index ? AccentGreen : TextSecondary;
            _navButtons[i].Font = i == index
                ? new Font("Segoe UI", 9.5f, FontStyle.Bold)
                : new Font("Segoe UI", 9.5f);
        }

        switch (index)
        {
            case 0: BuildDashboard(); break;
            case 1: BuildLiveSessions(); break;
            case 2: BuildRemoteControl(); break;
            case 3: BuildPushFiles(); break;
            case 4: BuildAnnouncements(); break;
            case 5: BuildSettings(); break;
        }
    }

    // ════════════════════════════════════════════════════════════
    // 0. DASHBOARD
    // ════════════════════════════════════════════════════════════

    private void BuildDashboard()
    {
        var header = MakeHeader("Dashboard", "Server overview and connected clients");
        _contentPanel.Controls.Add(header);

        int y = 70;
        var ip = AdminServer.GetLocalIp();

        // Stats cards
        var cards = new (string title, string value, Color color)[]
        {
            ("Server IP", ip, AccentBlue),
            ("Port", _serverPort.ToString(), AccentPurple),
            ("Clients Online", _server.ClientCount.ToString(), AccentGreen),
            ("Status", _server.IsRunning ? "ONLINE" : "OFFLINE", _server.IsRunning ? AccentGreen : AccentRed)
        };

        int cardX = 24;
        foreach (var (title, value, color) in cards)
        {
            var card = MakeCard(cardX, y, 160, 90);
            card.Controls.Add(new Panel { Location = new Point(0, 0), Size = new Size(4, 90), BackColor = color });
            card.Controls.Add(new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = color,
                Location = new Point(14, 14),
                AutoSize = true
            });
            card.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextSecondary,
                Location = new Point(14, 52),
                AutoSize = true
            });
            _contentPanel.Controls.Add(card);
            cardX += 174;
        }
        y += 110;

        // Connection info card
        var infoCard = MakeCard(24, y, 700, 170);
        infoCard.Controls.Add(new Label
        {
            Text = "Client Config (admin.dat)",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });
        infoCard.Controls.Add(new Label
        {
            Text = "Generate an encrypted admin.dat to place next to the macro exe.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextSecondary,
            Location = new Point(14, 32),
            AutoSize = true
        });

        // Public host/port fields — override the local IP for tunnels / port-forwarding.
        infoCard.Controls.Add(new Label
        {
            Text = "Public host (leave blank for LAN):",
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextSecondary,
            Location = new Point(14, 62),
            AutoSize = true
        });
        var txtPublicHost = new TextBox
        {
            Text = _publicHost,
            Location = new Point(14, 82),
            Size = new Size(280, 24),
            BackColor = BgInput,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f)
        };
        infoCard.Controls.Add(txtPublicHost);

        infoCard.Controls.Add(new Label
        {
            Text = "Public port:",
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextSecondary,
            Location = new Point(304, 62),
            AutoSize = true
        });
        var numPublicPort = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 65535,
            Value = _publicPort,
            Location = new Point(304, 82),
            Size = new Size(90, 24),
            BackColor = BgInput,
            ForeColor = TextPrimary,
            Font = new Font("Consolas", 9f)
        };
        infoCard.Controls.Add(numPublicPort);

        // Effective endpoint preview (updates as user types)
        var lblEffective = new Label
        {
            Font = new Font("Consolas", 9f),
            ForeColor = AccentOrange,
            Location = new Point(14, 116),
            Size = new Size(510, 18),
            AutoEllipsis = true
        };
        infoCard.Controls.Add(lblEffective);

        var lblTokenHint = new Label
        {
            Text = $"Token: {(_authToken.Length > 8 ? _authToken[..8] + "..." : _authToken)}",
            Font = new Font("Consolas", 8.5f),
            ForeColor = TextMuted,
            Location = new Point(14, 136),
            AutoSize = true
        };
        infoCard.Controls.Add(lblTokenHint);

        void UpdateEffective()
        {
            var host = txtPublicHost.Text.Trim();
            var port = (int)numPublicPort.Value;
            if (host.Length > 0 && port > 0)
                lblEffective.Text = $"Clients will connect to: {host}:{port}   (public)";
            else
                lblEffective.Text = $"Clients will connect to: {ip}:{_serverPort}   (LAN)";
        }
        UpdateEffective();
        txtPublicHost.TextChanged += (_, _) => UpdateEffective();
        numPublicPort.ValueChanged += (_, _) => UpdateEffective();

        var btnGenerate = MakeButton("Save admin.dat", 530, 10, 150, AccentGreen);
        var lblGenStatus = new Label
        {
            Text = "",
            Location = new Point(530, 48),
            Size = new Size(160, 86),
            ForeColor = AccentGreen,
            Font = new Font("Segoe UI", 8.5f)
        };
        infoCard.Controls.Add(lblGenStatus);

        btnGenerate.Click += (_, _) =>
        {
            // Persist public endpoint the moment the user saves an admin.dat
            _publicHost = txtPublicHost.Text.Trim();
            _publicPort = (int)numPublicPort.Value;
            SaveConfig(_serverPort, _authToken, _publicHost, _publicPort);

            // Decide which host/port the clients should dial
            var useHost = _publicHost.Length > 0 ? _publicHost : ip;
            var usePort = _publicHost.Length > 0 && _publicPort > 0 ? _publicPort : _serverPort;

            using var dlg = new SaveFileDialog
            {
                FileName = "admin.dat",
                Filter = "Encrypted Config|*.dat",
                Title = "Save admin.dat — place this next to SoulBeatsPro.exe"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        serverHost = useHost,
                        serverPort = usePort,
                        authToken = _authToken
                    });
                    var encrypted = AdminCrypto.Encrypt(json);
                    File.WriteAllBytes(dlg.FileName, encrypted);
                    lblGenStatus.ForeColor = AccentGreen;
                    lblGenStatus.Text = $"Saved:\n{Path.GetFileName(dlg.FileName)}\n{useHost}:{usePort}";
                }
                catch (Exception ex)
                {
                    lblGenStatus.ForeColor = AccentRed;
                    lblGenStatus.Text = $"Error: {ex.Message}";
                }
            }
        };
        infoCard.Controls.Add(btnGenerate);

        _contentPanel.Controls.Add(infoCard);
        y += 188;

        // Connected clients summary
        var clientCard = MakeCard(24, y, 700, 280);
        clientCard.Controls.Add(new Label
        {
            Text = "Connected Clients",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        _clientListView = CreateClientListView(12, 36, 676, 232);
        clientCard.Controls.Add(_clientListView);
        _contentPanel.Controls.Add(clientCard);
        RefreshClientList();
    }

    // ════════════════════════════════════════════════════════════
    // 1. LIVE SESSIONS
    // ════════════════════════════════════════════════════════════

    private void BuildLiveSessions()
    {
        var header = MakeHeader("Live Sessions", "Real-time view of all connected clients");
        _contentPanel.Controls.Add(header);

        _clientListView = CreateClientListView(24, 70, 720, 300);
        _contentPanel.Controls.Add(_clientListView);

        // Detail panel — grew to fit hardware fingerprint / GPU / ISP / Roblox blocks
        var detailCard = MakeCard(24, 384, 720, 340);
        detailCard.Controls.Add(new Label
        {
            Text = "Client Details",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        _lblClientDetail = new Label
        {
            Text = "Select a client above to see details",
            Font = new Font("Consolas", 9f),
            ForeColor = TextSecondary,
            Location = new Point(14, 36),
            Size = new Size(692, 292),
            AutoEllipsis = false
        };
        detailCard.Controls.Add(_lblClientDetail);
        _contentPanel.Controls.Add(detailCard);

        _clientListView.SelectedIndexChanged += (_, _) => RenderClientDetails(GetSelectedClient());

        RefreshClientList();
    }

    /// <summary>
    /// Renders the currently-selected client's details into the Live Sessions
    /// detail panel. Extracted from SelectedIndexChanged so heartbeat and
    /// Roblox-ping responses can trigger a live refresh without the user
    /// having to re-click the row.
    /// </summary>
    private void RenderClientDetails(ConnectedClient? selected)
    {
        if (_lblClientDetail == null || _lblClientDetail.IsDisposed) return;

        if (selected == null)
        {
            _lblClientDetail.Text = "Select a client above to see details";
            return;
        }

        var sb = new StringBuilder();

        // ── Identity
        sb.AppendLine($"User ID:    {selected.UserId}");
        sb.AppendLine($"Device ID:  {selected.DeviceId}");
        sb.AppendLine($"PC Name:    {selected.PcName}     OS User:  {selected.OsUser}");
        sb.AppendLine();

        // ── Network
        var isStale = !string.Equals(selected.Version, LatestExpectedVersion, StringComparison.OrdinalIgnoreCase);
        sb.AppendLine($"Version:    {selected.Version}{(isStale ? "  (STALE — latest is " + LatestExpectedVersion + ")" : "")}");
        sb.AppendLine($"IP:         {selected.RealIp}    Public: {selected.PublicIp}");
        sb.AppendLine($"Location:   {JoinNonEmpty(", ", selected.City, selected.Region, selected.Country)}");
        sb.AppendLine($"ISP:        {selected.Isp}    {selected.Asn}");
        var pingText = selected.RobloxPingMs >= 0 ? $"{selected.RobloxPingMs} ms" : "n/a";
        sb.AppendLine($"Roblox Ping: {pingText}");
        sb.AppendLine();

        // ── Hardware
        sb.AppendLine($"OS:         {selected.Os}");
        sb.AppendLine($"CPU:        {selected.Cpu}");
        sb.AppendLine($"RAM:        {selected.Ram}        Screen:   {selected.ScreenRes}");
        sb.AppendLine($"GPU:        {selected.GpuName}  ({selected.GpuRam})");
        sb.AppendLine($"GPU Driver: {selected.GpuDriverVersion}    {selected.GpuDriverDate}");
        sb.AppendLine($"MB Serial:  {selected.MotherboardSerial}");
        sb.AppendLine($"Disk Serial: {selected.DiskSerial}        MAC hash: {selected.MacHash}");
        sb.AppendLine();

        // ── Runtime
        sb.AppendLine($"Game:       {selected.GameMode}    Status: {selected.StatusText}");
        sb.AppendLine($"Connected:  {selected.ConnectedAt:HH:mm:ss}    Session:  {selected.SessionDuration}");
        sb.AppendLine($"Win Uptime: {FormatSeconds(selected.UptimeSeconds)}    Idle: {FormatSeconds(selected.IdleSeconds)}");
        sb.AppendLine($"Total Launches: {selected.TotalLaunches}    Installed: {selected.InstallDate}");

        var robloxLine = selected.RobloxRunning
            ? $"Roblox:     RUNNING  place={selected.RobloxPlaceId}  title=\"{selected.RobloxTitle}\""
            : "Roblox:     not running";
        sb.AppendLine(robloxLine);

        if (selected.FeatureUsage.Count > 0)
            sb.AppendLine($"Features:   {string.Join(", ", selected.FeatureUsage.Select(kv => $"{kv.Key}:{kv.Value}"))}");

        _lblClientDetail.Text = sb.ToString();
    }

    /// <summary>
    /// Called whenever a client's state changes (heartbeat, roblox ping reply,
    /// etc). Refreshes the list, and if the updated client is the one currently
    /// selected in the detail panel, re-renders it too.
    /// </summary>
    private void OnClientUpdated(ConnectedClient updated)
    {
        RefreshClientList();
        var selected = GetSelectedClient();
        if (selected != null && selected.Id == updated.Id)
            RenderClientDetails(selected);
    }

    // ════════════════════════════════════════════════════════════
    // 2. REMOTE CONTROL
    // ════════════════════════════════════════════════════════════

    private void BuildRemoteControl()
    {
        var header = MakeHeader("Remote Control", "Send commands to connected clients");
        _contentPanel.Controls.Add(header);

        _clientListView = CreateClientListView(24, 70, 720, 200);
        _contentPanel.Controls.Add(_clientListView);

        int y = 284;

        // Command buttons card — three rows (macro/diagnostics/files + gate)
        var cmdCard = MakeCard(24, y, 720, 164);
        cmdCard.Controls.Add(new Label
        {
            Text = "Commands (select a client above first, or applies to all)",
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextSecondary,
            Location = new Point(14, 8),
            AutoSize = true
        });

        // ── Row 1: macro control + screenshot
        var btnStart = MakeButton("Start Macro", 14, 34, 120, AccentGreen);
        btnStart.Click += (_, _) => SendCommandToSelected("start_macro");
        cmdCard.Controls.Add(btnStart);

        var btnStop = MakeButton("Stop Macro", 144, 34, 120, AccentRed);
        btnStop.Click += (_, _) => SendCommandToSelected("stop_macro");
        cmdCard.Controls.Add(btnStop);

        var btnPause = MakeButton("Toggle Pause", 274, 34, 120, AccentOrange);
        btnPause.Click += (_, _) => SendCommandToSelected("pause_macro");
        cmdCard.Controls.Add(btnPause);

        var btnScreenshot = MakeButton("Screenshot", 404, 34, 120, AccentBlue);
        btnScreenshot.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client != null)
                _server.RequestScreenshot(client);
            else
                MessageBox.Show("Select a client first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        cmdCard.Controls.Add(btnScreenshot);

        var btnKick = MakeButton("Kick", 534, 34, 80, AccentRed);
        btnKick.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client != null)
            {
                _server.KickClient(client);
                RefreshClientList();
            }
        };
        cmdCard.Controls.Add(btnKick);

        var btnStartAll = MakeButton("Start ALL", 624, 34, 82, AccentPurple);
        btnStartAll.Click += (_, _) => _server.BroadcastCommand("start_macro");
        cmdCard.Controls.Add(btnStartAll);

        // ── Row 2: diagnostics + gate
        var btnConfig = MakeButton("Get Config", 14, 78, 120, AccentBlue);
        btnConfig.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }
            _server.RequestConfig(client);
        };
        cmdCard.Controls.Add(btnConfig);

        var btnLogTail = MakeButton("Log Tail", 144, 78, 120, AccentBlue);
        btnLogTail.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }
            OpenLogTailFor(client);
        };
        cmdCard.Controls.Add(btnLogTail);

        var btnPing = MakeButton("Ping Roblox", 274, 78, 120, AccentBlue);
        btnPing.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }
            _server.RequestRobloxPing(client);
        };
        cmdCard.Controls.Add(btnPing);

        var btnRevoke = MakeButton("Revoke", 404, 78, 120, AccentRed);
        btnRevoke.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }

            var reason = Prompt("Revoke reason (shown to user):", "Revoke client", "Violated rules");
            if (reason == null) return;

            var confirm = MessageBox.Show(
                $"Revoke {client.PcName} ({client.UserId})?\n\nThis stops their macro and prevents relaunching.",
                "Confirm revoke", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            _server.RevokeClient(client, reason);
        };
        cmdCard.Controls.Add(btnRevoke);

        var btnUnrevoke = MakeButton("Un-revoke", 534, 78, 170, AccentGreen);
        btnUnrevoke.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }
            _server.SendFeatureGate(client, Array.Empty<string>(), revoked: false, reason: "");
        };
        cmdCard.Controls.Add(btnUnrevoke);

        // ── Row 3: remote file viewer (view what you've pushed, open it on the client)
        var btnRemoteFiles = MakeButton("Remote Files", 14, 122, 150, AccentPurple);
        btnRemoteFiles.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }
            OpenPushedFilesFor(client);
        };
        cmdCard.Controls.Add(btnRemoteFiles);

        cmdCard.Controls.Add(new Label
        {
            Text = "View & open files you've pushed to this client",
            Location = new Point(174, 128),
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic)
        });

        _contentPanel.Controls.Add(cmdCard);
        y += 180;

        // Screenshot viewer
        var ssCard = MakeCard(24, y, 720, 210);
        ssCard.Controls.Add(new Label
        {
            Text = "Screenshot Viewer",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        _screenshotBox = new PictureBox
        {
            Location = new Point(14, 36),
            Size = new Size(692, 162),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = BgInput,
            BorderStyle = BorderStyle.None
        };
        ssCard.Controls.Add(_screenshotBox);
        _contentPanel.Controls.Add(ssCard);

        RefreshClientList();
    }

    // ════════════════════════════════════════════════════════════
    // 3. PUSH FILES
    // ════════════════════════════════════════════════════════════

    private string? _pushFilePath;
    private Label? _lblPushFile;
    private Label? _lblPushStatus;
    private TextBox? _txtPushMessage;

    private void BuildPushFiles()
    {
        var header = MakeHeader("Push Files", "Send files directly to client computers");
        _contentPanel.Controls.Add(header);

        _clientListView = CreateClientListView(24, 70, 720, 180);
        _contentPanel.Controls.Add(_clientListView);

        var card = MakeCard(24, 264, 720, 190);

        card.Controls.Add(new Label
        {
            Text = "Push a File to Clients",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        var btnBrowse = MakeButton("Choose File...", 14, 38, 130, AccentBlue);
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "Select file to push" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _pushFilePath = dlg.FileName;
                var info = new FileInfo(dlg.FileName);
                _lblPushFile!.Text = $"{Path.GetFileName(dlg.FileName)}  ({FormatSize(info.Length)})";
                _lblPushFile.ForeColor = TextPrimary;
            }
        };
        card.Controls.Add(btnBrowse);

        _lblPushFile = new Label
        {
            Text = "No file selected",
            Location = new Point(154, 44),
            Size = new Size(552, 18),
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9f),
            AutoEllipsis = true
        };
        card.Controls.Add(_lblPushFile);

        card.Controls.Add(new Label
        {
            Text = "Message (optional):",
            Location = new Point(14, 72),
            AutoSize = true,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9f)
        });

        _txtPushMessage = new TextBox
        {
            Location = new Point(14, 92),
            Size = new Size(692, 26),
            BackColor = BgInput,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };
        card.Controls.Add(_txtPushMessage);

        var btnPushSelected = MakeButton("Push to Selected", 14, 130, 150, AccentGreen);
        btnPushSelected.Click += (_, _) => PushFileToSelected();
        card.Controls.Add(btnPushSelected);

        var btnPushAll = MakeButton("Push to ALL", 174, 130, 130, AccentPurple);
        btnPushAll.Click += (_, _) => PushFileToAll();
        card.Controls.Add(btnPushAll);

        var btnViewRemote = MakeButton("View Remote Files", 314, 130, 160, AccentBlue);
        btnViewRemote.Click += (_, _) =>
        {
            var client = GetSelectedClient();
            if (client == null) { WarnSelectFirst(); return; }
            OpenPushedFilesFor(client);
        };
        card.Controls.Add(btnViewRemote);

        _lblPushStatus = new Label
        {
            Text = "",
            Location = new Point(484, 136),
            Size = new Size(222, 36),
            ForeColor = AccentGreen,
            Font = new Font("Segoe UI", 8.5f),
            AutoEllipsis = true
        };
        card.Controls.Add(_lblPushStatus);

        _contentPanel.Controls.Add(card);
        RefreshClientList();
    }

    private void PushFileToSelected()
    {
        if (string.IsNullOrEmpty(_pushFilePath) || !File.Exists(_pushFilePath))
        {
            _lblPushStatus!.ForeColor = AccentRed;
            _lblPushStatus.Text = "Choose a file first.";
            return;
        }

        var client = GetSelectedClient();
        if (client == null)
        {
            _lblPushStatus!.ForeColor = AccentRed;
            _lblPushStatus.Text = "Select a client first.";
            return;
        }

        var fileBytes = File.ReadAllBytes(_pushFilePath);
        _server.SendFile(client, Path.GetFileName(_pushFilePath), fileBytes, _txtPushMessage?.Text ?? "");
        // The client will fire back a file_op_result; OnFileOpResult fills in
        // the real status (saved path, blocked, etc).
        _lblPushStatus!.ForeColor = TextSecondary;
        _lblPushStatus.Text = $"Sent to {client.PcName}, awaiting confirmation...";
    }

    private void PushFileToAll()
    {
        if (string.IsNullOrEmpty(_pushFilePath) || !File.Exists(_pushFilePath))
        {
            _lblPushStatus!.ForeColor = AccentRed;
            _lblPushStatus.Text = "Choose a file first.";
            return;
        }

        var fileBytes = File.ReadAllBytes(_pushFilePath);
        _server.BroadcastFile(Path.GetFileName(_pushFilePath), fileBytes, _txtPushMessage?.Text ?? "");
        _lblPushStatus!.ForeColor = TextSecondary;
        _lblPushStatus.Text = $"Sent to {_server.ClientCount} client(s), awaiting confirmation...";
    }

    // ════════════════════════════════════════════════════════════
    // 4. ANNOUNCEMENTS
    // ════════════════════════════════════════════════════════════

    private TextBox? _txtAnnouncement;
    private Label? _lblAnnounceStatus;
    private ListBox? _lstAnnouncements;

    private void BuildAnnouncements()
    {
        var header = MakeHeader("Announcements", "Broadcast messages to all connected clients");
        _contentPanel.Controls.Add(header);

        var card = MakeCard(24, 70, 720, 160);
        card.Controls.Add(new Label
        {
            Text = "New Announcement",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        _txtAnnouncement = new TextBox
        {
            Multiline = true,
            Location = new Point(14, 36),
            Size = new Size(692, 70),
            BackColor = BgInput,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = 2000
        };
        card.Controls.Add(_txtAnnouncement);

        var btnBroadcast = MakeButton("Broadcast to All", 14, 114, 160, AccentGreen);
        btnBroadcast.Click += BtnBroadcastAnnouncement_Click;
        card.Controls.Add(btnBroadcast);

        _lblAnnounceStatus = new Label
        {
            Text = "",
            Location = new Point(184, 120),
            AutoSize = true,
            ForeColor = AccentGreen,
            Font = new Font("Segoe UI", 9f)
        };
        card.Controls.Add(_lblAnnounceStatus);

        _contentPanel.Controls.Add(card);

        // History
        var histCard = MakeCard(24, 244, 720, 290);
        histCard.Controls.Add(new Label
        {
            Text = "Announcement History",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        _lstAnnouncements = new ListBox
        {
            Location = new Point(12, 36),
            Size = new Size(610, 242),
            BackColor = BgInput,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.None
        };
        RefreshAnnouncementList();
        histCard.Controls.Add(_lstAnnouncements);

        var btnDelete = MakeButton("Delete", 634, 36, 72, AccentRed);
        btnDelete.Click += (_, _) =>
        {
            if (_lstAnnouncements.SelectedIndex < 0) return;
            var list = LoadAnnouncements();
            if (_lstAnnouncements.SelectedIndex < list.Count)
            {
                list.RemoveAt(_lstAnnouncements.SelectedIndex);
                SaveAnnouncements(list);
                RefreshAnnouncementList();
            }
        };
        histCard.Controls.Add(btnDelete);

        _contentPanel.Controls.Add(histCard);
    }

    private void BtnBroadcastAnnouncement_Click(object? sender, EventArgs e)
    {
        var message = _txtAnnouncement!.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            _lblAnnounceStatus!.ForeColor = AccentRed;
            _lblAnnounceStatus.Text = "Type a message first.";
            return;
        }

        // Broadcast to all connected clients
        _server.BroadcastAnnouncement(message);

        // Save locally
        var list = LoadAnnouncements();
        list.Add((DateTime.Now.ToString("yyyy-MM-dd HH:mm"), message));
        SaveAnnouncements(list);

        _lblAnnounceStatus!.ForeColor = AccentGreen;
        _lblAnnounceStatus.Text = $"Broadcast to {_server.ClientCount} client(s)!";
        _txtAnnouncement.Clear();
        RefreshAnnouncementList();
    }

    // ════════════════════════════════════════════════════════════
    // 5. SETTINGS
    // ════════════════════════════════════════════════════════════

    private void BuildSettings()
    {
        var header = MakeHeader("Settings", "Configure your admin panel and server");
        _contentPanel.Controls.Add(header);

        // Server settings
        var srvCard = MakeCard(24, 70, 720, 110);
        srvCard.Controls.Add(new Label
        {
            Text = "Server Configuration",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        srvCard.Controls.Add(new Label
        {
            Text = "Port:",
            Location = new Point(14, 44),
            AutoSize = true,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9f)
        });

        var txtPort = new TextBox
        {
            Text = _serverPort.ToString(),
            Location = new Point(52, 40),
            Size = new Size(80, 26),
            BackColor = BgInput,
            ForeColor = Color.White,
            Font = new Font("Consolas", 10f),
            BorderStyle = BorderStyle.FixedSingle
        };
        srvCard.Controls.Add(txtPort);

        var lblIp = new Label
        {
            Text = $"Local IP: {AdminServer.GetLocalIp()}",
            Location = new Point(150, 44),
            AutoSize = true,
            ForeColor = AccentBlue,
            Font = new Font("Consolas", 9f)
        };
        srvCard.Controls.Add(lblIp);

        var btnRestart = MakeButton("Restart Server", 14, 72, 130, AccentOrange);
        btnRestart.Click += (_, _) =>
        {
            if (int.TryParse(txtPort.Text, out var port) && port is > 0 and < 65536)
            {
                _server.Stop();
                _serverPort = port;
                SaveConfig(_serverPort, _authToken, _publicHost, _publicPort);
                StartServer();
                btnRestart.Text = "Restarted!";
                Task.Delay(1500).ContinueWith(_ =>
                {
                    if (!btnRestart.IsDisposed) BeginInvoke(() => btnRestart.Text = "Restart Server");
                });
            }
        };
        srvCard.Controls.Add(btnRestart);

        var btnStop = MakeButton("Stop Server", 154, 72, 110, AccentRed);
        btnStop.Click += (_, _) =>
        {
            _server.Stop();
            _serverStatusLabel.Text = "Server: OFFLINE";
            _serverStatusLabel.ForeColor = AccentRed;
        };
        srvCard.Controls.Add(btnStop);

        _contentPanel.Controls.Add(srvCard);

        // Password change
        var pwCard = MakeCard(24, 196, 720, 140);
        pwCard.Controls.Add(new Label
        {
            Text = "Change Admin Password",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(14, 10),
            AutoSize = true
        });

        pwCard.Controls.Add(new Label { Text = "New:", Location = new Point(14, 42), AutoSize = true, ForeColor = TextSecondary });
        var txtNewPw = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(60, 38),
            Size = new Size(220, 26),
            BackColor = BgInput,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        pwCard.Controls.Add(txtNewPw);

        pwCard.Controls.Add(new Label { Text = "Confirm:", Location = new Point(14, 72), AutoSize = true, ForeColor = TextSecondary });
        var txtConfirmPw = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(80, 68),
            Size = new Size(200, 26),
            BackColor = BgInput,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        pwCard.Controls.Add(txtConfirmPw);

        var lblPwStatus = new Label { Text = "", Location = new Point(14, 106), AutoSize = true, ForeColor = AccentGreen };
        pwCard.Controls.Add(lblPwStatus);

        var btnChangePw = MakeButton("Change", 290, 38, 90, AccentOrange);
        btnChangePw.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtNewPw.Text) || txtNewPw.Text.Length < 4)
            { lblPwStatus.ForeColor = AccentRed; lblPwStatus.Text = "Min 4 characters."; return; }
            if (txtNewPw.Text != txtConfirmPw.Text)
            { lblPwStatus.ForeColor = AccentRed; lblPwStatus.Text = "Passwords do not match."; return; }
            LoginForm.ResetPassword(txtNewPw.Text);
            lblPwStatus.ForeColor = AccentGreen;
            lblPwStatus.Text = "Password changed!";
            txtNewPw.Clear();
            txtConfirmPw.Clear();
        };
        pwCard.Controls.Add(btnChangePw);
        _contentPanel.Controls.Add(pwCard);
    }

    // ── Client list helpers ───────────────────────────────────

    private ListView CreateClientListView(int x, int y, int w, int h)
    {
        var lv = new ListView
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = BgInput,
            ForeColor = TextSecondary,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.None,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };

        lv.Columns.Add("PC Name", 110);
        lv.Columns.Add("User", 80);
        lv.Columns.Add("Status", 70);
        lv.Columns.Add("FPS", 45);
        lv.Columns.Add("Duration", 75);
        lv.Columns.Add("Game", 90);
        lv.Columns.Add("Version", 65);
        lv.Columns.Add("Screen", 80);
        lv.Columns.Add("Last Seen", 70);

        return lv;
    }

    private void RefreshClientList()
    {
        if (_clientListView == null || _clientListView.IsDisposed) return;

        var clients = _server.Clients;
        var selectedId = GetSelectedClient()?.Id;

        _clientListView.BeginUpdate();
        _clientListView.Items.Clear();

        foreach (var c in clients)
        {
            var isOutdated = !string.IsNullOrEmpty(c.Version)
                && !string.Equals(c.Version, LatestExpectedVersion, StringComparison.OrdinalIgnoreCase);

            var item = new ListViewItem(c.PcName)
            {
                Tag = c.Id,
                ForeColor = c.IsStale ? TextMuted : (c.MacroActive ? AccentGreen : TextSecondary)
            };
            item.SubItems.Add(c.OsUser);
            item.SubItems.Add(c.StatusText);
            item.SubItems.Add(c.MacroRunning ? c.Fps.ToString() : "-");
            item.SubItems.Add(c.SessionDuration);
            item.SubItems.Add(c.GameMode);

            // Update-compliance: outdated versions rendered in orange so they
            // stand out without drowning the whole row.
            var versionCell = new ListViewItem.ListViewSubItem(item, c.Version)
            {
                ForeColor = isOutdated ? AccentOrange : item.ForeColor
            };
            item.SubItems.Add(versionCell);
            item.UseItemStyleForSubItems = false;

            item.SubItems.Add(c.ScreenRes);
            item.SubItems.Add(c.LastHeartbeat.ToLocalTime().ToString("HH:mm:ss"));
            _clientListView.Items.Add(item);

            if (c.Id == selectedId)
                item.Selected = true;
        }

        _clientListView.EndUpdate();
    }

    private ConnectedClient? GetSelectedClient()
    {
        if (_clientListView == null || _clientListView.SelectedItems.Count == 0) return null;
        var id = _clientListView.SelectedItems[0].Tag as string;
        return _server.Clients.FirstOrDefault(c => c.Id == id);
    }

    private void OnClientChanged() => RefreshClientList();

    private void SendCommandToSelected(string action)
    {
        var client = GetSelectedClient();
        if (client != null)
            _server.SendCommand(client, action);
        else
            _server.BroadcastCommand(action);
    }

    // ── Screenshot display ────────────────────────────────────

    private void ShowScreenshot(ConnectedClient client, string base64Image)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Image);
            using var ms = new MemoryStream(bytes);
            var img = Image.FromStream(ms);

            if (_screenshotBox != null && !_screenshotBox.IsDisposed)
            {
                var old = _screenshotBox.Image;
                _screenshotBox.Image = img;
                old?.Dispose();
            }
            else
            {
                // Open in new window if not on Remote Control tab
                var viewer = new Form
                {
                    Text = $"Screenshot — {client.PcName}",
                    ClientSize = new Size(800, 450),
                    BackColor = BgDark,
                    StartPosition = FormStartPosition.CenterParent
                };
                var pb = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = img
                };
                viewer.Controls.Add(pb);
                viewer.Show();
            }
        }
        catch { }
    }

    // ── Announcement persistence ──────────────────────────────

    private static List<(string date, string message)> LoadAnnouncements()
    {
        var list = new List<(string, string)>();
        if (!File.Exists(AnnouncementsPath)) return list;
        try
        {
            var json = File.ReadAllText(AnnouncementsPath);
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var date = el.GetProperty("date").GetString() ?? "";
                var msg = el.GetProperty("message").GetString() ?? "";
                list.Add((date, msg));
            }
        }
        catch { }
        return list;
    }

    private static void SaveAnnouncements(List<(string date, string message)> list)
    {
        var arr = list.Select(a => new { date = a.date, message = a.message }).ToArray();
        var json = JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AnnouncementsPath, json);
    }

    private void RefreshAnnouncementList()
    {
        if (_lstAnnouncements == null) return;
        _lstAnnouncements.Items.Clear();
        var list = LoadAnnouncements();
        foreach (var (date, message) in list)
            _lstAnnouncements.Items.Add($"[{date}]  {Truncate(message, 80)}");
        if (list.Count == 0)
            _lstAnnouncements.Items.Add("(no announcements)");
    }

    // ── Config persistence ────────────────────────────────────

    private static (int serverPort, string authToken, string publicHost, int publicPort) LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var port = root.TryGetProperty("serverPort", out var p) ? p.GetInt32() : 9500;
                var token = root.TryGetProperty("authToken", out var t) ? t.GetString() ?? "" : "";
                var pubHost = root.TryGetProperty("publicHost", out var ph) ? ph.GetString() ?? "" : "";
                var pubPort = root.TryGetProperty("publicPort", out var pp) ? pp.GetInt32() : 0;
                return (port, token, pubHost, pubPort);
            }
        }
        catch { }
        return (9500, "", "", 0);
    }

    private static void SaveConfig(int serverPort, string authToken, string publicHost, int publicPort)
    {
        var json = JsonSerializer.Serialize(new { serverPort, authToken, publicHost, publicPort });
        File.WriteAllText(ConfigPath, json);
    }

    // ── UI helpers ────────────────────────────────────────────

    private static Panel MakeHeader(string title, string subtitle)
    {
        var panel = new Panel
        {
            Location = new Point(24, 16),
            Size = new Size(700, 48),
            BackColor = Color.Transparent
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(0, 0),
            AutoSize = true
        });
        panel.Controls.Add(new Label
        {
            Text = subtitle,
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextMuted,
            Location = new Point(2, 28),
            AutoSize = true
        });
        return panel;
    }

    private static Panel MakeCard(int x, int y, int w, int h) => new()
    {
        Location = new Point(x, y),
        Size = new Size(w, h),
        BackColor = BgCard,
        BorderStyle = BorderStyle.None
    };

    private static Button MakeButton(string text, int x, int y, int w, Color bgColor)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = bgColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 3)] + "...";

    private static string FormatSeconds(long seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m {seconds % 60}s";
        if (seconds < 86400) return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
        return $"{seconds / 86400}d {(seconds % 86400) / 3600}h";
    }

    private static string JoinNonEmpty(string separator, params string[] parts)
        => string.Join(separator, parts.Where(s => !string.IsNullOrWhiteSpace(s)));

    private void WarnSelectFirst()
        => MessageBox.Show("Select a client first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>
    /// Tiny modal input dialog — WinForms doesn't ship one. Returns null on cancel.
    /// </summary>
    private static string? Prompt(string promptText, string title, string defaultValue)
    {
        using var form = new Form
        {
            Text = title,
            ClientSize = new Size(420, 130),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = BgCard,
            ForeColor = TextPrimary
        };
        var lbl = new Label
        {
            Text = promptText,
            Location = new Point(12, 12),
            Size = new Size(396, 20),
            ForeColor = TextSecondary
        };
        var tb = new TextBox
        {
            Text = defaultValue,
            Location = new Point(12, 38),
            Size = new Size(396, 24),
            BackColor = BgInput,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(232, 78),
            Size = new Size(80, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentGreen,
            ForeColor = Color.White
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(322, 78),
            Size = new Size(86, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = BgInput,
            ForeColor = TextPrimary
        };
        form.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }

    // ── Config snapshot viewer ───────────────────────────────

    private void ShowConfigSnapshot(ConnectedClient client, string json)
    {
        // Pretty-print so the dump is readable
        string pretty = json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { }

        var viewer = new Form
        {
            Text = $"Config snapshot — {client.PcName}",
            ClientSize = new Size(700, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = BgDark,
            ForeColor = TextPrimary
        };
        var tb = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5f),
            BackColor = BgInput,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.None,
            Text = pretty
        };
        viewer.Controls.Add(tb);
        viewer.Show();
    }

    // ── Log tail viewer ──────────────────────────────────────

    private void OpenLogTailFor(ConnectedClient client)
    {
        // If there's already a tail window open for a different client, stop
        // the old one before switching — the macro only supports one sink at a time.
        if (_logTailClient != null && _logTailClient.Id != client.Id)
        {
            try { _server.StopLogTail(_logTailClient); } catch { }
        }

        _logTailClient = client;

        if (_logTailForm == null || _logTailForm.IsDisposed)
        {
            _logTailForm = new Form
            {
                Text = $"Log tail — {client.PcName}",
                ClientSize = new Size(720, 480),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgDark,
                ForeColor = TextPrimary
            };
            _logTailBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                BackColor = BgInput,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };
            _logTailForm.Controls.Add(_logTailBox);
            _logTailForm.FormClosing += (_, _) =>
            {
                if (_logTailClient != null)
                {
                    try { _server.StopLogTail(_logTailClient); } catch { }
                    _logTailClient = null;
                }
                _logTailBox = null;
                _logTailForm = null;
            };
            _logTailForm.Show();
        }
        else
        {
            _logTailForm.Text = $"Log tail — {client.PcName}";
            _logTailBox!.Clear();
            _logTailForm.Focus();
        }

        _server.StartLogTail(client);
    }

    private void AppendLogTail(ConnectedClient client, string line)
    {
        if (_logTailForm == null || _logTailBox == null || _logTailForm.IsDisposed) return;
        // Only append lines from the currently-tailed client
        if (_logTailClient == null || _logTailClient.Id != client.Id) return;

        _logTailBox.AppendText(line + Environment.NewLine);
    }

    // ── Remote file viewer ───────────────────────────────────

    /// <summary>
    /// Opens (or reuses) the floating window that lists files we've pushed to
    /// the given client's recv directory. Hits the client with a
    /// list_pushed_files command and populates the list when the response
    /// comes back via <see cref="OnPushedFilesReceived"/>.
    /// </summary>
    private void OpenPushedFilesFor(ConnectedClient client)
    {
        _pushedFilesClient = client;

        if (_pushedFilesForm == null || _pushedFilesForm.IsDisposed)
        {
            _pushedFilesForm = new Form
            {
                Text = $"Remote Files — {client.PcName}",
                ClientSize = new Size(760, 500),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = BgDark,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 9.5f)
            };

            _pushedFilesHeader = new Label
            {
                Text = $"Files on {client.PcName} — fetching...",
                Location = new Point(12, 12),
                Size = new Size(736, 22),
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoEllipsis = true
            };
            _pushedFilesForm.Controls.Add(_pushedFilesHeader);

            _pushedFilesList = new ListView
            {
                Location = new Point(12, 40),
                Size = new Size(736, 380),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = BgInput,
                ForeColor = TextPrimary,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None
            };
            _pushedFilesList.Columns.Add("Name", 380);
            _pushedFilesList.Columns.Add("Size", 100);
            _pushedFilesList.Columns.Add("Modified", 240);
            // Double-click a row to open — matches Explorer muscle memory
            _pushedFilesList.DoubleClick += (_, _) => OpenSelectedRemoteFile();
            _pushedFilesForm.Controls.Add(_pushedFilesList);

            var btnRefresh = MakeButton("Refresh", 12, 434, 110, AccentBlue);
            btnRefresh.Click += (_, _) =>
            {
                if (_pushedFilesClient != null)
                {
                    if (_pushedFilesHeader != null)
                        _pushedFilesHeader.Text = $"Files on {_pushedFilesClient.PcName} — fetching...";
                    _server.RequestPushedFiles(_pushedFilesClient);
                }
            };
            _pushedFilesForm.Controls.Add(btnRefresh);

            var btnOpen = MakeButton("Open on Client", 132, 434, 150, AccentGreen);
            btnOpen.Click += (_, _) => OpenSelectedRemoteFile();
            _pushedFilesForm.Controls.Add(btnOpen);

            _pushedFilesStatus = new Label
            {
                Text = "",
                Location = new Point(294, 440),
                Size = new Size(454, 40),
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 9f),
                AutoEllipsis = true
            };
            _pushedFilesForm.Controls.Add(_pushedFilesStatus);

            _pushedFilesForm.FormClosing += (_, _) =>
            {
                _pushedFilesClient = null;
                _pushedFilesList = null;
                _pushedFilesStatus = null;
                _pushedFilesHeader = null;
                _pushedFilesForm = null;
            };
            _pushedFilesForm.Show();
        }
        else
        {
            _pushedFilesForm.Text = $"Remote Files — {client.PcName}";
            if (_pushedFilesHeader != null)
                _pushedFilesHeader.Text = $"Files on {client.PcName} — fetching...";
            _pushedFilesList?.Items.Clear();
            _pushedFilesForm.Focus();
        }

        // Ask the client for its current file list — the response fills in the UI.
        _server.RequestPushedFiles(client);
    }

    private void OpenSelectedRemoteFile()
    {
        if (_pushedFilesClient == null || _pushedFilesList == null) return;
        if (_pushedFilesList.SelectedItems.Count == 0)
        {
            if (_pushedFilesStatus != null)
            {
                _pushedFilesStatus.ForeColor = AccentOrange;
                _pushedFilesStatus.Text = "Select a file first.";
            }
            return;
        }

        var name = _pushedFilesList.SelectedItems[0].Text;
        if (string.IsNullOrWhiteSpace(name)) return;

        _server.OpenPushedFile(_pushedFilesClient, name);
        if (_pushedFilesStatus != null)
        {
            _pushedFilesStatus.ForeColor = TextSecondary;
            _pushedFilesStatus.Text = $"Requested open: {name}";
        }
    }

    /// <summary>
    /// Fires when a pushed_files response arrives from a client. If the viewer
    /// is currently showing that client, repopulate the list.
    /// </summary>
    private void OnPushedFilesReceived(ConnectedClient client)
    {
        if (_pushedFilesForm == null || _pushedFilesForm.IsDisposed) return;
        if (_pushedFilesClient == null || _pushedFilesClient.Id != client.Id) return;
        if (_pushedFilesList == null) return;

        _pushedFilesList.BeginUpdate();
        _pushedFilesList.Items.Clear();
        foreach (var f in client.PushedFiles)
        {
            var item = new ListViewItem(f.Name);
            item.SubItems.Add(FormatSize(f.Size));
            item.SubItems.Add(f.Modified == default ? "" : f.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            _pushedFilesList.Items.Add(item);
        }
        _pushedFilesList.EndUpdate();

        if (_pushedFilesHeader != null)
        {
            var dir = string.IsNullOrEmpty(client.PushedFilesDir) ? "(unknown dir)" : client.PushedFilesDir;
            _pushedFilesHeader.Text = $"{client.PcName} — {client.PushedFiles.Count} file(s)  |  {dir}";
        }
        if (_pushedFilesStatus != null && _pushedFilesList.Items.Count == 0)
        {
            _pushedFilesStatus.ForeColor = TextMuted;
            _pushedFilesStatus.Text = "(no files pushed yet — use Push Files to send one)";
        }
    }

    /// <summary>
    /// Fires when a client reports back about a push or open attempt. Used to
    /// drive the Push Files status label and the Remote Files viewer status
    /// line with real, actionable feedback — previously everything was silent.
    /// </summary>
    private void OnFileOpResult(ConnectedClient client, FileOpResult result)
    {
        // Remote Files viewer status (only when it's showing this client)
        if (_pushedFilesForm != null && !_pushedFilesForm.IsDisposed &&
            _pushedFilesClient != null && _pushedFilesClient.Id == client.Id &&
            _pushedFilesStatus != null)
        {
            if (result.Op == "open")
            {
                _pushedFilesStatus.ForeColor = result.Ok ? AccentGreen : AccentRed;
                _pushedFilesStatus.Text = result.Ok
                    ? $"Opened on client: {result.FileName}"
                    : $"Open failed ({result.FileName}): {result.Message}";
            }
            else if (result.Op == "push" && result.Ok)
            {
                // A successful push landed — refresh the list so the new file shows.
                _server.RequestPushedFiles(client);
            }
        }

        // Push Files tab status label (only when it exists — we might be on another tab)
        if (_lblPushStatus != null && !_lblPushStatus.IsDisposed && result.Op == "push")
        {
            if (result.Ok)
            {
                _lblPushStatus.ForeColor = AccentGreen;
                var savedAs = string.IsNullOrEmpty(result.SavedAs) ? result.FileName : result.SavedAs;
                _lblPushStatus.Text = $"Saved on {client.PcName} as {savedAs}\n{result.Dir}";
            }
            else
            {
                _lblPushStatus.ForeColor = AccentRed;
                _lblPushStatus.Text = $"{client.PcName}: {result.Message}";
            }
        }
    }

    // ── Cleanup ───────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _refreshTimer.Stop();
        _server.Dispose();
        base.OnFormClosing(e);
    }
}
