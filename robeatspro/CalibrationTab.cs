using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// Calibration tab — live screen preview with sidebar buttons to select and move T/H points.
internal sealed class CalibrationTab : UserControl
{
    private new const float Scale = 0.5f;
    private const int HandleSize = 13;
    private const int SampleHalf = 3;
    private const int MoveStep = 1;
    private const int MoveStepFast = 10;

    private Point[] _tapPts;
    private Point[] _holdPts;

    private readonly HashSet<(string kind, int lane)> _selected = new();

    private readonly PictureBox _pic;
    private readonly Panel _sidebar;
    private readonly Panel _buttonBar;
    private readonly Label _selLabel;
    private readonly PictureBox _magnifierPic;
    private readonly Label _magnifierLabel;
    private const int MagnifierZoom = 6;
    private readonly Button _btnStartPreview;
    private readonly Button _btnSave;
    private readonly Button _btnRevert;
    private readonly Button _btnRescan;
    private bool _rescanRequested;

    // Signature-capture walk state
    private readonly Button _btnCaptureSigs;
    private readonly Button _btnCaptureSkip;
    private readonly Button _btnCaptureCancel;
    private readonly Label _captureStatusLabel;
    private bool _captureRunning;
    private bool _captureSkipHoldRequested;
    private bool _captureCancelRequested;
    private readonly Button[] _tapBtns = new Button[4];
    private readonly Button[] _holdBtns = new Button[4];

    private System.Windows.Forms.Timer? _timer;
    private ScreenCapture? _capture;
    private Rectangle _monitorBounds;
    private bool _previewing;

    // Drag state
    private (string kind, int lane)? _dragging;
    private bool _isDragging;
    private Point _dragStartScreen;
    private readonly Dictionary<(string, int), Point> _dragStartPts = new();
    private string _lastSnapMessage = "";
    private double _lastSnapMessageAt;

    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(0xFF, 0xDC, 0x28),
        Color.FromArgb(0x28, 0xDC, 0xFF),
        Color.FromArgb(0xFF, 0x64, 0x64),
        Color.FromArgb(0x64, 0xFF, 0x64)
    ];

    private static readonly string[] LaneNames = ["Z", "X", ",", "."];

    public CalibrationTab()
    {
        (_tapPts, _holdPts) = ConfigManager.Instance.LoadCoords();

        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = new Font("MS Sans Serif", 8f);
        Dock = DockStyle.Fill;

        var sideFont = new Font("MS Sans Serif", 7.5f);
        var sideBold = new Font("MS Sans Serif", 8f, FontStyle.Bold);

        // ── Bottom button bar ────────────────────────────────────
        _buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            BackColor = ConfigManager.Instance.Theme.GetButtonFace(),
            Padding = new Padding(6)
        };

        _btnStartPreview = MakeButton("Start Preview");
        _btnStartPreview.Location = new Point(6, 6);
        _btnStartPreview.Click += BtnStartPreview_Click;

        _btnSave = MakeButton("Save");
        _btnSave.Size = new Size(60, 24);
        _btnSave.Location = new Point(112, 6);
        _btnSave.Click += (_, _) => ConfigManager.Instance.SaveCoords(_tapPts, _holdPts);

        _btnRevert = MakeButton("Revert");
        _btnRevert.Size = new Size(60, 24);
        _btnRevert.Location = new Point(178, 6);
        _btnRevert.Click += (_, _) => { (_tapPts, _holdPts) = ConfigManager.Instance.LoadCoords(); RefreshSelectionUI(); };

        _btnRescan = MakeButton("Rescan");
        _btnRescan.Size = new Size(70, 24);
        _btnRescan.Location = new Point(244, 6);
        _btnRescan.Enabled = false;
        _btnRescan.Click += (_, _) => { _rescanRequested = true; };

        _buttonBar.Controls.AddRange([_btnStartPreview, _btnSave, _btnRevert, _btnRescan]);

        // ── Right sidebar ────────────────────────────────────────
        _sidebar = new Panel
        {
            Dock = DockStyle.Right,
            Width = 150,
            BackColor = ConfigManager.Instance.Theme.GetButtonFace(),
            AutoScroll = true,
            BorderStyle = BorderStyle.Fixed3D
        };

        int pad = 6;
        int btnW = 150 - pad * 2 - 4; // account for border
        int sy = pad;

        // Title
        var title = new Label { Text = "Select Points", Font = sideBold, AutoSize = true, Location = new Point(pad, sy) };
        _sidebar.Controls.Add(title);
        sy += 18;

        // Tap row
        var tapLbl = new Label { Text = "Tap:", Font = sideFont, AutoSize = true, Location = new Point(pad, sy + 3) };
        _sidebar.Controls.Add(tapLbl);
        int bx = 32;
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            _tapBtns[i] = new Button
            {
                Text = $"T{LaneNames[i]}",
                Font = sideFont,
                FlatStyle = FlatStyle.Standard,
                Size = new Size(28, 22),
                Location = new Point(bx + i * 29, sy),
                BackColor = ConfigManager.Instance.Theme.GetButtonFace()
            };
            _tapBtns[i].Click += (_, _) => ToggleSelection("tap", idx);
            _sidebar.Controls.Add(_tapBtns[i]);
        }
        sy += 26;

        // Hold row
        var holdLbl = new Label { Text = "Hold:", Font = sideFont, AutoSize = true, Location = new Point(pad, sy + 3) };
        _sidebar.Controls.Add(holdLbl);
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            _holdBtns[i] = new Button
            {
                Text = $"H{LaneNames[i]}",
                Font = sideFont,
                FlatStyle = FlatStyle.Standard,
                Size = new Size(28, 22),
                Location = new Point(bx + i * 29, sy),
                BackColor = ConfigManager.Instance.Theme.GetButtonFace()
            };
            _holdBtns[i].Click += (_, _) => ToggleSelection("hold", idx);
            _sidebar.Controls.Add(_holdBtns[i]);
        }
        sy += 28;

        // Quick select
        var selAllTap = new Button { Text = "All Tap", Font = sideFont, Size = new Size(btnW / 2, 22), Location = new Point(pad, sy) };
        selAllTap.Click += (_, _) => { _selected.Clear(); for (int i = 0; i < 4; i++) _selected.Add(("tap", i)); RefreshSelectionUI(); };
        _sidebar.Controls.Add(selAllTap);

        var selAllHold = new Button { Text = "All Hold", Font = sideFont, Size = new Size(btnW / 2, 22), Location = new Point(pad + btnW / 2 + 2, sy) };
        selAllHold.Click += (_, _) => { _selected.Clear(); for (int i = 0; i < 4; i++) _selected.Add(("hold", i)); RefreshSelectionUI(); };
        _sidebar.Controls.Add(selAllHold);
        sy += 24;

        var selAll = new Button { Text = "All", Font = sideFont, Size = new Size(btnW / 2, 22), Location = new Point(pad, sy) };
        selAll.Click += (_, _) => { for (int i = 0; i < 4; i++) { _selected.Add(("tap", i)); _selected.Add(("hold", i)); } RefreshSelectionUI(); };
        _sidebar.Controls.Add(selAll);

        var selClear = new Button { Text = "Clear", Font = sideFont, Size = new Size(btnW / 2, 22), Location = new Point(pad + btnW / 2 + 2, sy) };
        selClear.Click += (_, _) => { _selected.Clear(); RefreshSelectionUI(); };
        _sidebar.Controls.Add(selClear);
        sy += 30;

        // Arrow buttons
        var moveTitle = new Label { Text = "Move Selected", Font = sideBold, AutoSize = true, Location = new Point(pad, sy) };
        _sidebar.Controls.Add(moveTitle);
        sy += 18;

        var arrowSize = new Size(32, 26);
        int aLeft = pad;
        int aMid = aLeft + 34;
        int aRight = aMid + 34;

        var upBtn = new Button { Text = "^", Font = sideBold, Size = arrowSize, Location = new Point(aMid, sy) };
        upBtn.Click += (_, _) => MoveSelected(0, -GetStep());
        _sidebar.Controls.Add(upBtn);
        sy += 28;

        var leftBtn = new Button { Text = "<", Font = sideBold, Size = arrowSize, Location = new Point(aLeft, sy) };
        leftBtn.Click += (_, _) => MoveSelected(-GetStep(), 0);
        _sidebar.Controls.Add(leftBtn);

        var rightBtn = new Button { Text = ">", Font = sideBold, Size = arrowSize, Location = new Point(aRight, sy) };
        rightBtn.Click += (_, _) => MoveSelected(GetStep(), 0);
        _sidebar.Controls.Add(rightBtn);
        sy += 28;

        var downBtn = new Button { Text = "v", Font = sideBold, Size = arrowSize, Location = new Point(aMid, sy) };
        downBtn.Click += (_, _) => MoveSelected(0, GetStep());
        _sidebar.Controls.Add(downBtn);
        sy += 32;

        var hint = new Label { Text = "Shift = 10px\nArrows work too", Font = sideFont, ForeColor = Color.Gray, Size = new Size(btnW, 28), Location = new Point(pad, sy) };
        _sidebar.Controls.Add(hint);
        sy += 32;

        // Selection info
        _selLabel = new Label { Text = "", Font = sideFont, ForeColor = ConfigManager.Instance.Theme.GetTextColor(), Size = new Size(btnW, 60), Location = new Point(pad, sy) };
        _sidebar.Controls.Add(_selLabel);

        sy += 64;

        var magTitle = new Label
        {
            Text = "Pixel Inspector",
            Font = sideBold,
            AutoSize = true,
            Location = new Point(pad, sy)
        };
        _sidebar.Controls.Add(magTitle);
        sy += 18;

        int magDisplay = btnW;
        _magnifierPic = new PictureBox
        {
            Size = new Size(magDisplay, magDisplay),
            Location = new Point(pad, sy),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(20, 20, 28),
            SizeMode = PictureBoxSizeMode.StretchImage
        };
        _sidebar.Controls.Add(_magnifierPic);
        sy += magDisplay + 6;

        _magnifierLabel = new Label
        {
            Text = "(no point selected)",
            Font = sideFont,
            ForeColor = ConfigManager.Instance.Theme.GetTextColor(),
            Size = new Size(btnW, 32),
            Location = new Point(pad, sy)
        };
        _sidebar.Controls.Add(_magnifierLabel);

        sy += 36;

        var snapBtn = new Button
        {
            Text = "Snap to Body",
            Font = sideFont,
            Size = new Size(btnW, 26),
            Location = new Point(pad, sy),
            BackColor = ConfigManager.Instance.Theme.GetButtonFace()
        };
        snapBtn.Click += (_, _) => SnapSelectedToBody();
        _sidebar.Controls.Add(snapBtn);

        sy += 30;

        _btnCaptureSigs = new Button
        {
            Text = "Capture Signatures",
            Font = sideFont,
            Size = new Size(btnW, 26),
            Location = new Point(pad, sy),
            BackColor = ConfigManager.Instance.Theme.GetButtonFace()
        };
        _btnCaptureSigs.Click += BtnCaptureSigs_Click;
        _sidebar.Controls.Add(_btnCaptureSigs);

        sy += 30;

        _captureStatusLabel = new Label
        {
            Text = "",
            Font = sideFont,
            ForeColor = ConfigManager.Instance.Theme.GetTextColor(),
            Size = new Size(btnW, 32),
            Location = new Point(pad, sy),
            Visible = false
        };
        _sidebar.Controls.Add(_captureStatusLabel);
        sy += 36;

        _btnCaptureSkip = new Button
        {
            Text = "Skip Hold",
            Font = sideFont,
            Size = new Size(btnW / 2, 22),
            Location = new Point(pad, sy),
            BackColor = ConfigManager.Instance.Theme.GetButtonFace(),
            Visible = false
        };
        _btnCaptureSkip.Click += (_, _) => _captureSkipHoldRequested = true;
        _sidebar.Controls.Add(_btnCaptureSkip);

        _btnCaptureCancel = new Button
        {
            Text = "Cancel",
            Font = sideFont,
            Size = new Size(btnW / 2 - 4, 22),
            Location = new Point(pad + btnW / 2 + 2, sy),
            BackColor = ConfigManager.Instance.Theme.GetButtonFace(),
            Visible = false
        };
        _btnCaptureCancel.Click += (_, _) => _captureCancelRequested = true;
        _sidebar.Controls.Add(_btnCaptureCancel);

        // ── PictureBox ───────────────────────────────────────────
        _pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.Fixed3D
        };

        // Mouse drag support on the preview
        _pic.MouseDown += Pic_MouseDown;
        _pic.MouseMove += Pic_MouseMove;
        _pic.MouseUp += Pic_MouseUp;
        _pic.Cursor = Cursors.Cross;

        // Docking order: Fill FIRST, then non-Fill controls
        Controls.Add(_pic);
        Controls.Add(_sidebar);
        Controls.Add(_buttonBar);

        // Auto-select first tap box
        _selected.Add(("tap", 0));
        RefreshSelectionUI();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Standard,
        Font = new Font("MS Sans Serif", 8f),
        Size = new Size(100, 24),
        BackColor = ConfigManager.Instance.Theme.GetButtonFace()
    };

    private void ToggleSelection(string kind, int lane)
    {
        bool multi = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
        var item = (kind, lane);

        if (multi)
        {
            if (!_selected.Remove(item))
                _selected.Add(item);
        }
        else
        {
            _selected.Clear();
            _selected.Add(item);
        }
        RefreshSelectionUI();
    }

    private void RefreshSelectionUI()
    {
        for (int i = 0; i < 4; i++)
        {
            _tapBtns[i].BackColor = _selected.Contains(("tap", i)) ? LaneColors[i] : ConfigManager.Instance.Theme.GetButtonFace();
            _holdBtns[i].BackColor = _selected.Contains(("hold", i)) ? LaneColors[i] : ConfigManager.Instance.Theme.GetButtonFace();
        }

        if (_selected.Count == 0) { _selLabel.Text = "No selection"; return; }
        var parts = new List<string>();
        foreach (var (kind, lane) in _selected.OrderBy(s => s.kind).ThenBy(s => s.lane))
        {
            var prefix = kind == "tap" ? "T" : "H";
            var pts = kind == "tap" ? _tapPts : _holdPts;
            parts.Add($"{prefix}{LaneNames[lane]} ({pts[lane].X},{pts[lane].Y})");
        }
        _selLabel.Text = string.Join("\n", parts);
    }

    private int GetStep() => (ModifierKeys & Keys.Shift) != 0 ? MoveStepFast : MoveStep;

    private void MoveSelected(int dx, int dy)
    {
        foreach (var (kind, lane) in _selected)
        {
            if (kind == "tap")
            {
                var p = _tapPts[lane];
                _tapPts[lane] = new Point(Math.Max(0, p.X + dx), Math.Max(0, p.Y + dy));
            }
            else
            {
                var p = _holdPts[lane];
                _holdPts[lane] = new Point(Math.Max(0, p.X + dx), Math.Max(0, p.Y + dy));
            }
        }
        RefreshSelectionUI();
    }

    // ── Preview ──────────────────────────────────────────────────

    private void BtnStartPreview_Click(object? sender, EventArgs e)
    {
        if (_previewing) StopPreview(); else StartPreview();
    }

    private void StartPreview()
    {
        _monitorBounds = FindTargetMonitor();
        _capture?.Dispose();
        _capture = new ScreenCapture(_monitorBounds.Left, _monitorBounds.Top, _monitorBounds.Width, _monitorBounds.Height);
        _timer?.Dispose();
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += Timer_Tick;
        _timer.Start();
        _previewing = true;
        _rescanRequested = true; // initial snapshot
        _btnStartPreview.Text = "Stop Preview";
        _btnRescan.Enabled = true;
    }

    private void StopPreview()
    {
        _timer?.Stop(); _timer?.Dispose(); _timer = null;
        _capture?.Dispose(); _capture = null;
        _previewing = false;
        _btnStartPreview.Text = "Start Preview";
        _btnRescan.Enabled = false;
    }

    private static Rectangle FindTargetMonitor()
    {
        var roblox = NativeApi.FindRobloxCenter();
        if (roblox != null)
        {
            var (cx, cy) = roblox.Value;
            foreach (var scr in Screen.AllScreens)
                if (scr.Bounds.Contains(cx, cy))
                    return scr.Bounds;
        }
        NativeApi.GetCursorPos(out var pt);
        foreach (var scr in Screen.AllScreens)
            if (scr.Bounds.Contains(pt.X, pt.Y))
                return scr.Bounds;
        return Screen.PrimaryScreen!.Bounds;
    }

    private Point ScreenToDisplay(Point screen) =>
        new((int)((screen.X - _monitorBounds.Left) * Scale),
            (int)((screen.Y - _monitorBounds.Top) * Scale));

    // ── Rendering ────────────────────────────────────────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_capture == null) return;
        if (_rescanRequested)
        {
            try { _capture.Grab(); }
            catch { return; }
            _rescanRequested = false;
        }

        int dw = (int)(_monitorBounds.Width * Scale);
        int dh = (int)(_monitorBounds.Height * Scale);

        var disp = new Bitmap(dw, dh);
        using (var g = Graphics.FromImage(disp))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;

            using var darkAttr = new ImageAttributes();
            float[][] darkMatrix =
            [
                [0.65f, 0, 0, 0, 0],
                [0, 0.65f, 0, 0, 0],
                [0, 0, 0.65f, 0, 0],
                [0, 0, 0, 1, 0],
                [0, 0, 0, 0, 1]
            ];
            darkAttr.SetColorMatrix(new ColorMatrix(darkMatrix));
            g.DrawImage(_capture.Bitmap,
                new Rectangle(0, 0, dw, dh),
                0, 0, _capture.Width, _capture.Height,
                GraphicsUnit.Pixel, darkAttr);

            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < 4; i++)
            {
                var col = LaneColors[i];
                using var pen = new Pen(col);
                using var brush = new SolidBrush(Color.FromArgb(100, col));

                foreach (var kind in new[] { "tap", "hold" })
                {
                    var pts = kind == "tap" ? _tapPts : _holdPts;
                    var dp = ScreenToDisplay(pts[i]);
                    int dx = Math.Clamp(dp.X, HandleSize, dw - HandleSize - 1);
                    int dy = Math.Clamp(dp.Y, HandleSize, dh - HandleSize - 1);

                    bool sel = _selected.Contains((kind, i));

                    if (sel)
                        g.FillRectangle(brush, dx - HandleSize, dy - HandleSize,
                            HandleSize * 2 + 1, HandleSize * 2 + 1);

                    pen.Width = sel ? 3 : (kind == "tap" ? 2 : 1);
                    g.DrawRectangle(pen, dx - HandleSize, dy - HandleSize,
                        HandleSize * 2, HandleSize * 2);

                    pen.Width = 1;
                    g.DrawLine(pen, dx - 5, dy, dx + 5, dy);
                    g.DrawLine(pen, dx, dy - 5, dx, dy + 5);

                    // Visible sample patch — actual (2*SampleHalf+1)² area scaled to preview.
                    int sampleSidePx = (2 * SampleHalf + 1);
                    int sh = Math.Max(3, (int)Math.Ceiling(sampleSidePx * Scale));
                    var patchRect = new Rectangle(dx - sh / 2, dy - sh / 2, sh, sh);

                    using (var patchFill = new SolidBrush(Color.FromArgb(80, col)))
                        g.FillRectangle(patchFill, patchRect);
                    using (var patchOutline = new Pen(col, 1))
                        g.DrawRectangle(patchOutline, patchRect);

                    // Live detection HUD
                    var monitorPt = pts[i];
                    int captureX = monitorPt.X - _monitorBounds.Left;
                    int captureY = monitorPt.Y - _monitorBounds.Top;

                    if (_capture != null
                        && captureX >= SampleHalf && captureY >= SampleHalf
                        && captureX < _capture.Width - SampleHalf
                        && captureY < _capture.Height - SampleHalf)
                    {
                        var analysis = _capture.AnalyzePatch(captureX, captureY, SampleHalf, includePixels: false);
                        int count = kind == "tap" ? analysis.WhiteCount : analysis.GrayCount;
                        int minPx = ConfigManager.Instance.Tuning.MinPixels;
                        bool pass = count >= minPx;
                        string mark = pass ? "\u2713" : "\u2717";
                        var hudColor = pass ? Color.FromArgb(80, 220, 120) : Color.FromArgb(255, 90, 90);

                        using var hudFont = new Font("MS Sans Serif", 8f, FontStyle.Bold);
                        using var hudBrush = new SolidBrush(hudColor);
                        using var shadowBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));

                        string hudText = $"{count} {mark}";
                        var size = g.MeasureString(hudText, hudFont);
                        int hudX = dx + sh / 2 + 4;
                        int hudY = dy - (int)(size.Height / 2);

                        g.FillRectangle(shadowBrush, hudX - 2, hudY, size.Width + 4, size.Height);
                        g.DrawString(hudText, hudFont, hudBrush, hudX, hudY);
                    }

                    string lbl = kind == "tap" ? $"T{LaneNames[i]}" : $"H{LaneNames[i]}";
                    using var font = new Font("MS Sans Serif", 7f);
                    using var lblBrush = new SolidBrush(col);
                    g.DrawString(lbl, font, lblBrush, dx - HandleSize, dy - HandleSize - 14);

                    if (sel)
                    {
                        string coordText = $"({pts[i].X}, {pts[i].Y})";
                        g.DrawString(coordText, font, lblBrush, dx + HandleSize + 3, dy - 5);
                    }
                }
            }

            g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), 0, 0, dw, 28);
            string msg;
            double nowSec = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            if (!string.IsNullOrEmpty(_lastSnapMessage) && nowSec - _lastSnapMessageAt < 3.0)
            {
                msg = _lastSnapMessage;
            }
            else
            {
                msg = _selected.Count > 0
                    ? $"{_selected.Count} selected  |  Use sidebar arrows or keyboard arrows  |  Shift = 10px"
                    : "Select points in sidebar  |  Ctrl/Shift+Click = multi-select";
            }
            using var barFont = new Font("MS Sans Serif", 8f);
            using var barBrush = new SolidBrush(Color.FromArgb(255, 220, 40));
            g.DrawString(msg, barFont, barBrush, 8, 7);
        }

        RefreshSelectionUI();
        RenderMagnifier();

        var old = _pic.Image;
        _pic.Image = disp;
        old?.Dispose();
    }

    private const int MagnifierContextHalf = 12; // 25×25 view window around the point

    private void RenderMagnifier()
    {
        if (_capture == null || _selected.Count != 1)
        {
            if (_magnifierPic.Image != null)
            {
                var prev = _magnifierPic.Image;
                _magnifierPic.Image = null;
                prev.Dispose();
            }
            _magnifierLabel.Text = _selected.Count == 0 ? "(no point selected)" : "(select one point)";
            return;
        }

        var (kind, lane) = _selected.First();
        var pts = kind == "tap" ? _tapPts : _holdPts;
        var monitorPt = pts[lane];
        int captureX = monitorPt.X - _monitorBounds.Left;
        int captureY = monitorPt.Y - _monitorBounds.Top;

        if (captureX < 0 || captureY < 0 || captureX >= _capture.Width || captureY >= _capture.Height)
        {
            _magnifierLabel.Text = "(out of capture bounds)";
            return;
        }

        var ctx = _capture.GetContextPatch(captureX, captureY, MagnifierContextHalf);
        int side = ctx.Width;
        var bmp = new Bitmap(side, side);

        // Real pixels, tinted where classified.
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                int idx = y * side + x;
                var raw = ctx.Colors[idx];
                var pk = ctx.Kinds[idx];
                Color drawn = pk switch
                {
                    PixelKind.White => Blend(raw, Color.FromArgb(80, 255, 120), 0.45f),
                    PixelKind.Gray  => Blend(raw, Color.FromArgb(80, 180, 255), 0.45f),
                    _               => raw
                };
                bmp.SetPixel(x, y, drawn);
            }
        }

        // White 1px outline around the actual sample patch (center 2*SampleHalf+1 area).
        int sampleStart = MagnifierContextHalf - SampleHalf;
        int sampleEnd   = MagnifierContextHalf + SampleHalf;
        var outlineColor = Color.White;
        for (int x = sampleStart; x <= sampleEnd; x++)
        {
            bmp.SetPixel(x, sampleStart, outlineColor);
            bmp.SetPixel(x, sampleEnd, outlineColor);
        }
        for (int y = sampleStart; y <= sampleEnd; y++)
        {
            bmp.SetPixel(sampleStart, y, outlineColor);
            bmp.SetPixel(sampleEnd, y, outlineColor);
        }

        // Center crosshair (1px) — helps align the eye to the exact pixel.
        bmp.SetPixel(MagnifierContextHalf, MagnifierContextHalf, Color.Red);

        var oldImg = _magnifierPic.Image;
        _magnifierPic.Image = bmp;
        oldImg?.Dispose();

        // Counts come from the sample patch (same as HUD).
        int whiteCount = 0, grayCount = 0;
        for (int y = sampleStart; y <= sampleEnd; y++)
        {
            for (int x = sampleStart; x <= sampleEnd; x++)
            {
                var pk = ctx.Kinds[y * side + x];
                if (pk == PixelKind.White) whiteCount++;
                else if (pk == PixelKind.Gray) grayCount++;
            }
        }
        int minPx = ConfigManager.Instance.Tuning.MinPixels;
        _magnifierLabel.Text = $"White: {whiteCount}  Gray: {grayCount}\nMinPixels: {minPx}";
    }

    private static Color Blend(Color a, Color b, float t)
    {
        int r = (int)(a.R * (1 - t) + b.R * t);
        int g = (int)(a.G * (1 - t) + b.G * t);
        int bl = (int)(a.B * (1 - t) + b.B * t);
        return Color.FromArgb(255, r, g, bl);
    }

    private void SnapSelectedToBody()
    {
        if (_capture == null)
        {
            FlashSnapMessage("Start preview first");
            return;
        }
        if (_selected.Count != 1)
        {
            FlashSnapMessage("Select exactly one point");
            return;
        }

        var (kind, lane) = _selected.First();
        var pts = kind == "tap" ? _tapPts : _holdPts;
        var current = pts[lane];

        int curCaptureX = current.X - _monitorBounds.Left;
        int curCaptureY = current.Y - _monitorBounds.Top;

        bool useWhite = kind == "tap";

        int currentCount = ScoreAt(curCaptureX, curCaptureY, useWhite);

        int bestX = curCaptureX;
        int bestY = curCaptureY;
        int bestCount = currentCount;

        const int searchRadius = 15;
        for (int dyS = -searchRadius; dyS <= searchRadius; dyS++)
        {
            for (int dxS = -searchRadius; dxS <= searchRadius; dxS++)
            {
                if (dxS == 0 && dyS == 0) continue;
                int sxS = curCaptureX + dxS;
                int syS = curCaptureY + dyS;
                if (sxS < SampleHalf || syS < SampleHalf
                    || sxS >= _capture.Width - SampleHalf
                    || syS >= _capture.Height - SampleHalf)
                    continue;

                int score = ScoreAt(sxS, syS, useWhite);
                if (score > bestCount)
                {
                    bestCount = score;
                    bestX = sxS;
                    bestY = syS;
                }
            }
        }

        if (bestCount <= currentCount)
        {
            FlashSnapMessage($"Already optimal ({currentCount} px)");
            return;
        }

        var snapped = new Point(bestX + _monitorBounds.Left, bestY + _monitorBounds.Top);
        if (kind == "tap") _tapPts[lane] = snapped;
        else _holdPts[lane] = snapped;

        FlashSnapMessage($"Snapped {(kind == "tap" ? "T" : "H")}{LaneNames[lane]}: {currentCount} → {bestCount} px");
        RefreshSelectionUI();
    }

    private int ScoreAt(int captureX, int captureY, bool useWhite)
    {
        if (_capture == null) return 0;
        var a = _capture.AnalyzePatch(captureX, captureY, SampleHalf, includePixels: false);
        return useWhite ? a.WhiteCount : a.GrayCount;
    }

    private void FlashSnapMessage(string msg)
    {
        _lastSnapMessage = msg;
        _lastSnapMessageAt = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
    }

    // ── Mouse drag on preview ────────────────────────────────────

    /// Convert a mouse position on the PictureBox (Zoom mode) to
    /// display-image coordinates, accounting for letterboxing.
    private Point MouseToDisplay(Point mouse)
    {
        if (_pic.Image == null) return mouse;
        float imgW = _pic.Image.Width;
        float imgH = _pic.Image.Height;
        float boxW = _pic.ClientSize.Width;
        float boxH = _pic.ClientSize.Height;
        float s = Math.Min(boxW / imgW, boxH / imgH);
        float offX = (boxW - imgW * s) / 2;
        float offY = (boxH - imgH * s) / 2;
        return new Point((int)((mouse.X - offX) / s), (int)((mouse.Y - offY) / s));
    }

    private Point DisplayToScreen(Point disp) =>
        new((int)(disp.X / Scale) + _monitorBounds.Left,
            (int)(disp.Y / Scale) + _monitorBounds.Top);

    private (string kind, int lane)? FindHandleAt(Point displayPt)
    {
        int dw = _pic.Image?.Width ?? 1;
        int dh = _pic.Image?.Height ?? 1;
        int bestDist = int.MaxValue;
        (string kind, int lane)? best = null;

        for (int i = 0; i < 4; i++)
        {
            foreach (var kind in new[] { "tap", "hold" })
            {
                var pts = kind == "tap" ? _tapPts : _holdPts;
                var dp = ScreenToDisplay(pts[i]);
                int dx = Math.Clamp(dp.X, HandleSize, dw - HandleSize - 1);
                int dy = Math.Clamp(dp.Y, HandleSize, dh - HandleSize - 1);
                int dist = Math.Abs(displayPt.X - dx) + Math.Abs(displayPt.Y - dy);
                if (dist < bestDist && dist <= HandleSize * 3)
                {
                    bestDist = dist;
                    best = (kind, i);
                }
            }
        }
        return best;
    }

    private void Pic_MouseDown(object? sender, MouseEventArgs e)
    {
        if (!_previewing || e.Button != MouseButtons.Left) return;
        var dp = MouseToDisplay(e.Location);
        var hit = FindHandleAt(dp);
        if (hit != null)
        {
            _dragging = hit;
            _isDragging = true;

            // Select the dragged handle (Ctrl or Shift = add to selection)
            bool multi = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            if (!multi && !_selected.Contains(hit.Value)) _selected.Clear();
            _selected.Add(hit.Value);

            // Capture drag anchor + every selected point's starting position for group drag.
            var dpStart = MouseToDisplay(e.Location);
            _dragStartScreen = DisplayToScreen(dpStart);
            _dragStartPts.Clear();
            foreach (var sel in _selected)
            {
                var startPt = sel.kind == "tap" ? _tapPts[sel.lane] : _holdPts[sel.lane];
                _dragStartPts[sel] = startPt;
            }

            RefreshSelectionUI();
            _pic.Cursor = Cursors.SizeAll;
        }
    }

    private void Pic_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging || _dragging == null) return;
        var dp = MouseToDisplay(e.Location);
        var screenPt = DisplayToScreen(dp);

        int deltaX = screenPt.X - _dragStartScreen.X;
        int deltaY = screenPt.Y - _dragStartScreen.Y;

        foreach (var sel in _selected)
        {
            if (!_dragStartPts.TryGetValue(sel, out var start)) continue;
            int nx = Math.Clamp(start.X + deltaX, _monitorBounds.Left, _monitorBounds.Right - 1);
            int ny = Math.Clamp(start.Y + deltaY, _monitorBounds.Top, _monitorBounds.Bottom - 1);
            if (sel.Item1 == "tap") _tapPts[sel.Item2] = new Point(nx, ny);
            else _holdPts[sel.Item2] = new Point(nx, ny);
        }

        RefreshSelectionUI();
    }

    private void Pic_MouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        _dragging = null;
        _pic.Cursor = Cursors.Cross;
    }

    // ── Signature capture walk ───────────────────────────────────

    private async void BtnCaptureSigs_Click(object? sender, EventArgs e)
    {
        if (_captureRunning) return;
        if (!_previewing || _capture == null)
        {
            MessageBox.Show(this, "Start Preview first so signatures can be captured from the live screen.",
                "Capture Signatures", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _captureRunning = true;
        _captureCancelRequested = false;
        _btnCaptureSigs.Enabled = false;
        _btnCaptureSkip.Visible = true;
        _btnCaptureCancel.Visible = true;
        _captureStatusLabel.Visible = true;

        bool completed = false;
        try
        {
            completed = await RunCaptureWalkAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Capture failed: {ex.Message}", "Capture Signatures",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _captureRunning = false;
            _captureSkipHoldRequested = false;
            _captureCancelRequested = false;
            _btnCaptureSigs.Enabled = true;
            _btnCaptureSkip.Visible = false;
            _btnCaptureCancel.Visible = false;
            _captureStatusLabel.Visible = false;
        }

        if (completed)
        {
            ConfigManager.Instance.SaveSettings();
            MessageBox.Show(this, "Signature capture complete and saved.", "Capture Signatures",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task<bool> RunCaptureWalkAsync()
    {
        var profile = ConfigManager.Instance.ActiveProfile;
        var sigs = profile.Signatures;

        for (int lane = 0; lane < 4; lane++)
        {
            // Tap entry (index 0) — required for every lane
            var tapEntry = await CaptureEntryAsync(lane, isHold: false);
            if (_captureCancelRequested) return false;
            if (tapEntry == null) return false;
            StoreEntry(sigs[lane], 0, tapEntry);

            // Hold entry (index 1) — user can skip
            _captureSkipHoldRequested = false;
            var holdEntry = await CaptureEntryAsync(lane, isHold: true);
            if (_captureCancelRequested) return false;
            if (holdEntry != null)
                StoreEntry(sigs[lane], 1, holdEntry);
            // if null (skipped), leave any existing hold entry at index 1 untouched
        }

        _captureStatusLabel.Text = "Saving...";
        return true;
    }

    private async Task<ColorSignatureEntry?> CaptureEntryAsync(int lane, bool isHold)
    {
        const int frameCount = 10;
        const int frameDelayMs = 20;
        var samples = new List<(byte r, byte g, byte b)>(frameCount);
        var pts = isHold ? _holdPts : _tapPts;
        var pt = pts[lane];
        int cx = pt.X - _monitorBounds.Left;
        int cy = pt.Y - _monitorBounds.Top;

        string kindWord = isHold ? "hold" : "tap";
        _captureStatusLabel.Text = $"Lane {LaneNames[lane]} {kindWord}...";
        _btnCaptureSkip.Enabled = isHold;

        for (int f = 0; f < frameCount; f++)
        {
            if (_captureCancelRequested) return null;
            if (isHold && _captureSkipHoldRequested) return null;
            if (_capture == null) return null;

            if (cx < 0 || cy < 0 || cx >= _capture.Width || cy >= _capture.Height)
            {
                // point outside capture — skip frame
                await Task.Delay(frameDelayMs);
                continue;
            }

            try
            {
                _capture.Grab();
                var px = _capture.ReadPixel(cx, cy);
                samples.Add(px);
            }
            catch
            {
                // swallow transient capture errors; keep going
            }

            _captureStatusLabel.Text = $"Lane {LaneNames[lane]} {kindWord} ({f + 1}/{frameCount})";
            await Task.Delay(frameDelayMs);
        }

        if (samples.Count == 0) return null;
        return SignatureCapture.BuildEntry(samples);
    }

    private static void StoreEntry(ColorSignature sig, int index, ColorSignatureEntry entry)
    {
        while (sig.Entries.Count <= index)
            sig.Entries.Add(new ColorSignatureEntry(0, 0, 0, 8));
        sig.Entries[index] = entry;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopPreview();
        base.Dispose(disposing);
    }

    // ── Keyboard arrows (form must have KeyPreview = true) ───────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        int step = (keyData & Keys.Shift) != 0 ? MoveStepFast : MoveStep;
        var key = keyData & Keys.KeyCode;

        switch (key)
        {
            case Keys.Up:    MoveSelected(0, -step); return true;
            case Keys.Down:  MoveSelected(0, step);  return true;
            case Keys.Left:  MoveSelected(-step, 0); return true;
            case Keys.Right: MoveSelected(step, 0);  return true;
        }

        if (keyData == (Keys.Control | Keys.A))
        {
            for (int i = 0; i < 4; i++) { _selected.Add(("tap", i)); _selected.Add(("hold", i)); }
            RefreshSelectionUI();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
