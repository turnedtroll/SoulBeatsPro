using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// <summary>
/// HSV color wheel control with an outer hue ring and inner saturation/value square.
/// </summary>
internal sealed class ColorWheelControl : UserControl
{
    // ── HSV state ───────────────────────────────────────────────
    private float _hue;        // 0..360
    private float _saturation; // 0..1
    private float _value;      // 0..1

    private bool _draggingRing;
    private bool _draggingSquare;

    // Cached geometry (recomputed on resize)
    private PointF _center;
    private float _outerRadius;
    private float _innerRadius;
    private RectangleF _squareRect;

    // Pre-rendered hue ring bitmap (regenerated on resize)
    private Bitmap? _ringBitmap;

    public event EventHandler<Color>? ColorChanged;

    public Color SelectedColor
    {
        get => HsvToRgb(_hue, _saturation, _value);
        set => SetColor(value);
    }

    public ColorWheelControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Size = new Size(180, 180);
        _hue = 0;
        _saturation = 1;
        _value = 1;
        RecalcLayout();
    }

    public void SetColor(Color c)
    {
        RgbToHsv(c, out _hue, out _saturation, out _value);
        Invalidate();
    }

    // ── Layout ──────────────────────────────────────────────────

    private void RecalcLayout()
    {
        float side = Math.Min(Width, Height);
        _center = new PointF(Width / 2f, Height / 2f);
        _outerRadius = side / 2f - 2f;
        _innerRadius = _outerRadius * 0.68f;

        // Inner square inscribed in the inner circle (axis-aligned)
        float halfSq = _innerRadius / MathF.Sqrt(2f);
        _squareRect = new RectangleF(
            _center.X - halfSq, _center.Y - halfSq,
            halfSq * 2, halfSq * 2);

        _ringBitmap?.Dispose();
        _ringBitmap = null;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalcLayout();
    }

    // ── Paint ───────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.Bilinear;

        DrawHueRing(g);
        DrawSatValSquare(g);
        DrawSelectionIndicators(g);
    }

    private void DrawHueRing(Graphics g)
    {
        // Lazily generate the ring bitmap at current size
        if (_ringBitmap == null || _ringBitmap.Width != Width || _ringBitmap.Height != Height)
        {
            _ringBitmap?.Dispose();
            _ringBitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using var bg = Graphics.FromImage(_ringBitmap);
            bg.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw 360 thin pie slices
            for (int deg = 0; deg < 360; deg++)
            {
                Color c = HsvToRgb(deg, 1f, 1f);
                using var brush = new SolidBrush(c);
                using var path = new GraphicsPath();
                path.AddPie(
                    _center.X - _outerRadius, _center.Y - _outerRadius,
                    _outerRadius * 2, _outerRadius * 2,
                    deg - 1.5f, 3f);
                // Cut out the inner circle
                path.AddEllipse(
                    _center.X - _innerRadius, _center.Y - _innerRadius,
                    _innerRadius * 2, _innerRadius * 2);
                bg.FillPath(brush, path);
            }
        }

        g.DrawImageUnscaled(_ringBitmap, 0, 0);
    }

    private void DrawSatValSquare(Graphics g)
    {
        // Draw a quick approximation: many small columns blending S and V.
        int steps = Math.Max(1, (int)_squareRect.Width);
        float colW = _squareRect.Width / steps;

        for (int ix = 0; ix <= steps; ix++)
        {
            float s = (float)ix / steps;
            Color top = HsvToRgb(_hue, s, 1f);
            Color bot = HsvToRgb(_hue, s, 0f);

            using var brush = new LinearGradientBrush(
                new PointF(_squareRect.Left + ix * colW, _squareRect.Top - 1),
                new PointF(_squareRect.Left + ix * colW, _squareRect.Bottom + 1),
                top, bot);

            g.FillRectangle(brush,
                _squareRect.Left + ix * colW, _squareRect.Top,
                colW + 1f, _squareRect.Height);
        }

        // Border
        using var pen = new Pen(Color.FromArgb(100, 0, 0, 0), 1f);
        g.DrawRectangle(pen, _squareRect.X, _squareRect.Y, _squareRect.Width, _squareRect.Height);
    }

    private void DrawSelectionIndicators(Graphics g)
    {
        // Hue indicator on the ring
        float midRingRadius = (_outerRadius + _innerRadius) / 2f;
        float hueRad = _hue * MathF.PI / 180f;
        float hx = _center.X + MathF.Cos(hueRad) * midRingRadius;
        float hy = _center.Y + MathF.Sin(hueRad) * midRingRadius;
        float indicatorR = (_outerRadius - _innerRadius) / 2f - 1f;

        using (var pen = new Pen(Color.White, 2f))
            g.DrawEllipse(pen, hx - indicatorR, hy - indicatorR, indicatorR * 2, indicatorR * 2);
        using (var pen = new Pen(Color.Black, 1f))
            g.DrawEllipse(pen, hx - indicatorR, hy - indicatorR, indicatorR * 2, indicatorR * 2);

        // Sat/Val indicator on the square
        float sx = _squareRect.Left + _saturation * _squareRect.Width;
        float sy = _squareRect.Top + (1f - _value) * _squareRect.Height;
        const float cr = 5f;

        using (var pen = new Pen(Color.White, 2f))
            g.DrawEllipse(pen, sx - cr, sy - cr, cr * 2, cr * 2);
        using (var pen = new Pen(Color.Black, 1f))
            g.DrawEllipse(pen, sx - cr, sy - cr, cr * 2, cr * 2);
    }

    // ── Mouse interaction ───────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        float dx = e.X - _center.X;
        float dy = e.Y - _center.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist >= _innerRadius && dist <= _outerRadius)
        {
            _draggingRing = true;
            UpdateHueFromMouse(e.X, e.Y);
        }
        else if (_squareRect.Contains(e.X, e.Y))
        {
            _draggingSquare = true;
            UpdateSatValFromMouse(e.X, e.Y);
        }

        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingRing)
            UpdateHueFromMouse(e.X, e.Y);
        else if (_draggingSquare)
            UpdateSatValFromMouse(e.X, e.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _draggingRing = false;
        _draggingSquare = false;
        Capture = false;
    }

    private void UpdateHueFromMouse(int x, int y)
    {
        float angle = MathF.Atan2(y - _center.Y, x - _center.X);
        float deg = angle * 180f / MathF.PI;
        if (deg < 0) deg += 360f;
        _hue = deg;
        _ringBitmap?.Dispose();
        _ringBitmap = null; // force square redraw with new hue
        Invalidate();
        ColorChanged?.Invoke(this, SelectedColor);
    }

    private void UpdateSatValFromMouse(int x, int y)
    {
        _saturation = Math.Clamp((x - _squareRect.Left) / _squareRect.Width, 0f, 1f);
        _value = Math.Clamp(1f - (y - _squareRect.Top) / _squareRect.Height, 0f, 1f);
        Invalidate();
        ColorChanged?.Invoke(this, SelectedColor);
    }

    // ── HSV ↔ RGB helpers ───────────────────────────────────────

    public static Color HsvToRgb(float h, float s, float v)
    {
        h %= 360f;
        if (h < 0) h += 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;

        float r, g, b;
        if (h < 60)       { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else               { r = c; g = 0; b = x; }

        return Color.FromArgb(
            Clamp8((r + m) * 255f),
            Clamp8((g + m) * 255f),
            Clamp8((b + m) * 255f));
    }

    public static void RgbToHsv(Color c, out float h, out float s, out float v)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
            h = 0;
        else if (max == r)
            h = 60f * ((g - b) / delta % 6f);
        else if (max == g)
            h = 60f * ((b - r) / delta + 2f);
        else
            h = 60f * ((r - g) / delta + 4f);

        if (h < 0) h += 360f;
    }

    private static int Clamp8(float f) => Math.Clamp((int)(f + 0.5f), 0, 255);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ringBitmap?.Dispose();
        base.Dispose(disposing);
    }
}
