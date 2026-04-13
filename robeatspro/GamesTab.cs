namespace SoulBeatsPro;

/// Tab for switching between game modes (Funky Friday, RoBeats).
/// Each game mode has its own detection style, default keys, and default coords.
internal sealed class GamesTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);

    private readonly RadioButton _rbFunkyFriday;
    private readonly RadioButton _rbRoBeats;
    private readonly Label _descLabel;
    private readonly Button _applyDefaultsBtn;
    private readonly ComboBox _accuracyCombo;
    private readonly Label _accuracyLabel;
    private LayoutScaler? _scaler;

    public event Action? GameModeChanged;

    public GamesTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var activeGame = ConfigManager.Instance.GameMode.ActiveGame;

        // ── Game Selection GroupBox ─────────────────────────────
        var grpGame = new GroupBox
        {
            Text = "Game Mode",
            Font = RetroFont,
            Location = new Point(12, 8),
            Size = new Size(360, 100),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        _rbFunkyFriday = new RadioButton
        {
            Text = "Funky Friday",
            Font = new Font("MS Sans Serif", 9f, FontStyle.Bold),
            Location = new Point(14, 24),
            AutoSize = true,
            Checked = activeGame == "funkyFriday"
        };
        _rbFunkyFriday.CheckedChanged += RbGame_CheckedChanged;
        grpGame.Controls.Add(_rbFunkyFriday);

        _rbRoBeats = new RadioButton
        {
            Text = "RoBeats",
            Font = new Font("MS Sans Serif", 9f, FontStyle.Bold),
            Location = new Point(14, 52),
            AutoSize = true,
            Checked = activeGame == "robeats"
        };
        _rbRoBeats.CheckedChanged += RbGame_CheckedChanged;
        grpGame.Controls.Add(_rbRoBeats);

        var ffLabel = new Label
        {
            Text = "White/Gray detection  |  Keys: Z X , .",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Location = new Point(140, 28)
        };
        grpGame.Controls.Add(ffLabel);

        var rbLabel = new Label
        {
            Text = "Color-based detection  |  Keys: Z X , .",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Location = new Point(140, 56)
        };
        grpGame.Controls.Add(rbLabel);

        Controls.Add(grpGame);

        // ── Description ────────────────────────────────────────
        _descLabel = new Label
        {
            Font = RetroFont,
            Location = new Point(12, 118),
            Size = new Size(360, 80),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };
        UpdateDescription();
        Controls.Add(_descLabel);

        // ── Apply Defaults Button ──────────────────────────────
        _applyDefaultsBtn = new Button
        {
            Text = "Apply Game Defaults",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(160, 28),
            Location = new Point(12, 206)
        };
        _applyDefaultsBtn.Click += ApplyDefaults_Click;
        Controls.Add(_applyDefaultsBtn);

        var warnLabel = new Label
        {
            Text = "This will reset your keybinds, detection colors,\nand calibration coords to the selected game's defaults.",
            Font = RetroFont,
            ForeColor = Color.FromArgb(255, 120, 120),
            AutoSize = true,
            Location = new Point(12, 240)
        };
        Controls.Add(warnLabel);

        // ── Info GroupBox ───────────────────────────────────────
        var grpInfo = new GroupBox
        {
            Text = "Detection Info",
            Font = RetroFont,
            Location = new Point(12, 284),
            Size = new Size(360, 120),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        var infoLabel = new Label
        {
            Text =
                "Funky Friday (White/Gray mode):\n" +
                "  Notes = bright white pixels (all channels >= 240)\n" +
                "  Holds = gray pixels (all channels 130-170)\n" +
                "  Same detection as larpLOLv4\n\n" +
                "RoBeats (Color-based mode):\n" +
                "  Notes = configurable color thresholds\n" +
                "  Holds = configurable color range",
            Font = RetroFont,
            Location = new Point(10, 18),
            Size = new Size(340, 95)
        };
        grpInfo.Controls.Add(infoLabel);
        Controls.Add(grpInfo);

        // ── Accuracy preset dropdown ────────────────────────────
        _accuracyLabel = new Label
        {
            Text = "Accuracy:",
            Font = RetroFont,
            AutoSize = true,
            Location = new Point(12, 414)
        };
        Controls.Add(_accuracyLabel);

        _accuracyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = RetroFont,
            Location = new Point(80, 410),
            Size = new Size(160, 22)
        };
        Controls.Add(_accuracyCombo);

        RefreshAccuracyCombo();
        _accuracyCombo.SelectedIndexChanged += AccuracyCombo_Changed;
    }

    private void RefreshAccuracyCombo()
    {
        bool ff = ConfigManager.Instance.IsWhiteGrayMode;
        var labels = AccuracyPresetTable.GetLabels(ff);

        _accuracyCombo.SelectedIndexChanged -= AccuracyCombo_Changed;
        _accuracyCombo.Items.Clear();
        _accuracyCombo.Items.AddRange(labels);
        _accuracyCombo.SelectedIndex = (int)ConfigManager.Instance.ActiveProfile.AccuracyPreset;
        _accuracyCombo.SelectedIndexChanged += AccuracyCombo_Changed;
    }

    private void AccuracyCombo_Changed(object? sender, EventArgs e)
    {
        int idx = _accuracyCombo.SelectedIndex;
        if (idx < 0 || idx > 3) return;
        ConfigManager.Instance.ActiveProfile.AccuracyPreset = (AccuracyPreset)idx;
        ConfigManager.Instance.SaveSettings();
    }

    private void RbGame_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton rb || !rb.Checked) return;

        string game = rb == _rbFunkyFriday ? "funkyFriday" : "robeats";
        ConfigManager.Instance.GameMode.ActiveGame = game;
        ConfigManager.Instance.SaveSettings();
        UpdateDescription();
        RefreshAccuracyCombo();
        GameModeChanged?.Invoke();
    }

    private void UpdateDescription()
    {
        bool ff = ConfigManager.Instance.IsWhiteGrayMode;
        _descLabel.Text = ff
            ? "Funky Friday mode active.\n" +
              "Detection: White/Gray (larpLOLv4 style)\n" +
              "Notes scroll upward. 4 lanes: Left, Down, Up, Right."
            : "RoBeats mode active.\n" +
              "Detection: Color-based (configurable in Colors tab)\n" +
              "Notes scroll downward. 4 lanes.";
    }

    private void ApplyDefaults_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Reset keybinds, detection settings, and calibration coords\nto the selected game's defaults?",
            "Apply Game Defaults",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        bool ff = ConfigManager.Instance.IsWhiteGrayMode;

        // Reset keybinds
        var kb = ConfigManager.Instance.Keybinds;
        // Both modes use same keys as larpLOLv4
        kb.Lane1 = "Z"; kb.Lane2 = "X"; kb.Lane3 = "OEM_COMMA"; kb.Lane4 = "OEM_PERIOD";
        NativeApi.UpdateLaneScans(kb.LaneKeys);

        // Reset detection
        var det = ConfigManager.Instance.Detection;
        if (ff)
        {
            det.WhiteGray.WhiteMin = 240;
            det.WhiteGray.GrayMin = 130;
            det.WhiteGray.GrayMax = 170;
        }
        else
        {
            det.NoteColor.MinR = 200; det.NoteColor.MinG = 180; det.NoteColor.MaxB = 80;
            det.NoteColor.PickedR = det.NoteColor.PickedG = det.NoteColor.PickedB = -1;
            det.HoldColor.MinR = 120; det.HoldColor.MaxR = 200;
            det.HoldColor.MinG = 100; det.HoldColor.MaxG = 180;
            det.HoldColor.MaxB = 80; det.HoldColor.MinRG = 230;
            det.HoldColor.PickedR = det.HoldColor.PickedG = det.HoldColor.PickedB = -1;
        }

        // Reset coords (same defaults for now — calibrate for your screen)
        Point[] defaultTap, defaultHold;
        // Same default coords as larpLOLv4
        defaultTap = [new(720, 950), new(881, 950), new(1039, 950), new(1200, 950)];
        defaultHold = [new(720, 824), new(878, 824), new(1040, 824), new(1197, 824)];
        ConfigManager.Instance.SaveCoords(defaultTap, defaultHold);

        // Reset tuning
        ConfigManager.Instance.Tuning.Reset();

        ConfigManager.Instance.SaveSettings();
        GameModeChanged?.Invoke();

        MessageBox.Show("Defaults applied! Use the Calibration tab to\nadjust detection points for your screen.",
            "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

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
}
