using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// Calibration window — select T/H points via sidebar buttons, move with arrows.
internal sealed class CalibrationForm : Form
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
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ScreenCapture _capture;
    private Rectangle _monitorBounds;

    // Drag state
    private (string kind, int lane)? _dragging;
    private bool _isDragging;

    // Sidebar
    private readonly Panel _sidebar;
    private readonly Label _selLabel;
    private readonly Button[] _tapBtns = new Button[4];
    private readonly Button[] _holdBtns = new Button[4];

    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(255, 220, 40),
        Color.FromArgb(40, 220, 255),
        Color.FromArgb(255, 100, 100),
        Color.FromArgb(100, 255, 100)
    ];

    private static readonly string[] LaneNames = ["Z", "X", ",", "."];

    public CalibrationForm()
    {
        (_tapPts, _holdPts) = ConfigManager.Instance.LoadCoords();

        Text = "SoulBeats Pro — Calibration";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;

        _monitorBounds = FindTargetMonitor();

        int dw = (int)(_monitorBounds.Width * Scale);
        int dh = (int)(_monitorBounds.Height * Scale);
        int sidebarWidth = 170;
        ClientSize = new Size(dw + sidebarWidth, dh);

        _capture = new ScreenCapture(
            _monitorBounds.Left, _monitorBounds.Top,
            _monitorBounds.Width, _monitorBounds.Height);

        // ── Sidebar ──────────────────────────────────────────────────
        _sidebar = new Panel
        {
            Location = new Point(dw, 0),
            Size = new Size(sidebarWidth, dh),
            BackColor = Color.FromArgb(30, 30, 30),
            AutoScroll = true
        };

        var sideFont = new Font("Segoe UI", 8f);
        var sideBoldFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        Color btnBg = Color.FromArgb(55, 55, 55);
        Color btnFg = Color.White;
        int pad = 8;
        int btnW = sidebarWidth - pad * 2;
        int sy = 8;

        // ── Select Boxes section ─────────────────────────────────
        var selectTitle = new Label
        {
            Text = "Select Boxes",
            Font = sideBoldFont,
            ForeColor = Color.FromArgb(255, 220, 40),
            AutoSize = true,
            Location = new Point(pad, sy)
        };
        _sidebar.Controls.Add(selectTitle);
        sy += 22;

        // Tap row label
        var tapLabel = new Label
        {
            Text = "Tap:",
            Font = sideFont,
            ForeColor = Color.LightGray,
            AutoSize = true,
            Location = new Point(pad, sy + 4)
        };
        _sidebar.Controls.Add(tapLabel);

        // 4 tap buttons in a row
        int smallBtnW = 32;
        int btnStartX = 38;
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var col = LaneColors[i];
            _tapBtns[i] = new Button
            {
                Text = LaneNames[i],
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                ForeColor = col,
                BackColor = btnBg,
                Size = new Size(smallBtnW, 26),
                Location = new Point(btnStartX + i * (smallBtnW + 2), sy)
            };
            _tapBtns[i].FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            _tapBtns[i].Click += (_, _) => ToggleSelection("tap", idx);
            _sidebar.Controls.Add(_tapBtns[i]);
        }
        sy += 30;

        // Hold row label
        var holdLabel = new Label
        {
            Text = "Hold:",
            Font = sideFont,
            ForeColor = Color.LightGray,
            AutoSize = true,
            Location = new Point(pad, sy + 4)
        };
        _sidebar.Controls.Add(holdLabel);

        // 4 hold buttons in a row
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var col = LaneColors[i];
            _holdBtns[i] = new Button
            {
                Text = LaneNames[i],
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                ForeColor = col,
                BackColor = btnBg,
                Size = new Size(smallBtnW, 26),
                Location = new Point(btnStartX + i * (smallBtnW + 2), sy)
            };
            _holdBtns[i].FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            _holdBtns[i].Click += (_, _) => ToggleSelection("hold", idx);
            _sidebar.Controls.Add(_holdBtns[i]);
        }
        sy += 32;

        // Quick select buttons
        var selectAllTap = MakeSideBtn("Select All Tap", sideFont, btnW, btnBg, btnFg, pad, sy);
        selectAllTap.Click += (_, _) => { _selected.Clear(); for (int i = 0; i < 4; i++) _selected.Add(("tap", i)); RefreshSelectionUI(); };
        _sidebar.Controls.Add(selectAllTap);
        sy += 28;

        var selectAllHold = MakeSideBtn("Select All Hold", sideFont, btnW, btnBg, btnFg, pad, sy);
        selectAllHold.Click += (_, _) => { _selected.Clear(); for (int i = 0; i < 4; i++) _selected.Add(("hold", i)); RefreshSelectionUI(); };
        _sidebar.Controls.Add(selectAllHold);
        sy += 28;

        var selectAll = MakeSideBtn("Select All", sideFont, btnW, btnBg, btnFg, pad, sy);
        selectAll.Click += (_, _) => { for (int i = 0; i < 4; i++) { _selected.Add(("tap", i)); _selected.Add(("hold", i)); } RefreshSelectionUI(); };
        _sidebar.Controls.Add(selectAll);
        sy += 28;

        var clearSel = MakeSideBtn("Clear Selection", sideFont, btnW, btnBg, btnFg, pad, sy);
        clearSel.Click += (_, _) => { _selected.Clear(); RefreshSelectionUI(); };
        _sidebar.Controls.Add(clearSel);
        sy += 36;

        // ── Separator ────────────────────────────────────────────
        _sidebar.Controls.Add(new Panel { BackColor = Color.FromArgb(70, 70, 70), Size = new Size(btnW, 1), Location = new Point(pad, sy) });
        sy += 8;

        // ── Move section ─────────────────────────────────────────
        var moveTitle = new Label
        {
            Text = "Move Selected",
            Font = sideBoldFont,
            ForeColor = Color.FromArgb(255, 220, 40),
            AutoSize = true,
            Location = new Point(pad, sy)
        };
        _sidebar.Controls.Add(moveTitle);
        sy += 22;

        // Arrow buttons
        var arrowSize = new Size(42, 34);
        int arrowLeft = pad;
        int arrowMid = arrowLeft + 44;
        int arrowRight = arrowMid + 44;

        var upBtn = MakeArrowBtn("^", arrowSize, btnBg, btnFg, new Point(arrowMid, sy));
        upBtn.Click += (_, _) => MoveSelected(0, -GetStep());
        _sidebar.Controls.Add(upBtn);
        sy += 36;

        var leftBtn = MakeArrowBtn("<", arrowSize, btnBg, btnFg, new Point(arrowLeft, sy));
        leftBtn.Click += (_, _) => MoveSelected(-GetStep(), 0);
        _sidebar.Controls.Add(leftBtn);

        var rightBtn = MakeArrowBtn(">", arrowSize, btnBg, btnFg, new Point(arrowRight, sy));
        rightBtn.Click += (_, _) => MoveSelected(GetStep(), 0);
        _sidebar.Controls.Add(rightBtn);
        sy += 36;

        var downBtn = MakeArrowBtn("v", arrowSize, btnBg, btnFg, new Point(arrowMid, sy));
        downBtn.Click += (_, _) => MoveSelected(0, GetStep());
        _sidebar.Controls.Add(downBtn);
        sy += 44;

        var hintLbl = new Label
        {
            Text = "Shift = 10px | Arrow keys work too",
            Font = sideFont,
            ForeColor = Color.Gray,
            Size = new Size(btnW, 30),
            Location = new Point(pad, sy)
        };
        _sidebar.Controls.Add(hintLbl);
        sy += 30;

        // ── Selection info ───────────────────────────────────────
        _selLabel = new Label
        {
            Text = "",
            Font = sideFont,
            ForeColor = Color.White,
            Size = new Size(btnW, 64),
            Location = new Point(pad, sy)
        };
        _sidebar.Controls.Add(_selLabel);
        sy += 68;

        // ── Separator ────────────────────────────────────────────
        _sidebar.Controls.Add(new Panel { BackColor = Color.FromArgb(70, 70, 70), Size = new Size(btnW, 1), Location = new Point(pad, sy) });
        sy += 8;

        // ── Save / Cancel ────────────────────────────────────────
        var saveBtn = new Button
        {
            Text = "Save && Quit",
            Font = sideBoldFont,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(100, 255, 100),
            BackColor = Color.FromArgb(40, 60, 40),
            Size = new Size(btnW, 28),
            Location = new Point(pad, sy)
        };
        saveBtn.FlatAppearance.BorderColor = Color.FromArgb(100, 255, 100);
        saveBtn.Click += (_, _) => { ConfigManager.Instance.SaveCoords(_tapPts, _holdPts); Close(); };
        _sidebar.Controls.Add(saveBtn);
        sy += 32;

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Font = sideFont,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(255, 100, 100),
            BackColor = Color.FromArgb(60, 40, 40),
            Size = new Size(btnW, 28),
            Location = new Point(pad, sy)
        };
        cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 100, 100);
        cancelBtn.Click += (_, _) => Close();
        _sidebar.Controls.Add(cancelBtn);

        // ── Picture box ──────────────────────────────────────────
        _pic = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(dw, dh),
            SizeMode = PictureBoxSizeMode.Normal,
            Cursor = Cursors.Cross
        };

        _pic.MouseDown += Pic_MouseDown;
        _pic.MouseMove += Pic_MouseMove;
        _pic.MouseUp += Pic_MouseUp;

        Controls.Add(_pic);
        Controls.Add(_sidebar);

        // ── Timer ────────────────────────────────────────────────
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        // ── Keyboard ─────────────────────────────────────────────
        KeyDown += OnKeyDown;

        // Auto-select first tap box
        _selected.Add(("tap", 0));
        RefreshSelectionUI();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Button MakeSideBtn(string text, Font font, int w, Color bg, Color fg, int x, int y)
    {
        var btn = new Button
        {
            Text = text,
            Font = font,
            FlatStyle = FlatStyle.Flat,
            ForeColor = fg,
            BackColor = bg,
            Size = new Size(w, 24),
            Location = new Point(x, y)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        return btn;
    }

    private static Button MakeArrowBtn(string text, Size size, Color bg, Color fg, Point loc)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            ForeColor = fg,
            BackColor = bg,
            Size = size,
            Location = loc
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        return btn;
    }

    private void ToggleSelection(string kind, int lane)
    {
        bool ctrl = (ModifierKeys & Keys.Control) != 0;
        var item = (kind, lane);

        if (ctrl)
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
        // Update button borders to show selection state
        for (int i = 0; i < 4; i++)
        {
            bool tapSel = _selected.Contains(("tap", i));
            _tapBtns[i].FlatAppearance.BorderColor = tapSel ? LaneColors[i] : Color.FromArgb(80, 80, 80);
            _tapBtns[i].FlatAppearance.BorderSize = tapSel ? 2 : 1;

            bool holdSel = _selected.Contains(("hold", i));
            _holdBtns[i].FlatAppearance.BorderColor = holdSel ? LaneColors[i] : Color.FromArgb(80, 80, 80);
            _holdBtns[i].FlatAppearance.BorderSize = holdSel ? 2 : 1;
        }

        // Update info label
        if (_selected.Count == 0)
        {
            _selLabel.Text = "No selection";
            return;
        }
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

    // ── Keyboard ─────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        int step = (e.Modifiers & Keys.Shift) != 0 ? MoveStepFast : MoveStep;

        switch (e.KeyCode)
        {
            case Keys.Up:    MoveSelected(0, -step); e.Handled = true; break;
            case Keys.Down:  MoveSelected(0, step);  e.Handled = true; break;
            case Keys.Left:  MoveSelected(-step, 0); e.Handled = true; break;
            case Keys.Right: MoveSelected(step, 0);  e.Handled = true; break;
            case Keys.S:
                ConfigManager.Instance.SaveCoords(_tapPts, _holdPts);
                Close();
                e.Handled = true;
                break;
            case Keys.Escape:
                Close();
                e.Handled = true;
                break;
            case Keys.A when (e.Modifiers & Keys.Control) != 0:
                for (int i = 0; i < 4; i++) { _selected.Add(("tap", i)); _selected.Add(("hold", i)); }
                RefreshSelectionUI();
                e.Handled = true;
                break;
        }
    }

    // ── Rendering ────────────────────────────────────────────────

    private Rectangle FindTargetMonitor()
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

    private void Timer_Tick(object? sender, EventArgs e)
    {
        try { _capture.Grab(); }
        catch { return; }

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

                    int sh = Math.Max(1, (int)(SampleHalf * Scale));
                    using var innerPen = new Pen(kind == "tap" ? Color.White : Color.LightGray, 1);
                    g.DrawRectangle(innerPen, dx - sh, dy - sh, sh * 2, sh * 2);

                    string lbl = kind == "tap" ? $"T{LaneNames[i]}" : $"H{LaneNames[i]}";
                    using var font = new Font("Segoe UI", 7f);
                    g.DrawString(lbl, font, new SolidBrush(col), dx - HandleSize, dy - HandleSize - 14);

                    if (sel)
                    {
                        string coordText = $"({pts[i].X}, {pts[i].Y})";
                        g.DrawString(coordText, font, new SolidBrush(col), dx + HandleSize + 3, dy - 5);
                    }
                }
            }

            // Top bar
            g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), 0, 0, dw, 28);
            string msg = _selected.Count > 0
                ? $"{_selected.Count} selected  |  Use sidebar or arrow keys to move  |  Ctrl+Click buttons = multi-select"
                : "Select boxes in sidebar  |  Arrow keys to move  |  S = Save  |  ESC = Cancel";
            using var barFont = new Font("Segoe UI", 8.5f);
            g.DrawString(msg, barFont, new SolidBrush(Color.FromArgb(255, 220, 40)), 8, 6);
        }

        var old = _pic.Image;
        _pic.Image = disp;
        old?.Dispose();
    }

    // ── Mouse drag on preview ────────────────────────────────────

    private Point DisplayToScreen(Point disp) =>
        new((int)(disp.X / Scale) + _monitorBounds.Left,
            (int)(disp.Y / Scale) + _monitorBounds.Top);

    private (string kind, int lane)? FindHandleAt(Point displayPt)
    {
        int dw = (int)(_monitorBounds.Width * Scale);
        int dh = (int)(_monitorBounds.Height * Scale);
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
        if (e.Button != MouseButtons.Left) return;
        // PictureBox is Normal mode — mouse pos = display pos
        var hit = FindHandleAt(e.Location);
        if (hit != null)
        {
            _dragging = hit;
            _isDragging = true;

            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            if (!ctrl) _selected.Clear();
            _selected.Add(hit.Value);
            RefreshSelectionUI();
            _pic.Cursor = Cursors.SizeAll;
        }
    }

    private void Pic_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging || _dragging == null) return;
        var screenPt = DisplayToScreen(e.Location);

        int sx = Math.Clamp(screenPt.X, _monitorBounds.Left, _monitorBounds.Right - 1);
        int sy = Math.Clamp(screenPt.Y, _monitorBounds.Top, _monitorBounds.Bottom - 1);

        var (kind, lane) = _dragging.Value;
        if (kind == "tap")
            _tapPts[lane] = new Point(sx, sy);
        else
            _holdPts[lane] = new Point(sx, sy);

        RefreshSelectionUI();
    }

    private void Pic_MouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        _dragging = null;
        _pic.Cursor = Cursors.Cross;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _capture.Dispose();
        base.OnFormClosed(e);
    }
}
