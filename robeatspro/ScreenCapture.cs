using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// Fast screen capture and pixel sampling using GDI+.
/// Supports two detection modes:
///   - WhiteGray (Funky Friday / larpLOLv4): all channels high = note, all channels mid = hold
///   - ColorBased (RoBeats): configurable R/G thresholds with B constraint
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
        Left = left; Top = top; Width = width; Height = height;
        _bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _gfx = Graphics.FromImage(_bmp);
    }

    public void Resize(int left, int top, int width, int height)
    {
        if (Left == left && Top == top && Width == width && Height == height) return;
        Left = left; Top = top; Width = width; Height = height;
        _gfx.Dispose(); _bmp.Dispose();
        _bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _gfx = Graphics.FromImage(_bmp);
    }

    public void Grab()
    {
        _gfx.CopyFromScreen(Left, Top, 0, 0, new Size(Width, Height), CopyPixelOperation.SourceCopy);
    }

    public Bitmap Bitmap => _bmp;

    // ── White/Gray detection (larpLOLv4 / Funky Friday) ─────────

    /// Count white and gray pixels in a (2*half+1) square patch.
    /// White = all channels >= whiteMin (note arriving)
    /// Gray = all channels in [grayMin, grayMax] (hold body)
    public unsafe void AnalyzePatchWhiteGray(
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
            var wg = ConfigManager.Instance.Detection.WhiteGray;
            int whiteMin = wg.WhiteMin;
            int grayMin = wg.GrayMin;
            int grayMax = wg.GrayMax;

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

                    if (r >= whiteMin && g >= whiteMin && b >= whiteMin)
                        whiteCount++;
                    else if (r >= grayMin && r <= grayMax &&
                             g >= grayMin && g <= grayMax &&
                             b >= grayMin && b <= grayMax)
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
            var wg = ConfigManager.Instance.Detection.WhiteGray;
            int whiteMin = wg.WhiteMin;

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
                    if (r >= whiteMin && g >= whiteMin && b >= whiteMin) return true;
                }
            }
            return false;
        }
        finally
        {
            _bmp.UnlockBits(data);
        }
    }

    // ── Color-based detection (RoBeats) ─────────────────────────

    /// Count note-color and hold-color pixels in a patch.
    /// Note = bright color matching configurable R/G/B thresholds
    /// Hold = darker color matching configurable range
    public unsafe void AnalyzePatchColor(
        int cx, int cy, int half,
        out int noteCount, out int holdCount)
    {
        noteCount = 0;
        holdCount = 0;

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

            var nc = ConfigManager.Instance.Detection.NoteColor;
            var hc = ConfigManager.Instance.Detection.HoldColor;

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b = line[col * 4];
                    byte g = line[col * 4 + 1];
                    byte r = line[col * 4 + 2];

                    if (r >= nc.MinR && g >= nc.MinG && b < nc.MaxB)
                        noteCount++;
                    else if (r >= hc.MinR && r <= hc.MaxR && g >= hc.MinG && g <= hc.MaxG
                             && b < hc.MaxB && (r + g) > hc.MinRG)
                        holdCount++;
                }
            }
        }
        finally
        {
            _bmp.UnlockBits(data);
        }
    }

    /// Check if patch has any note-color pixels (color-based mode).
    public unsafe bool PatchHasNoteColor(int cx, int cy, int half)
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

            var nc = ConfigManager.Instance.Detection.NoteColor;

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b = line[col * 4];
                    byte g = line[col * 4 + 1];
                    byte r = line[col * 4 + 2];
                    if (r >= nc.MinR && g >= nc.MinG && b < nc.MaxB) return true;
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
