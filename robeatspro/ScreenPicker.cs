using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace SoulBeatsPro;

/// <summary>
/// Fullscreen overlay that lets the user pick a color from anywhere on screen.
/// Takes a screenshot on open and renders a magnifier + crosshair around the cursor.
/// Usage:
///   using var picker = new ScreenPicker();
///   if (picker.ShowDialog() == DialogResult.OK &amp;&amp; picker.PickedColor != null)
///       Color c = picker.PickedColor.Value;
/// </summary>
internal sealed class ScreenPicker : Form
{
    private Bitmap _screenshot = null!;
    private Bitmap _backBuffer = null!;
    private Graphics _bufferGfx = null!;

    // Virtual screen bounds (all monitors)
    private Rectangle _virtualBounds;

    // Current mouse position in virtual-screen coords
    private Point _cursorPos;
    private Color _pixelColor;

    // Magnifier settings
    private const int MagCapture = 9;   // 9x9 pixels captured
    private const int MagSize = 126;     // rendered at 126x126 (9*14)
    private const int MagPixel = MagSize / MagCapture; // 14px per source pixel

    // Timer-based refresh
    private readonly System.Windows.Forms.Timer _timer;

    public Color? PickedColor { get; private set; }

    public ScreenPicker()
    {
        // Measure virtual screen
        _virtualBounds = SystemInformation.VirtualScreen;

        // Form setup
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(_virtualBounds.Left, _virtualBounds.Top);
        Size = new Size(_virtualBounds.Width, _virtualBounds.Height);
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;

        // Capture screen
        CaptureScreen();

        // Allocate back buffer
        _backBuffer = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppArgb);
        _bufferGfx = Graphics.FromImage(_backBuffer);

        // Refresh timer (~33 fps)
        _timer = new System.Windows.Forms.Timer { Interval = 30 };
        _timer.Tick += (_, _) => RefreshOverlay();
        _timer.Start();
    }

    private void CaptureScreen()
    {
        _screenshot = new Bitmap(_virtualBounds.Width, _virtualBounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(_screenshot);
        g.CopyFromScreen(_virtualBounds.Left, _virtualBounds.Top, 0, 0,
            new Size(_virtualBounds.Width, _virtualBounds.Height),
            CopyPixelOperation.SourceCopy);
    }

    // ── Rendering ───────────────────────────────────────────────

    private void RefreshOverlay()
    {
        NativeApi.GetCursorPos(out var pt);
        _cursorPos = new Point(pt.X, pt.Y);

        // Pixel coord relative to our screenshot bitmap
        int bx = _cursorPos.X - _virtualBounds.Left;
        int by = _cursorPos.Y - _virtualBounds.Top;

        // Sample color
        if (bx >= 0 && bx < _screenshot.Width && by >= 0 && by < _screenshot.Height)
            _pixelColor = _screenshot.GetPixel(bx, by);
        else
            _pixelColor = Color.Black;

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // Draw the screenshot as background
        g.DrawImageUnscaled(_screenshot, 0, 0);

        // Convert cursor to local form coords
        int lx = _cursorPos.X - _virtualBounds.Left;
        int ly = _cursorPos.Y - _virtualBounds.Top;

        DrawMagnifier(g, lx, ly);
        DrawCrosshair(g, lx, ly);
    }

    private void DrawMagnifier(Graphics g, int lx, int ly)
    {
        // Position the magnifier near cursor, flipping side if near edges
        int magX = lx + 24;
        int magY = ly + 24;
        int totalW = MagSize + 4;
        int totalH = MagSize + 4 + 24 + 24; // mag + border + swatch + text

        if (magX + totalW > _virtualBounds.Width)
            magX = lx - totalW - 10;
        if (magY + totalH > _virtualBounds.Height)
            magY = ly - totalH - 10;

        // Clamp to visible area
        magX = Math.Max(2, Math.Min(magX, _virtualBounds.Width - totalW - 2));
        magY = Math.Max(2, Math.Min(magY, _virtualBounds.Height - totalH - 2));

        // Background panel
        var panelRect = new Rectangle(magX - 2, magY - 2, totalW, totalH);
        using (var bgBrush = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            g.FillRectangle(bgBrush, panelRect);
        using (var borderPen = new Pen(Color.FromArgb(200, 200, 200, 200), 1f))
            g.DrawRectangle(borderPen, panelRect);

        // Draw magnified pixels
        int half = MagCapture / 2;
        int srcX = lx - half;
        int srcY = ly - half;

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        for (int py = 0; py < MagCapture; py++)
        {
            for (int px = 0; px < MagCapture; px++)
            {
                int sx = srcX + px;
                int sy = srcY + py;
                Color c;
                if (sx >= 0 && sx < _screenshot.Width && sy >= 0 && sy < _screenshot.Height)
                    c = _screenshot.GetPixel(sx, sy);
                else
                    c = Color.Black;

                using var brush = new SolidBrush(c);
                g.FillRectangle(brush,
                    magX + px * MagPixel,
                    magY + py * MagPixel,
                    MagPixel, MagPixel);
            }
        }

        // Grid lines over magnifier
        using (var gridPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
        {
            for (int i = 0; i <= MagCapture; i++)
            {
                g.DrawLine(gridPen, magX + i * MagPixel, magY, magX + i * MagPixel, magY + MagSize);
                g.DrawLine(gridPen, magX, magY + i * MagPixel, magX + MagSize, magY + i * MagPixel);
            }
        }

        // Highlight center pixel
        int cx = magX + half * MagPixel;
        int cy = magY + half * MagPixel;
        using (var highlightPen = new Pen(Color.White, 2f))
            g.DrawRectangle(highlightPen, cx, cy, MagPixel, MagPixel);

        // Color swatch below magnifier
        int swatchY = magY + MagSize + 4;
        using (var swatchBrush = new SolidBrush(_pixelColor))
            g.FillRectangle(swatchBrush, magX, swatchY, MagSize, 20);
        using (var swatchBorder = new Pen(Color.FromArgb(180, 180, 180, 180), 1f))
            g.DrawRectangle(swatchBorder, magX, swatchY, MagSize, 20);

        // RGB text below swatch
        int textY = swatchY + 22;
        string rgbText = $"R: {_pixelColor.R}  G: {_pixelColor.G}  B: {_pixelColor.B}";
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Consolas", 9f, FontStyle.Bold);
        using (var shadowBrush = new SolidBrush(Color.Black))
            g.DrawString(rgbText, font, shadowBrush, magX + 1, textY + 1);
        using (var textBrush = new SolidBrush(Color.White))
            g.DrawString(rgbText, font, textBrush, magX, textY);
    }

    private static void DrawCrosshair(Graphics g, int lx, int ly)
    {
        const int gap = 6;
        const int armLen = 20;

        using var pen = new Pen(Color.White, 1.5f);
        pen.DashStyle = DashStyle.Solid;

        // Four arms with a gap in the center
        g.DrawLine(pen, lx - gap - armLen, ly, lx - gap, ly);
        g.DrawLine(pen, lx + gap, ly, lx + gap + armLen, ly);
        g.DrawLine(pen, lx, ly - gap - armLen, lx, ly - gap);
        g.DrawLine(pen, lx, ly + gap, lx, ly + gap + armLen);

        // Dark outline for contrast
        using var penDark = new Pen(Color.FromArgb(160, 0, 0, 0), 0.8f);
        g.DrawLine(penDark, lx - gap - armLen - 1, ly + 1, lx - gap + 1, ly + 1);
        g.DrawLine(penDark, lx + gap - 1, ly + 1, lx + gap + armLen + 1, ly + 1);
        g.DrawLine(penDark, lx + 1, ly - gap - armLen - 1, lx + 1, ly - gap + 1);
        g.DrawLine(penDark, lx + 1, ly + gap - 1, lx + 1, ly + gap + armLen + 1);
    }

    // ── Input ───────────────────────────────────────────────────

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            PickedColor = _pixelColor;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            PickedColor = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    // ── Cleanup ─────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _bufferGfx.Dispose();
            _backBuffer.Dispose();
            _screenshot.Dispose();
        }
        base.Dispose(disposing);
    }

    // Prevent background flicker
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }
}
