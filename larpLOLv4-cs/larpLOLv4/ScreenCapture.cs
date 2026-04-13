using System.Drawing.Imaging;

namespace larpLOLv4;

/// Fast screen capture and pixel sampling using GDI+.
internal sealed class ScreenCapture : IDisposable
{
    private Bitmap _bmp;
    private Graphics _gfx;

    public int Left { get; private set; }
    public int Top { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public ScreenCapture(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        _bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _gfx = Graphics.FromImage(_bmp);
    }

    /// Resize the capture region (e.g. for full-monitor grabs)
    public void Resize(int left, int top, int width, int height)
    {
        if (Left == left && Top == top && Width == width && Height == height)
            return;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        _gfx.Dispose();
        _bmp.Dispose();
        _bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _gfx = Graphics.FromImage(_bmp);
    }

    /// Grab the screen region.
    public void Grab()
    {
        _gfx.CopyFromScreen(Left, Top, 0, 0, new Size(Width, Height), CopyPixelOperation.SourceCopy);
    }

    /// Get the raw bitmap for drawing (calibration/debug overlay).
    public Bitmap Bitmap => _bmp;

    /// Count pixels in a (2*half+1) square patch that satisfy a predicate.
    /// Uses LockBits for speed.
    public unsafe void AnalyzePatch(
        int cx, int cy, int half,
        out int whiteCount, out int grayCount)
    {
        whiteCount = 0;
        grayCount = 0;

        int x0 = Math.Max(0, cx - half);
        int y0 = Math.Max(0, cy - half);
        int x1 = Math.Min(Width - 1, cx + half);
        int y1 = Math.Min(Height - 1, cy + half);

        var data = _bmp.LockBits(
            new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte* ptr = (byte*)data.Scan0;
            int w = x1 - x0 + 1;
            int h = y1 - y0 + 1;

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b = line[col * 4];
                    byte g = line[col * 4 + 1];
                    byte r = line[col * 4 + 2];

                    if (r >= 240 && g >= 240 && b >= 240)
                        whiteCount++;
                    else if (r >= 130 && r <= 170 && g >= 130 && g <= 170 && b >= 130 && b <= 170)
                        grayCount++;
                }
            }
        }
        finally
        {
            _bmp.UnlockBits(data);
        }
    }

    /// Check if patch has any white pixels.
    public unsafe bool PatchHasWhite(int cx, int cy, int half)
    {
        int x0 = Math.Max(0, cx - half);
        int y0 = Math.Max(0, cy - half);
        int x1 = Math.Min(Width - 1, cx + half);
        int y1 = Math.Min(Height - 1, cy + half);

        var data = _bmp.LockBits(
            new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte* ptr = (byte*)data.Scan0;
            int w = x1 - x0 + 1;
            int h = y1 - y0 + 1;

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b2 = line[col * 4];
                    byte g2 = line[col * 4 + 1];
                    byte r2 = line[col * 4 + 2];
                    if (r2 >= 240 && g2 >= 240 && b2 >= 240) return true;
                }
            }
            return false;
        }
        finally
        {
            _bmp.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        _gfx.Dispose();
        _bmp.Dispose();
    }
}
