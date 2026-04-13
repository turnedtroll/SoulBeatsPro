using System.Diagnostics;

namespace SoulBeatsPro;

/// <summary>
/// UserControl for customizing the GUI theme and background image.
/// Organized into clear, user-friendly sections.
/// </summary>
internal sealed class AppearanceTab : UserControl
{
    /// <summary>Fired whenever any theme property changes.</summary>
    public event Action? ThemeChanged;

    private readonly ThemeSettings _theme = ConfigManager.Instance.Theme;

    // ── Background controls ─────────────────────────────────────
    private readonly Button _btnBrowse;
    private readonly Button _btnClear;
    private readonly Label _lblFilePath;
    private readonly PictureBox _imgPreview;
    private readonly TrackBar _trkOverlay;
    private readonly Label _lblOverlay;

    // ── Font controls ───────────────────────────────────────────
    private readonly ComboBox _cboFont;
    private readonly NumericUpDown _nudFontSize;

    // ── Transparency controls ───────────────────────────────────
    private readonly TrackBar _trkFormOpacity;
    private readonly Label _lblFormOpacity;

    // ── Color swatch rows ───────────────────────────────────────
    private readonly List<(string label, Panel swatch, Label hexLabel, Func<string> getter, Action<string> setter)> _colorRows = [];

    // ── Bottom buttons ──────────────────────────────────────────
    private readonly Button _btnResetTheme;
    private readonly Button _btnOpenConfig;
    private LayoutScaler? _scaler;

    public AppearanceTab()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = new Font("MS Sans Serif", 8f);
        Dock = DockStyle.Fill;
        AutoScroll = true;

        int y = 8;

        // ════════════════════════════════════════════════════════
        // BACKGROUND IMAGE section
        // ════════════════════════════════════════════════════════
        var grpBg = new GroupBox
        {
            Text = "Background Image",
            Font = new Font("MS Sans Serif", 8f),
            Location = new Point(8, y),
            Size = new Size(420, 120),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        var lblHelp = new Label
        {
            Text = "Choose a photo to use as the background for the entire app.",
            Location = new Point(10, 18),
            Size = new Size(400, 14),
            ForeColor = Color.FromArgb(160, 160, 170),
            Font = new Font("MS Sans Serif", 7.5f)
        };

        _btnBrowse = MakeButton("Choose Image...", 10, 36, 100);
        _btnBrowse.Click += BtnBrowse_Click;

        _btnClear = MakeButton("Remove", 116, 36, 60);
        _btnClear.Click += BtnClear_Click;

        _lblFilePath = new Label
        {
            Text = string.IsNullOrEmpty(_theme.BackgroundImage) ? "(no image selected)" : Path.GetFileName(_theme.BackgroundImage),
            AutoEllipsis = true,
            Location = new Point(182, 39),
            Size = new Size(228, 16),
            ForeColor = ConfigManager.Instance.Theme.GetTextColor(),
            Font = new Font("MS Sans Serif", 8f)
        };

        // Image preview thumbnail
        _imgPreview = new PictureBox
        {
            Size = new Size(80, 50),
            Location = new Point(10, 64),
            BorderStyle = BorderStyle.Fixed3D,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        LoadPreviewImage();

        // Overlay / tint slider
        var lblOverlayLabel = new Label
        {
            Text = "Background Tint:",
            Location = new Point(96, 68),
            Size = new Size(92, 16),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("MS Sans Serif", 8f)
        };

        _trkOverlay = new TrackBar
        {
            Minimum = 0,
            Maximum = 255,
            Value = Math.Clamp(_theme.PanelAlpha, 0, 255),
            TickFrequency = 32,
            Location = new Point(192, 64),
            Size = new Size(160, 24),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        _lblOverlay = new Label
        {
            Text = _theme.PanelAlpha == 0 ? "None" : _theme.PanelAlpha == 255 ? "Full" : $"{(int)(_theme.PanelAlpha / 2.55)}%",
            Location = new Point(356, 68),
            Size = new Size(54, 16),
            Font = new Font("MS Sans Serif", 8f),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        var lblTintHelp = new Label
        {
            Text = "None = image fully visible. Full = image hidden by tab color.",
            Location = new Point(96, 92),
            Size = new Size(314, 14),
            ForeColor = Color.FromArgb(160, 160, 170),
            Font = new Font("MS Sans Serif", 7f)
        };

        _trkOverlay.ValueChanged += (_, _) =>
        {
            _theme.PanelAlpha = _trkOverlay.Value;
            _lblOverlay.Text = _trkOverlay.Value == 0 ? "None" : _trkOverlay.Value == 255 ? "Full" : $"{(int)(_trkOverlay.Value / 2.55)}%";
            SaveAndNotify();
        };

        grpBg.Controls.AddRange([lblHelp, _btnBrowse, _btnClear, _lblFilePath,
            _imgPreview, lblOverlayLabel, _trkOverlay, _lblOverlay, lblTintHelp]);
        Controls.Add(grpBg);
        y += 128;

        // ════════════════════════════════════════════════════════
        // WINDOW TRANSPARENCY section
        // ════════════════════════════════════════════════════════
        var grpWindow = new GroupBox
        {
            Text = "Window",
            Font = new Font("MS Sans Serif", 8f),
            Location = new Point(8, y),
            Size = new Size(420, 50),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        var lblFormOpacityLabel = new Label
        {
            Text = "Window Transparency:",
            Location = new Point(10, 22),
            Size = new Size(116, 16),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("MS Sans Serif", 8f)
        };

        _trkFormOpacity = new TrackBar
        {
            Minimum = 20,
            Maximum = 100,
            Value = Math.Clamp(_theme.FormOpacity, 20, 100),
            TickFrequency = 10,
            Location = new Point(130, 18),
            Size = new Size(240, 24),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        _lblFormOpacity = new Label
        {
            Text = $"{_trkFormOpacity.Value}%",
            Location = new Point(374, 22),
            Size = new Size(36, 16),
            Font = new Font("MS Sans Serif", 8f),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _trkFormOpacity.ValueChanged += (_, _) =>
        {
            _theme.FormOpacity = _trkFormOpacity.Value;
            _lblFormOpacity.Text = $"{_trkFormOpacity.Value}%";
            SaveAndNotify();
        };

        grpWindow.Controls.AddRange([lblFormOpacityLabel, _trkFormOpacity, _lblFormOpacity]);
        Controls.Add(grpWindow);
        y += 58;

        // ════════════════════════════════════════════════════════
        // FONT section
        // ════════════════════════════════════════════════════════
        var grpFont = new GroupBox
        {
            Text = "Font",
            Font = new Font("MS Sans Serif", 8f),
            Location = new Point(8, y),
            Size = new Size(420, 52),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        var lblName = new Label
        {
            Text = "Font:",
            Location = new Point(10, 22),
            Size = new Size(32, 16),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("MS Sans Serif", 8f)
        };

        _cboFont = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(46, 19),
            Size = new Size(180, 21),
            Font = new Font("MS Sans Serif", 8f)
        };
        _cboFont.Items.AddRange(new object[]
        {
            "MS Sans Serif", "Tahoma", "Segoe UI", "Consolas",
            "Courier New", "Arial", "Verdana", "Microsoft Sans Serif",
            "Lucida Console", "Comic Sans MS"
        });
        _cboFont.SelectedItem = _theme.FontName;
        if (_cboFont.SelectedIndex < 0) _cboFont.SelectedIndex = 0;
        _cboFont.SelectedIndexChanged += (_, _) =>
        {
            _theme.FontName = _cboFont.SelectedItem?.ToString() ?? "MS Sans Serif";
            SaveAndNotify();
        };

        var lblSize = new Label
        {
            Text = "Size:",
            Location = new Point(238, 22),
            Size = new Size(32, 16),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("MS Sans Serif", 8f)
        };

        _nudFontSize = new NumericUpDown
        {
            Minimum = 6,
            Maximum = 24,
            Value = Math.Clamp(_theme.FontSize, 6, 24),
            Location = new Point(274, 19),
            Size = new Size(50, 21),
            Font = new Font("MS Sans Serif", 8f)
        };
        _nudFontSize.ValueChanged += (_, _) =>
        {
            _theme.FontSize = (int)_nudFontSize.Value;
            SaveAndNotify();
        };

        grpFont.Controls.AddRange([lblName, _cboFont, lblSize, _nudFontSize]);
        Controls.Add(grpFont);
        y += 60;

        // ════════════════════════════════════════════════════════
        // COLORS section
        // ════════════════════════════════════════════════════════
        var colorDefs = new (string Label, Func<string> Getter, Action<string> Setter)[]
        {
            ("Main Background:",     () => _theme.WindowBg,        v => _theme.WindowBg = v),
            ("Tab Background:",      () => _theme.TabBg,           v => _theme.TabBg = v),
            ("Panel Background:",    () => _theme.PanelBg,         v => _theme.PanelBg = v),
            ("Button Color:",        () => _theme.ButtonFace,      v => _theme.ButtonFace = v),
            ("Button Text:",         () => _theme.ButtonText,      v => _theme.ButtonText = v),
            ("Text Color:",          () => _theme.TextColor,       v => _theme.TextColor = v),
            ("Title Bar:",           () => _theme.TitleBar,        v => _theme.TitleBar = v),
            ("Accent Color:",        () => _theme.AccentColor,     v => _theme.AccentColor = v),
            ("Button Border Light:", () => _theme.ButtonHighlight, v => _theme.ButtonHighlight = v),
            ("Button Border Dark:",  () => _theme.ButtonShadow,    v => _theme.ButtonShadow = v),
        };

        int colorGroupHeight = 20 + colorDefs.Length * 26 + 8;
        var grpColors = new GroupBox
        {
            Text = "Colors  (click a swatch to change)",
            Font = new Font("MS Sans Serif", 8f),
            Location = new Point(8, y),
            Size = new Size(420, colorGroupHeight),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        int rowY = 20;
        foreach (var (label, getter, setter) in colorDefs)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(10, rowY + 2),
                Size = new Size(120, 16),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("MS Sans Serif", 8f)
            };

            var swatch = new Panel
            {
                Size = new Size(20, 20),
                Location = new Point(136, rowY),
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = _theme.GetColor(getter())
            };

            var hexLbl = new Label
            {
                Text = getter().ToUpperInvariant(),
                Location = new Point(162, rowY + 2),
                Size = new Size(70, 16),
                Font = new Font("MS Sans Serif", 8f),
                ForeColor = ConfigManager.Instance.Theme.GetTextColor()
            };

            // Capture for closure
            var currentGetter = getter;
            var currentSetter = setter;
            var currentSwatch = swatch;
            var currentHexLbl = hexLbl;

            swatch.Click += (_, _) =>
            {
                using var dlg = new ColorDialog
                {
                    Color = _theme.GetColor(currentGetter()),
                    FullOpen = true,
                    AnyColor = true
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string hex = ColorTranslator.ToHtml(dlg.Color);
                    currentSetter(hex);
                    currentSwatch.BackColor = dlg.Color;
                    currentHexLbl.Text = hex.ToUpperInvariant();
                    SaveAndNotify();
                }
            };
            swatch.Cursor = Cursors.Hand;

            _colorRows.Add((label, swatch, hexLbl, getter, setter));

            grpColors.Controls.AddRange([lbl, swatch, hexLbl]);
            rowY += 26;
        }

        Controls.Add(grpColors);
        y += colorGroupHeight + 8;

        // ════════════════════════════════════════════════════════
        // Bottom buttons
        // ════════════════════════════════════════════════════════
        _btnResetTheme = MakeButton("Reset All", 8, y, 80);
        _btnResetTheme.Click += BtnResetTheme_Click;
        Controls.Add(_btnResetTheme);

        _btnOpenConfig = MakeButton("Open Config Folder", 96, y, 130);
        _btnOpenConfig.Click += BtnOpenConfig_Click;
        Controls.Add(_btnOpenConfig);
    }

    // ── Button factory (Win95 style) ────────────────────────────

    private static Button MakeButton(string text, int x, int y, int width)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Standard,
            Font = new Font("MS Sans Serif", 8f),
            Location = new Point(x, y),
            Size = new Size(width, 24),
            UseVisualStyleBackColor = false,
            BackColor = ConfigManager.Instance.Theme.GetButtonFace()
        };
    }

    // ── Background ──────────────────────────────────────────────

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select Background Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*",
            InitialDirectory = ConfigManager.BackgroundsDir
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _theme.BackgroundImage = dlg.FileName;
            _lblFilePath.Text = Path.GetFileName(dlg.FileName);
            LoadPreviewImage();
            SaveAndNotify();
        }
    }

    private void BtnClear_Click(object? sender, EventArgs e)
    {
        _theme.BackgroundImage = "";
        _lblFilePath.Text = "(no image selected)";
        _imgPreview.Image?.Dispose();
        _imgPreview.Image = null;
        SaveAndNotify();
    }

    private void LoadPreviewImage()
    {
        _imgPreview.Image?.Dispose();
        _imgPreview.Image = null;

        var path = _theme.BackgroundImage;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { _imgPreview.Image = Image.FromFile(path); }
            catch { /* ignore bad files */ }
        }
    }

    // ── Reset Theme ─────────────────────────────────────────────

    private void BtnResetTheme_Click(object? sender, EventArgs e)
    {
        _theme.Reset();
        RefreshAllSwatches();
        _cboFont.SelectedItem = _theme.FontName;
        _nudFontSize.Value = Math.Clamp(_theme.FontSize, 6, 24);
        _lblFilePath.Text = string.IsNullOrEmpty(_theme.BackgroundImage) ? "(no image selected)" : Path.GetFileName(_theme.BackgroundImage);
        LoadPreviewImage();
        _trkFormOpacity.Value = Math.Clamp(_theme.FormOpacity, 20, 100);
        _lblFormOpacity.Text = $"{_trkFormOpacity.Value}%";
        _trkOverlay.Value = Math.Clamp(_theme.PanelAlpha, 0, 255);
        _lblOverlay.Text = _trkOverlay.Value == 0 ? "None" : _trkOverlay.Value == 255 ? "Full" : $"{(int)(_trkOverlay.Value / 2.55)}%";
        SaveAndNotify();
    }

    // ── Open Config Folder ──────────────────────────────────────

    private void BtnOpenConfig_Click(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigManager.ConfigDir,
                UseShellExecute = true
            });
        }
        catch { /* ignore if folder doesn't exist */ }
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void RefreshAllSwatches()
    {
        foreach (var (_, swatch, hexLabel, getter, _) in _colorRows)
        {
            string hex = getter();
            swatch.BackColor = _theme.GetColor(hex);
            hexLabel.Text = hex.ToUpperInvariant();
        }
    }

    private void SaveAndNotify()
    {
        ConfigManager.Instance.SaveSettings();
        ThemeChanged?.Invoke();
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
