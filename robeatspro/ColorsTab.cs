namespace SoulBeatsPro;

/// <summary>
/// UserControl for configuring note and hold color detection thresholds.
/// Win95/98 retro style with GroupBox sections, NumericUpDown spinners,
/// color swatches, screen picker integration, and a ColorWheelControl.
/// </summary>
internal sealed class ColorsTab : UserControl
{
    // ── Style ───────────────────────────────────────────────────
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);
    private static readonly Color BgColor = ConfigManager.Instance.Theme.GetWindowBg();

    // ── Note color controls ─────────────────────────────────────
    private readonly NumericUpDown _noteMinR, _noteMinG, _noteMaxB;
    private readonly Panel _noteSwatch;

    // ── Hold color controls ─────────────────────────────────────
    private readonly NumericUpDown _holdMinR, _holdMaxR, _holdMinG, _holdMaxG, _holdMaxB, _holdMinRG;
    private readonly Panel _holdSwatch;

    // ── Color wheel ─────────────────────────────────────────────
    private readonly ColorWheelControl _colorWheel;

    // ── Track which section last requested the color wheel ──────
    private enum WheelTarget { None, Note, Hold }
    private WheelTarget _wheelTarget = WheelTarget.Note;
    private LayoutScaler? _scaler;
    private bool _applyingPick;

    public ColorsTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = RetroFont;
        AutoScroll = true;

        var det = ConfigManager.Instance.Detection;

        // ════════════════════════════════════════════════════════
        //  Note Color GroupBox
        // ════════════════════════════════════════════════════════
        var noteGroup = new GroupBox
        {
            Text = "Note Color (tap zone)",
            Font = RetroFont,
            Location = new Point(12, 8),
            Size = new Size(360, 110),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        // Swatch
        _noteSwatch = MakeSwatch(14, 24);
        noteGroup.Controls.Add(_noteSwatch);

        // Spinners — row 1
        _noteMinR = MakeSpinner(noteGroup, "Min R:", 80, 20, det.NoteColor.MinR);
        _noteMinG = MakeSpinner(noteGroup, "Min G:", 200, 20, det.NoteColor.MinG);

        // Spinners — row 2
        _noteMaxB = MakeSpinner(noteGroup, "Max B:", 80, 48, det.NoteColor.MaxB);

        // Pick from screen button
        var notePickBtn = new Button
        {
            Text = "Pick from screen",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(120, 23),
            Location = new Point(14, 78)
        };
        notePickBtn.Click += NotePickBtn_Click;
        noteGroup.Controls.Add(notePickBtn);

        // "Use wheel" button to assign wheel picks to this section
        var noteWheelBtn = new Button
        {
            Text = "Use Color Wheel",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(120, 23),
            Location = new Point(142, 78)
        };
        noteWheelBtn.Click += (_, _) => _wheelTarget = WheelTarget.Note;
        noteGroup.Controls.Add(noteWheelBtn);

        // Wire change events
        _noteMinR.ValueChanged += NoteSpinner_Changed;
        _noteMinG.ValueChanged += NoteSpinner_Changed;
        _noteMaxB.ValueChanged += NoteSpinner_Changed;

        Controls.Add(noteGroup);

        // ════════════════════════════════════════════════════════
        //  Hold Color GroupBox
        // ════════════════════════════════════════════════════════
        var holdGroup = new GroupBox
        {
            Text = "Hold Color (hold zone)",
            Font = RetroFont,
            Location = new Point(12, 126),
            Size = new Size(360, 156),
            FlatStyle = FlatStyle.Standard,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        // Swatch
        _holdSwatch = MakeSwatch(14, 24);
        holdGroup.Controls.Add(_holdSwatch);

        // Spinners — row 1
        _holdMinR = MakeSpinner(holdGroup, "Min R:", 80, 20, det.HoldColor.MinR);
        _holdMaxR = MakeSpinner(holdGroup, "Max R:", 200, 20, det.HoldColor.MaxR);

        // Spinners — row 2
        _holdMinG = MakeSpinner(holdGroup, "Min G:", 80, 48, det.HoldColor.MinG);
        _holdMaxG = MakeSpinner(holdGroup, "Max G:", 200, 48, det.HoldColor.MaxG);

        // Spinners — row 3
        _holdMaxB = MakeSpinner(holdGroup, "Max B:", 80, 76, det.HoldColor.MaxB);
        _holdMinRG = MakeSpinner(holdGroup, "Min R+G:", 200, 76, det.HoldColor.MinRG, max: 510);

        // Pick from screen button
        var holdPickBtn = new Button
        {
            Text = "Pick from screen",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(120, 23),
            Location = new Point(14, 106)
        };
        holdPickBtn.Click += HoldPickBtn_Click;
        holdGroup.Controls.Add(holdPickBtn);

        // "Use wheel" button to assign wheel picks to this section
        var holdWheelBtn = new Button
        {
            Text = "Use Color Wheel",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(120, 23),
            Location = new Point(142, 106)
        };
        holdWheelBtn.Click += (_, _) => _wheelTarget = WheelTarget.Hold;
        holdGroup.Controls.Add(holdWheelBtn);

        // Wire change events
        _holdMinR.ValueChanged += HoldSpinner_Changed;
        _holdMaxR.ValueChanged += HoldSpinner_Changed;
        _holdMinG.ValueChanged += HoldSpinner_Changed;
        _holdMaxG.ValueChanged += HoldSpinner_Changed;
        _holdMaxB.ValueChanged += HoldSpinner_Changed;
        _holdMinRG.ValueChanged += HoldSpinner_Changed;

        Controls.Add(holdGroup);

        // ════════════════════════════════════════════════════════
        //  Color Wheel
        // ════════════════════════════════════════════════════════
        _colorWheel = new ColorWheelControl
        {
            Location = new Point(12, 290),
            Size = new Size(200, 200)
        };
        _colorWheel.ColorChanged += ColorWheel_ColorChanged;
        Controls.Add(_colorWheel);

        var wheelLabel = new Label
        {
            Text = "Color Wheel",
            Font = RetroFont,
            AutoSize = true,
            Location = new Point(220, 290)
        };
        Controls.Add(wheelLabel);

        var wheelHint = new Label
        {
            Text = "Click 'Use Color Wheel'\non a section above,\nthen pick a color here.",
            Font = RetroFont,
            ForeColor = Color.FromArgb(160, 160, 170),
            AutoSize = true,
            Location = new Point(220, 310)
        };
        Controls.Add(wheelHint);

        // ════════════════════════════════════════════════════════
        //  Reset to Defaults
        // ════════════════════════════════════════════════════════
        var resetBtn = new Button
        {
            Text = "Reset to Defaults",
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(130, 26),
            Location = new Point(12, 500)
        };
        resetBtn.Click += ResetButton_Click;
        Controls.Add(resetBtn);

        // Initial swatch colours
        UpdateNoteSwatch();
        UpdateHoldSwatch();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static Panel MakeSwatch(int x, int y)
    {
        return new Panel
        {
            Size = new Size(48, 40),
            Location = new Point(x, y),
            BorderStyle = BorderStyle.Fixed3D
        };
    }

    /// <summary>
    /// Creates a Label + NumericUpDown pair inside a parent GroupBox.
    /// Returns the NumericUpDown.
    /// </summary>
    private static NumericUpDown MakeSpinner(GroupBox parent, string labelText,
        int x, int y, int value, int min = 0, int max = 255)
    {
        var lbl = new Label
        {
            Text = labelText,
            Font = RetroFont,
            AutoSize = true,
            Location = new Point(x, y + 3)
        };
        parent.Controls.Add(lbl);

        var nud = new NumericUpDown
        {
            Font = RetroFont,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Size = new Size(56, 20),
            Location = new Point(x + lbl.PreferredWidth + 4, y),
            BorderStyle = BorderStyle.Fixed3D
        };
        parent.Controls.Add(nud);
        return nud;
    }

    // ── Swatch update ───────────────────────────────────────────

    private void UpdateNoteSwatch()
    {
        var nc = ConfigManager.Instance.Detection.NoteColor;
        if (nc.PickedR >= 0 && nc.PickedG >= 0 && nc.PickedB >= 0)
            _noteSwatch.BackColor = Color.FromArgb(nc.PickedR, nc.PickedG, nc.PickedB);
        else
            _noteSwatch.BackColor = Color.FromArgb(
                Math.Clamp((nc.MinR + 255) / 2, 0, 255),
                Math.Clamp((nc.MinG + 255) / 2, 0, 255),
                Math.Clamp(nc.MaxB / 2, 0, 255));
    }

    private void UpdateHoldSwatch()
    {
        var hc = ConfigManager.Instance.Detection.HoldColor;
        if (hc.PickedR >= 0 && hc.PickedG >= 0 && hc.PickedB >= 0)
            _holdSwatch.BackColor = Color.FromArgb(hc.PickedR, hc.PickedG, hc.PickedB);
        else
            _holdSwatch.BackColor = Color.FromArgb(
                Math.Clamp((hc.MinR + hc.MaxR) / 2, 0, 255),
                Math.Clamp((hc.MinG + hc.MaxG) / 2, 0, 255),
                Math.Clamp(hc.MaxB / 2, 0, 255));
    }

    // ── Spinner change handlers ─────────────────────────────────

    private void NoteSpinner_Changed(object? sender, EventArgs e)
    {
        if (_applyingPick) return;
        var nc = ConfigManager.Instance.Detection.NoteColor;
        nc.MinR = (int)_noteMinR.Value;
        nc.MinG = (int)_noteMinG.Value;
        nc.MaxB = (int)_noteMaxB.Value;
        nc.PickedR = nc.PickedG = nc.PickedB = -1;
        UpdateNoteSwatch();
        ConfigManager.Instance.SaveSettings();
    }

    private void HoldSpinner_Changed(object? sender, EventArgs e)
    {
        if (_applyingPick) return;
        var hc = ConfigManager.Instance.Detection.HoldColor;
        hc.MinR = (int)_holdMinR.Value;
        hc.MaxR = (int)_holdMaxR.Value;
        hc.MinG = (int)_holdMinG.Value;
        hc.MaxG = (int)_holdMaxG.Value;
        hc.MaxB = (int)_holdMaxB.Value;
        hc.MinRG = (int)_holdMinRG.Value;
        hc.PickedR = hc.PickedG = hc.PickedB = -1;
        UpdateHoldSwatch();
        ConfigManager.Instance.SaveSettings();
    }

    // ── Screen picker — Note ────────────────────────────────────

    private void NotePickBtn_Click(object? sender, EventArgs e)
    {
        using var picker = new ScreenPicker();
        if (picker.ShowDialog() == DialogResult.OK && picker.PickedColor != null)
        {
            ApplyNoteColor(picker.PickedColor.Value);
        }
    }

    private void ApplyNoteColor(Color c)
    {
        _applyingPick = true;
        try
        {
            var nc = ConfigManager.Instance.Detection.NoteColor;
            nc.MinR = Math.Max(0, c.R - 30);
            nc.MinG = Math.Max(0, c.G - 30);
            nc.MaxB = Math.Min(255, c.B + 30);

            // Set spinners first — let any ValueChanged events fire
            _noteMinR.Value = nc.MinR;
            _noteMinG.Value = nc.MinG;
            _noteMaxB.Value = nc.MaxB;

            // Set picked color and swatch LAST so nothing can overwrite them
            nc.PickedR = c.R;
            nc.PickedG = c.G;
            nc.PickedB = c.B;
            _noteSwatch.BackColor = c;
            ConfigManager.Instance.SaveSettings();
        }
        finally { _applyingPick = false; }
    }

    // ── Screen picker — Hold ────────────────────────────────────

    private void HoldPickBtn_Click(object? sender, EventArgs e)
    {
        using var picker = new ScreenPicker();
        if (picker.ShowDialog() == DialogResult.OK && picker.PickedColor != null)
        {
            ApplyHoldColor(picker.PickedColor.Value);
        }
    }

    private void ApplyHoldColor(Color c)
    {
        _applyingPick = true;
        try
        {
            var hc = ConfigManager.Instance.Detection.HoldColor;
            hc.MinR = Math.Max(0, c.R - 40);
            hc.MaxR = Math.Min(255, c.R + 40);
            hc.MinG = Math.Max(0, c.G - 40);
            hc.MaxG = Math.Min(255, c.G + 40);
            hc.MaxB = Math.Min(255, c.B + 30);
            hc.MinRG = Math.Max(0, c.R + c.G - 30);

            // Set spinners first — let any ValueChanged events fire
            _holdMinR.Value = hc.MinR;
            _holdMaxR.Value = hc.MaxR;
            _holdMinG.Value = hc.MinG;
            _holdMaxG.Value = hc.MaxG;
            _holdMaxB.Value = hc.MaxB;
            _holdMinRG.Value = Math.Clamp(hc.MinRG, 0, 510);

            // Set picked color and swatch LAST so nothing can overwrite them
            hc.PickedR = c.R;
            hc.PickedG = c.G;
            hc.PickedB = c.B;
            _holdSwatch.BackColor = c;
            ConfigManager.Instance.SaveSettings();
        }
        finally { _applyingPick = false; }
    }

    // ── Color wheel handler ─────────────────────────────────────

    private void ColorWheel_ColorChanged(object? sender, Color c)
    {
        switch (_wheelTarget)
        {
            case WheelTarget.Note:
                ApplyNoteColor(c);
                break;
            case WheelTarget.Hold:
                ApplyHoldColor(c);
                break;
        }
    }

    // ── Reset to defaults ───────────────────────────────────────

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        _applyingPick = true;
        try
        {
            var det = ConfigManager.Instance.Detection;

            // Note color defaults
            det.NoteColor.MinR = 200;
            det.NoteColor.MinG = 180;
            det.NoteColor.MaxB = 80;
            det.NoteColor.PickedR = det.NoteColor.PickedG = det.NoteColor.PickedB = -1;

            _noteMinR.Value = 200;
            _noteMinG.Value = 180;
            _noteMaxB.Value = 80;

            // Hold color defaults
            det.HoldColor.MinR = 120;
            det.HoldColor.MaxR = 200;
            det.HoldColor.MinG = 100;
            det.HoldColor.MaxG = 180;
            det.HoldColor.MaxB = 80;
            det.HoldColor.MinRG = 230;
            det.HoldColor.PickedR = det.HoldColor.PickedG = det.HoldColor.PickedB = -1;

            _holdMinR.Value = 120;
            _holdMaxR.Value = 200;
            _holdMinG.Value = 100;
            _holdMaxG.Value = 180;
            _holdMaxB.Value = 80;
            _holdMinRG.Value = 230;

            UpdateNoteSwatch();
            UpdateHoldSwatch();
            ConfigManager.Instance.SaveSettings();
        }
        finally { _applyingPick = false; }
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
}
