namespace SoulBeatsPro;

/// Main tab — run/stop macro, status display, screenshot, quick color picking.
internal sealed class MainTab : UserControl
{
    private readonly Button _runBtn;
    private readonly Button _stopBtn;
    private readonly Button _screenshotBtn;
    private readonly CheckBox _debugCheck;
    private readonly Label _statusLabel;
    private readonly Label _fpsLabel;
    private readonly Label _fpsWarnLabel;
    private readonly Label[] _laneLabels = new Label[4];
    private readonly GroupBox _laneGroup;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _fpsTimer;

    // Quick-change color controls
    private readonly Panel _tapColorSwatch;
    private readonly Button _tapColorPickBtn;
    private readonly Label _tapColorRgb;
    private readonly Panel _holdColorSwatch;
    private readonly Button _holdColorPickBtn;

    private MacroEngine? _engine;
    private DebugForm? _debugForm;
    private double _lastPToggle;
    private double _lastScreenshotToggle;
    private LayoutScaler? _scaler;

    public MainTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var font = new Font("MS Sans Serif", 8);
        var boldFont = new Font("MS Sans Serif", 8, FontStyle.Bold);
        var bigFont = new Font("MS Sans Serif", 10, FontStyle.Bold);

        int y = 12;

        // ── Title ──────────────────────────────────────────────────
        var title = new Label
        {
            Text = "SoulBeats Pro",
            Font = bigFont,
            AutoSize = true,
            Location = new Point(14, y),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(title);
        y += 28;

        // ── Run / Stop buttons ─────────────────────────────────────
        _runBtn = new Button
        {
            Text = "Run Macro",
            FlatStyle = FlatStyle.Standard,
            Size = new Size(110, 28),
            Location = new Point(14, y),
            Font = font,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _runBtn.Click += RunBtn_Click;
        Controls.Add(_runBtn);

        _stopBtn = new Button
        {
            Text = "Stop",
            FlatStyle = FlatStyle.Standard,
            Size = new Size(80, 28),
            Location = new Point(130, y),
            Enabled = false,
            Font = font,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _stopBtn.Click += StopBtn_Click;
        Controls.Add(_stopBtn);
        y += 36;

        // ── Status ─────────────────────────────────────────────────
        _statusLabel = new Label
        {
            Text = "Status: IDLE",
            Font = boldFont,
            AutoSize = true,
            Location = new Point(14, y),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(_statusLabel);
        y += 20;

        _fpsLabel = new Label
        {
            Text = "FPS: --",
            Font = new Font("MS Sans Serif", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Location = new Point(14, y),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(_fpsLabel);
        y += 24;

        // ── Debug checkbox ─────────────────────────────────────────
        _debugCheck = new CheckBox
        {
            Text = "Open debug window on start",
            Font = font,
            AutoSize = true,
            Location = new Point(14, y),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(_debugCheck);
        y += 28;

        // ── Lane states group ──────────────────────────────────────
        _laneGroup = new GroupBox
        {
            Text = "Lane States",
            Font = font,
            Location = new Point(10, y),
            Size = new Size(280, 85),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        Controls.Add(_laneGroup);

        int laneSpacing = 16;
        for (int i = 0; i < 4; i++)
        {
            _laneLabels[i] = new Label
            {
                Text = $"{MacroEngine.LaneNames[i]}: IDLE",
                Font = font,
                AutoSize = true,
                Location = new Point(12, 18 + i * laneSpacing),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _laneGroup.Controls.Add(_laneLabels[i]);
        }
        y += 95;

        // ── Screenshot button ──────────────────────────────────────
        _screenshotBtn = new Button
        {
            Text = "Screenshot",
            FlatStyle = FlatStyle.Standard,
            Size = new Size(100, 26),
            Location = new Point(14, y),
            Font = font,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _screenshotBtn.Click += ScreenshotBtn_Click;
        Controls.Add(_screenshotBtn);
        y += 34;

        // ── Quick-change: Tap Color ────────────────────────────────
        var tapColorLabel = new Label
        {
            Text = "Tap Color:",
            Font = font,
            AutoSize = true,
            Location = new Point(14, y + 2),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(tapColorLabel);

        _tapColorSwatch = new Panel
        {
            Size = new Size(20, 20),
            Location = new Point(80, y),
            BorderStyle = BorderStyle.Fixed3D,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(_tapColorSwatch);

        _tapColorPickBtn = new Button
        {
            Text = "Pick",
            Font = font,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(50, 22),
            Location = new Point(106, y),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _tapColorPickBtn.Click += TapColorPickBtn_Click;
        Controls.Add(_tapColorPickBtn);

        _tapColorRgb = new Label
        {
            Text = "",
            Font = font,
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(162, y + 2),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(_tapColorRgb);
        y += 28;

        // ── Quick-change: Hold Color ───────────────────────────────
        var holdColorLabel = new Label
        {
            Text = "Hold Color:",
            Font = font,
            AutoSize = true,
            Location = new Point(14, y + 2),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(holdColorLabel);

        _holdColorSwatch = new Panel
        {
            Size = new Size(20, 20),
            Location = new Point(80, y),
            BorderStyle = BorderStyle.Fixed3D,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(_holdColorSwatch);

        _holdColorPickBtn = new Button
        {
            Text = "Pick",
            Font = font,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(50, 22),
            Location = new Point(106, y),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        _holdColorPickBtn.Click += HoldColorPickBtn_Click;
        Controls.Add(_holdColorPickBtn);
        y += 34;

        // ── Help text ──────────────────────────────────────────────
        var help = new Label
        {
            Text = "Hotkeys are configurable in the Keybinds tab",
            Font = font,
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(14, y),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(help);
        y += 24;

        // ── Version + check for updates ───────────────────────────
        var versionLabel = new Label
        {
            Text = $"v{AutoUpdater.CurrentVersion.ToString(3)}",
            Font = font,
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(14, y),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Controls.Add(versionLabel);

        var updateBtn = new Button
        {
            Text = "Check for Updates",
            Font = font,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(120, 24),
            Location = new Point(80, y - 2),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        updateBtn.Click += async (_, _) =>
        {
            updateBtn.Enabled = false;
            updateBtn.Text = "Checking...";
            var update = await AutoUpdater.CheckForUpdateAsync();
            if (update == null)
            {
                updateBtn.Text = "Up to date!";
                await Task.Delay(2000);
                updateBtn.Text = "Check for Updates";
                updateBtn.Enabled = true;
                return;
            }
            var (version, tag, url) = update.Value;
            var result = MessageBox.Show(
                $"New version v{version.ToString(3)} available!\nDownload and install?",
                "Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes)
            {
                updateBtn.Text = "Downloading...";
                await AutoUpdater.DownloadAndApplyAsync(url, pct =>
                {
                    if (InvokeRequired)
                        BeginInvoke(() => updateBtn.Text = $"Updating {pct}%...");
                    else
                        updateBtn.Text = $"Updating {pct}%...";
                });
            }
            else
            {
                updateBtn.Text = "Check for Updates";
                updateBtn.Enabled = true;
            }
        };
        Controls.Add(updateBtn);

        // ── FPS warning label (bottom-left) ────────────────────────
        _fpsWarnLabel = new Label
        {
            Text = "",
            Font = new Font("MS Sans Serif", 8f),
            AutoSize = false,
            Size = new Size(360, 32),
            Location = new Point(14, y + 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = Color.FromArgb(255, 100, 100),
            Visible = false
        };
        Controls.Add(_fpsWarnLabel);

        // ── Initial swatch colors ──────────────────────────────────
        UpdateTapSwatch();
        UpdateHoldSwatch();

        // ── Timer for polling state ────────────────────────────────
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        // ── FPS update timer (1 Hz) ────────────────────────────────
        _fpsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _fpsTimer.Tick += FpsTimer_Tick;
        _fpsTimer.Start();
    }

    // ── Swatch helpers ─────────────────────────────────────────────

    private void UpdateTapSwatch()
    {
        var nc = ConfigManager.Instance.Detection.NoteColor;
        if (nc.PickedR >= 0 && nc.PickedG >= 0 && nc.PickedB >= 0)
            _tapColorSwatch.BackColor = Color.FromArgb(nc.PickedR, nc.PickedG, nc.PickedB);
        else
            _tapColorSwatch.BackColor = Color.FromArgb(
                Math.Clamp((nc.MinR + 255) / 2, 0, 255),
                Math.Clamp((nc.MinG + 255) / 2, 0, 255),
                Math.Clamp(nc.MaxB / 2, 0, 255));
    }

    private void UpdateHoldSwatch()
    {
        var hc = ConfigManager.Instance.Detection.HoldColor;
        if (hc.PickedR >= 0 && hc.PickedG >= 0 && hc.PickedB >= 0)
            _holdColorSwatch.BackColor = Color.FromArgb(hc.PickedR, hc.PickedG, hc.PickedB);
        else
            _holdColorSwatch.BackColor = Color.FromArgb(
                Math.Clamp((hc.MinR + hc.MaxR) / 2, 0, 255),
                Math.Clamp((hc.MinG + hc.MaxG) / 2, 0, 255),
                Math.Clamp(hc.MaxB / 2, 0, 255));
    }

    // ── Quick-change color pick handlers ───────────────────────────

    private void TapColorPickBtn_Click(object? sender, EventArgs e)
    {
        using var picker = new ScreenPicker();
        if (picker.ShowDialog() == DialogResult.OK && picker.PickedColor != null)
        {
            var c = picker.PickedColor.Value;
            var nc = ConfigManager.Instance.Detection.NoteColor;
            nc.MinR = Math.Max(0, c.R - 30);
            nc.MinG = Math.Max(0, c.G - 30);
            nc.MaxB = Math.Min(255, c.B + 30);
            nc.PickedR = c.R;
            nc.PickedG = c.G;
            nc.PickedB = c.B;
            _tapColorSwatch.BackColor = c;
            _tapColorRgb.Text = $"R:{c.R} G:{c.G} B:{c.B}";
            ConfigManager.Instance.SaveSettings();
        }
    }

    private void HoldColorPickBtn_Click(object? sender, EventArgs e)
    {
        using var picker = new ScreenPicker();
        if (picker.ShowDialog() == DialogResult.OK && picker.PickedColor != null)
        {
            var c = picker.PickedColor.Value;
            var hc = ConfigManager.Instance.Detection.HoldColor;
            hc.MinR = Math.Max(0, c.R - 40);
            hc.MaxR = Math.Min(255, c.R + 40);
            hc.MinG = Math.Max(0, c.G - 40);
            hc.MaxG = Math.Min(255, c.G + 40);
            hc.MaxB = Math.Min(255, c.B + 30);
            hc.MinRG = Math.Max(0, c.R + c.G - 30);
            hc.PickedR = c.R;
            hc.PickedG = c.G;
            hc.PickedB = c.B;
            _holdColorSwatch.BackColor = c;
            ConfigManager.Instance.SaveSettings();
        }
    }

    // ── Remote control methods ────────────────────────────────────

    public void RemoteStart() => RunBtn_Click(null, EventArgs.Empty);
    public void RemoteStop() => StopBtn_Click(null, EventArgs.Empty);

    public void RemoteTogglePause()
    {
        if (_engine != null && _engine.Running)
            _engine.Active = !_engine.Active;
    }

    // ── Run / Stop ─────────────────────────────────────────────────

    private void RunBtn_Click(object? sender, EventArgs e)
    {
        if (_engine != null && _engine.Running) return;

        // Update lane scans from current config
        NativeApi.UpdateLaneScans(ConfigManager.Instance.Keybinds.LaneKeys);

        TelemetryManager.Instance.TrackFeature("macro_run");

        _engine = new MacroEngine();
        _engine.OnStopped += () =>
        {
            if (InvokeRequired)
                BeginInvoke(OnEngineStopped);
            else
                OnEngineStopped();
        };

        _engine.Start();

        if (_debugCheck.Checked)
        {
            _debugForm = new DebugForm(_engine);
            _debugForm.Show();
        }

        _runBtn.Enabled = false;
        _stopBtn.Enabled = true;
    }

    private void StopBtn_Click(object? sender, EventArgs e)
    {
        _engine?.Stop();
    }

    private void OnEngineStopped()
    {
        _debugForm?.Close();
        _debugForm = null;
        _runBtn.Enabled = true;
        _stopBtn.Enabled = false;
        _statusLabel.Text = "Status: IDLE";
        _statusLabel.ForeColor = ConfigManager.Instance.Theme.GetTextColor();
        for (int i = 0; i < 4; i++)
            _laneLabels[i].Text = $"{MacroEngine.LaneNames[i]}: IDLE";
    }

    // ── Timer tick ─────────────────────────────────────────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_engine == null || !_engine.Running) return;

        // Status
        if (_engine.Active)
        {
            _statusLabel.Text = "Status: ACTIVE";
            _statusLabel.ForeColor = Color.Green;
        }
        else
        {
            _statusLabel.Text = "Status: PAUSED";
            _statusLabel.ForeColor = Color.FromArgb(200, 60, 60);
        }

        // Lane states
        for (int i = 0; i < 4; i++)
        {
            var s = _engine.States[i];
            _laneLabels[i].Text = $"{MacroEngine.LaneNames[i]}: {s}";
            _laneLabels[i].ForeColor = s != MacroEngine.LaneState.Idle
                ? MacroEngine.LaneColors[i] : ConfigManager.Instance.Theme.GetTextColor();
        }

        // Debug toggle hotkey
        var cfg = ConfigManager.Instance.Keybinds;
        double now = Environment.TickCount64 / 1000.0;

        if (NativeApi.IsKeyDown(cfg.Debug) && now - _lastPToggle > 0.3)
        {
            _lastPToggle = now;
            if (_debugForm == null || _debugForm.IsDisposed)
            {
                _debugForm = new DebugForm(_engine);
                _debugForm.Show();
            }
            else
            {
                _debugForm.Close();
                _debugForm = null;
            }
        }

        // Screenshot hotkey
        if (NativeApi.IsKeyDown(cfg.Screenshot) && now - _lastScreenshotToggle > 0.5)
        {
            _lastScreenshotToggle = now;
            TakeScreenshot();
        }

        // Quit hotkey
        if (NativeApi.IsKeyDown(cfg.Quit))
        {
            _engine.Stop();
        }
    }

    // ── FPS timer tick ─────────────────────────────────────────────

    private void FpsTimer_Tick(object? sender, EventArgs e)
    {
        var engine = MacroEngine.CurrentInstance;
        if (engine == null || !engine.Running)
        {
            _fpsLabel.Text = "FPS: --";
            _fpsLabel.ForeColor = Color.FromArgb(160, 160, 170);
            _fpsWarnLabel.Visible = false;
            return;
        }

        int fps = engine.Fps;
        _fpsLabel.Text = $"FPS: {fps}";

        if (fps >= 200)
        {
            _fpsLabel.ForeColor = Color.FromArgb(80, 220, 120);    // green
            _fpsWarnLabel.Visible = false;
        }
        else if (fps >= 120)
        {
            _fpsLabel.ForeColor = Color.FromArgb(230, 200, 80);    // yellow
            _fpsWarnLabel.Visible = false;
        }
        else
        {
            _fpsLabel.ForeColor = Color.FromArgb(255, 80, 80);     // red
            _fpsWarnLabel.Text =
                "Warning: FPS below 120 — macro may miss notes.\n" +
                "Close background apps or lower Roblox graphics to Quality 1.";
            _fpsWarnLabel.Visible = true;
        }
    }

    // ── Screenshot ─────────────────────────────────────────────────

    private void ScreenshotBtn_Click(object? sender, EventArgs e) => TakeScreenshot();

    private void TakeScreenshot()
    {
        TelemetryManager.Instance.TrackFeature("screenshot");
        try
        {
            var bounds = Screen.PrimaryScreen!.Bounds;
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            var path = ConfigManager.Instance.SaveScreenshot(bmp);
            _statusLabel.Text = $"Screenshot saved!";
        }
        catch { }
    }

    // ── Proportional scaling ────────────────────────────────────

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_scaler == null)
        {
            if (ClientSize.Width > 300 && ClientSize.Height > 200)
                _scaler = new LayoutScaler(this);
        }
        else
        {
            _scaler.Apply(this);
        }
    }

    // ── Dispose ────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _fpsTimer?.Stop();
            _fpsTimer?.Dispose();
            _debugForm?.Close();
            _engine?.Stop();
        }
        base.Dispose(disposing);
    }
}
