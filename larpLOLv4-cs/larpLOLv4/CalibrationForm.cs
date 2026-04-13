using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace larpLOLv4;

/// Calibration window — drag T/H points over a live screen capture.
internal sealed class CalibrationForm : Form
{
    private new const float Scale = 0.5f;
    private const int GrabRadius = 18;
    private const int HandleSize = 13;
    private const int SampleHalf = 3;

    private Point[] _tapPts;
    private Point[] _holdPts;

    private (string kind, int lane)? _dragging;
    private (string kind, int lane)? _hover;

    private readonly PictureBox _pic;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ScreenCapture _capture;
    private Rectangle _monitorBounds;

    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(255, 80, 80),
        Color.FromArgb(80, 255, 80),
        Color.FromArgb(255, 180, 0),
        Color.FromArgb(255, 80, 200)
    ];

    private static readonly string[] LaneNames = ["Z", "X", ",", "."];

    public CalibrationForm()
    {
        (_tapPts, _holdPts) = CoordsManager.Load();

        Text = "Rhythm Macro — Calibration";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;

        // Find monitor to calibrate on
        _monitorBounds = FindTargetMonitor();

        int dw = (int)(_monitorBounds.Width * Scale);
        int dh = (int)(_monitorBounds.Height * Scale);
        ClientSize = new Size(dw, dh);

        _capture = new ScreenCapture(
            _monitorBounds.Left, _monitorBounds.Top,
            _monitorBounds.Width, _monitorBounds.Height);

        _pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _pic.MouseDown += Pic_MouseDown;
        _pic.MouseUp += Pic_MouseUp;
        _pic.MouseMove += Pic_MouseMove;
        Controls.Add(_pic);

        _timer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 fps
        _timer.Tick += Timer_Tick;
        _timer.Start();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.S)
            {
                CoordsManager.Save(_tapPts, _holdPts);
                Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    private Rectangle FindTargetMonitor()
    {
        // Try to find Roblox window
        var roblox = NativeApi.FindRobloxCenter();
        if (roblox != null)
        {
            var (cx, cy) = roblox.Value;
            foreach (var scr in Screen.AllScreens)
            {
                if (scr.Bounds.Contains(cx, cy))
                    return scr.Bounds;
            }
        }

        // Fall back to monitor under cursor
        NativeApi.GetCursorPos(out var pt);
        foreach (var scr in Screen.AllScreens)
        {
            if (scr.Bounds.Contains(pt.X, pt.Y))
                return scr.Bounds;
        }

        return Screen.PrimaryScreen!.Bounds;
    }

    private Point ScreenToDisplay(Point screen)
    {
        return new Point(
            (int)((screen.X - _monitorBounds.Left) * Scale),
            (int)((screen.Y - _monitorBounds.Top) * Scale));
    }

    private Point DisplayToScreen(Point display)
    {
        return new Point(
            (int)(display.X / Scale) + _monitorBounds.Left,
            (int)(display.Y / Scale) + _monitorBounds.Top);
    }

    private (string kind, int lane)? FindNearest(int mx, int my)
    {
        int bestDist = GrabRadius * GrabRadius;
        (string kind, int lane)? best = null;

        for (int i = 0; i < 4; i++)
        {
            var dp = ScreenToDisplay(_tapPts[i]);
            int d = (mx - dp.X) * (mx - dp.X) + (my - dp.Y) * (my - dp.Y);
            if (d <= bestDist) { bestDist = d; best = ("tap", i); }

            dp = ScreenToDisplay(_holdPts[i]);
            d = (mx - dp.X) * (mx - dp.X) + (my - dp.Y) * (my - dp.Y);
            if (d <= bestDist) { bestDist = d; best = ("hold", i); }
        }

        return best;
    }

    private void Pic_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            _dragging = FindNearest(e.X, e.Y);
    }

    private void Pic_MouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = null;
    }

    private void Pic_MouseMove(object? sender, MouseEventArgs e)
    {
        _hover = FindNearest(e.X, e.Y);

        if (_dragging != null)
        {
            var sp = DisplayToScreen(new Point(e.X, e.Y));
            sp = new Point(Math.Max(0, sp.X), Math.Max(0, sp.Y));

            if (_dragging.Value.kind == "tap")
                _tapPts[_dragging.Value.lane] = sp;
            else
                _holdPts[_dragging.Value.lane] = sp;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _capture.Grab();
        }
        catch { return; }

        int dw = (int)(_monitorBounds.Width * Scale);
        int dh = (int)(_monitorBounds.Height * Scale);

        var disp = new Bitmap(dw, dh);
        using (var g = Graphics.FromImage(disp))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;

            // Draw darkened screen capture
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

                    bool hot = (_dragging == (kind, i)) ||
                               (_dragging == null && _hover == (kind, i));

                    // Highlight on hover/drag
                    if (hot)
                    {
                        g.FillRectangle(brush,
                            dx - HandleSize, dy - HandleSize,
                            HandleSize * 2 + 1, HandleSize * 2 + 1);
                    }

                    // Outer box
                    pen.Width = kind == "tap" ? (hot ? 3 : 2) : (hot ? 2 : 1);
                    g.DrawRectangle(pen,
                        dx - HandleSize, dy - HandleSize,
                        HandleSize * 2, HandleSize * 2);

                    // Cross marker
                    pen.Width = 1;
                    g.DrawLine(pen, dx - 5, dy, dx + 5, dy);
                    g.DrawLine(pen, dx, dy - 5, dx, dy + 5);

                    // Inner sample box
                    int sh = Math.Max(1, (int)(SampleHalf * Scale));
                    using var innerPen = new Pen(kind == "tap" ? Color.White : Color.LightGray, 1);
                    g.DrawRectangle(innerPen, dx - sh, dy - sh, sh * 2, sh * 2);

                    // Label
                    string lbl = kind == "tap" ? $"T{LaneNames[i]}" : $"H{LaneNames[i]}";
                    using var font = new Font("Segoe UI", 7f);
                    g.DrawString(lbl, font, new SolidBrush(col), dx - HandleSize, dy - HandleSize - 14);

                    // Coordinate text on hover
                    if (hot)
                    {
                        string coordText = $"({pts[i].X}, {pts[i].Y})";
                        g.DrawString(coordText, font, new SolidBrush(col),
                            dx + HandleSize + 3, dy - 5);
                    }
                }
            }

            // Top bar
            g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), 0, 0, dw, 28);

            string msg = _dragging != null
                ? $"Dragging {_dragging.Value.kind.ToUpper()} {LaneNames[_dragging.Value.lane]}  |  S = Save & Quit   ESC = Cancel"
                : "Drag T / H boxes to align  |  T=thick=tap  H=thin=hold  |  S = Save & Quit   ESC = Cancel";

            using var barFont = new Font("Segoe UI", 8.5f);
            var barColor = _dragging != null ? LaneColors[_dragging.Value.lane] : Color.FromArgb(255, 220, 0);
            g.DrawString(msg, barFont, new SolidBrush(barColor), 8, 6);
        }

        var old = _pic.Image;
        _pic.Image = disp;
        old?.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _capture.Dispose();
        base.OnFormClosed(e);
    }
}
