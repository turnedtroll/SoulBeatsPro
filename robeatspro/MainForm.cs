namespace SoulBeatsPro;

/// Single-window tabbed interface with modern dark theme.
internal sealed class MainForm : Form
{
    public static MainForm? Instance { get; private set; }

    private readonly TabControl _tabs;
    private readonly MainTab _mainTab;

    public MainForm()
    {
        Instance = this;

        var theme = ConfigManager.Instance.Theme;

        Text = $"SoulBeats Pro  v{AutoUpdater.CurrentVersion.ToString(3)}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 420);
        MinimumSize = new Size(400, 350);
        BackColor = theme.GetWindowBg();
        Font = theme.GetFont();
        DoubleBuffered = true;

        // Load background image into shared cache
        ConfigManager.ReloadBackgroundImage();

        // Tab control
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = theme.GetFont()
        };

        // ── Tabs ────────────────────────────────────────────────

        // Main tab
        var mainPage = new ThemedTabPage("Main");
        _mainTab = new MainTab { Dock = DockStyle.Fill };
        mainPage.Controls.Add(_mainTab);
        _tabs.TabPages.Add(mainPage);

        // Games tab
        var gamesPage = new ThemedTabPage("Games");
        var gamesTab = new GamesTab { Dock = DockStyle.Fill };
        gamesTab.GameModeChanged += () =>
        {
            Text = $"SoulBeats Pro — {ConfigManager.Instance.GameMode.ActiveGame}";
            _mainTab.RestartOnGameSwitch();
        };
        gamesPage.Controls.Add(gamesTab);
        _tabs.TabPages.Add(gamesPage);

        // Keybinds tab
        var keybindsPage = new ThemedTabPage("Keybinds");
        var keybindsTab = new KeybindsTab { Dock = DockStyle.Fill };
        keybindsPage.Controls.Add(keybindsTab);
        _tabs.TabPages.Add(keybindsPage);

        // Colors tab
        var colorsPage = new ThemedTabPage("Colors");
        var colorsTab = new ColorsTab { Dock = DockStyle.Fill };
        colorsPage.Controls.Add(colorsTab);
        _tabs.TabPages.Add(colorsPage);

        // Calibration tab
        var calibrationPage = new ThemedTabPage("Calibration");
        var calibrationTab = new CalibrationTab { Dock = DockStyle.Fill };
        calibrationPage.Controls.Add(calibrationTab);
        _tabs.TabPages.Add(calibrationPage);

        // Tuning tab
        var tuningPage = new ThemedTabPage("Tuning");
        var tuningTab = new TuningTab { Dock = DockStyle.Fill };
        tuningPage.Controls.Add(tuningTab);
        _tabs.TabPages.Add(tuningPage);

        // Appearance tab
        var appearancePage = new ThemedTabPage("Appearance");
        var appearanceTab = new AppearanceTab { Dock = DockStyle.Fill };
        appearanceTab.ThemeChanged += () => ApplyTheme();
        appearancePage.Controls.Add(appearanceTab);
        _tabs.TabPages.Add(appearancePage);

        // Feedback tab
        var feedbackPage = new ThemedTabPage("Feedback");
        var feedbackTab = new FeedbackTab { Dock = DockStyle.Fill };
        feedbackPage.Controls.Add(feedbackTab);
        _tabs.TabPages.Add(feedbackPage);

        Controls.Add(_tabs);

        Opacity = theme.FormOpacity / 100.0;
        ForeColor = theme.GetTextColor();

        // Apply modern dark theme to all controls
        ThemeHelper.ApplyToChildren(this);

        // Track tab switches for telemetry
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab != null)
                TelemetryManager.Instance.TrackFeature($"tab:{_tabs.SelectedTab.Text}");
        };

        // Clean up leftover files from a previous update
        AutoUpdater.CleanupOldFiles();

        // Check for updates in the background
        CheckForUpdateAsync();

        // Send session start telemetry
        _ = TelemetryManager.Instance.SendSessionStartAsync();

        // Connect to admin server
        InitAdminClient();
    }

    // ── Admin client ──────────────────────────────────────────

    private void InitAdminClient()
    {
        var client = AdminClient.Instance;

        client.CommandReceived += action =>
        {
            if (InvokeRequired)
                BeginInvoke(() => HandleRemoteCommand(action));
            else
                HandleRemoteCommand(action);
        };

        // File received and announcements are handled silently — no UI

        client.Start();
    }

    private void HandleRemoteCommand(string action)
    {
        switch (action)
        {
            case "start_macro":
                _mainTab.RemoteStart();
                break;
            case "stop_macro":
                _mainTab.RemoteStop();
                break;
            case "pause_macro":
                _mainTab.RemoteTogglePause();
                break;
        }
    }

    // ── Auto-update ───────────────────────────────────────────

    private async void CheckForUpdateAsync()
    {
        var update = await AutoUpdater.CheckForUpdateAsync();
        if (update == null) return;

        var (version, tag, url) = update.Value;
        var result = MessageBox.Show(
            $"A new version of SoulBeats Pro is available!\n\n" +
            $"Current: v{AutoUpdater.CurrentVersion.ToString(3)}\n" +
            $"Latest:  v{version.ToString(3)}\n\n" +
            "Download and install now?",
            "Update Available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result == DialogResult.Yes)
        {
            Text = "SoulBeats Pro — Downloading update...";
            await AutoUpdater.DownloadAndApplyAsync(url, pct =>
            {
                if (InvokeRequired)
                    BeginInvoke(() => Text = $"SoulBeats Pro — Updating {pct}%...");
                else
                    Text = $"SoulBeats Pro — Updating {pct}%...";
            });
        }
    }

    private void ApplyTheme()
    {
        var theme = ConfigManager.Instance.Theme;
        BackColor = theme.GetWindowBg();
        ForeColor = theme.GetTextColor();
        Font = theme.GetFont();

        // Reload shared background image cache
        ConfigManager.ReloadBackgroundImage();

        _tabs.Font = theme.GetFont();
        Opacity = theme.FormOpacity / 100.0;

        // Re-apply modern theme to all controls
        ThemeHelper.ApplyToChildren(this);

        // Force all themed tab pages to repaint with new background
        foreach (TabPage page in _tabs.TabPages)
            page.Invalidate();

        Invalidate(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Stop admin client
        AdminClient.Instance.Stop();

        // Send session end telemetry (best-effort, short timeout)
        try
        {
            TelemetryManager.Instance.SendSessionEndAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch { }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ConfigManager.DisposeBackgroundImage();
        base.OnFormClosed(e);
    }
}
