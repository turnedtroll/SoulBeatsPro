using System.Drawing.Imaging;

namespace SoulBeatsPro;

/// <summary>
/// Per-pixel classification used by the calibration magnifier.
/// </summary>
internal enum PixelKind : byte
{
    None = 0,
    White = 1,   // Passes white threshold (note in white/gray mode, or note color in color mode)
    Gray  = 2    // Passes gray threshold (hold body in white/gray mode, or hold color in color mode)
}

/// <summary>
/// Patch analysis result. Pixels[] is row-major (2*half+1) wide, length = (2*half+1)^2.
/// Pixels can be null if the caller didn't request per-pixel data.
/// </summary>
internal readonly struct PatchAnalysis
{
    public readonly int WhiteCount;
    public readonly int GrayCount;
    public readonly PixelKind[]? Pixels;
    public readonly int Width;
    public readonly int Height;

    public PatchAnalysis(int whiteCount, int grayCount, PixelKind[]? pixels, int width, int height)
    {
        WhiteCount = whiteCount;
        GrayCount  = grayCount;
        Pixels     = pixels;
        Width      = width;
        Height     = height;
    }
}

/// <summary>
/// Magnifier view: actual screen pixels (BGRA -> Color) plus per-pixel
/// classification for the same region. Width/Height = 2*contextHalf+1.
/// </summary>
internal readonly struct ContextPatch
{
    public readonly System.Drawing.Color[] Colors;
    public readonly PixelKind[] Kinds;
    public readonly int Width;
    public readonly int Height;

    public ContextPatch(System.Drawing.Color[] colors, PixelKind[] kinds, int width, int height)
    {
        Colors = colors;
        Kinds  = kinds;
        Width  = width;
        Height = height;
    }
}

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

    /// <summary>
    /// Count pixels in a (2*half+1) square patch that match any entry in the
    /// given color signature. This is the one-stop detection primitive used by
    /// MacroEngine's press-while-present algorithm.
    /// </summary>
    public unsafe int CountSignatureMatches(int cx, int cy, int half, ColorSignature sig)
    {
        if (sig.Entries.Count == 0) return 0;

        int x0 = Math.Max(0, cx - half);
        int y0 = Math.Max(0, cy - half);
        int x1 = Math.Min(Width - 1, cx + half);
        int y1 = Math.Min(Height - 1, cy + half);

        var data = _bmp.LockBits(
            new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int count = 0;
        try
        {
            int stride = data.Stride;
            byte* ptr = (byte*)data.Scan0;
            int w = x1 - x0 + 1;
            int h = y1 - y0 + 1;

            // Snapshot to locals for the inner loop.
            int entryCount = sig.Entries.Count;
            var entries = sig.Entries;

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b = line[col * 4];
                    byte g = line[col * 4 + 1];
                    byte r = line[col * 4 + 2];

                    for (int e = 0; e < entryCount; e++)
                    {
                        var entry = entries[e];
                        int tol = entry.Tolerance;
                        if (Math.Abs(r - entry.R) <= tol &&
                            Math.Abs(g - entry.G) <= tol &&
                            Math.Abs(b - entry.B) <= tol)
                        {
                            count++;
                            break;
                        }
                    }
                }
            }
        }
        finally { _bmp.UnlockBits(data); }

        return count;
    }

    /// <summary>Read the raw BGR pixel at a single relative coordinate.</summary>
    public unsafe (byte r, byte g, byte b) ReadPixel(int cx, int cy)
    {
        int x = Math.Clamp(cx, 0, Width - 1);
        int y = Math.Clamp(cy, 0, Height - 1);
        var data = _bmp.LockBits(
            new Rectangle(x, y, 1, 1),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* p = (byte*)data.Scan0;
            return (p[2], p[1], p[0]);
        }
        finally { _bmp.UnlockBits(data); }
    }

    /// <summary>
    /// Analyze a (2*half+1) square patch. Honors the active detection mode
    /// (WhiteGray vs Color-based) so callers don't need to branch.
    /// If `includePixels` is true, the returned struct includes per-pixel
    /// classification suitable for rendering the calibration magnifier.
    /// </summary>
    public unsafe PatchAnalysis AnalyzePatch(int cx, int cy, int half, bool includePixels)
    {
        int x0 = Math.Max(0, cx - half);
        int y0 = Math.Max(0, cy - half);
        int x1 = Math.Min(Width - 1, cx + half);
        int y1 = Math.Min(Height - 1, cy + half);
        int patchW = 2 * half + 1;
        int patchH = 2 * half + 1;

        var pixels = includePixels ? new PixelKind[patchW * patchH] : null;
        int whiteCount = 0;
        int grayCount  = 0;

        var data = _bmp.LockBits(
            new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            bool whiteGray = ConfigManager.Instance.IsWhiteGrayMode;
            var det = ConfigManager.Instance.Detection;

            int whiteMin = det.WhiteGray.WhiteMin;
            int grayMin  = det.WhiteGray.GrayMin;
            int grayMax  = det.WhiteGray.GrayMax;
            int nMinR = det.NoteColor.MinR, nMinG = det.NoteColor.MinG, nMaxB = det.NoteColor.MaxB;
            int hMinR = det.HoldColor.MinR, hMaxR = det.HoldColor.MaxR;
            int hMinG = det.HoldColor.MinG, hMaxG = det.HoldColor.MaxG;
            int hMaxB = det.HoldColor.MaxB,  hMinRG = det.HoldColor.MinRG;

            int stride = data.Stride;
            byte* ptr = (byte*)data.Scan0;
            int w = x1 - x0 + 1;
            int h = y1 - y0 + 1;

            int writeOffsetX = x0 - (cx - half);
            int writeOffsetY = y0 - (cy - half);

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b = line[col * 4];
                    byte g = line[col * 4 + 1];
                    byte r = line[col * 4 + 2];

                    PixelKind kind = PixelKind.None;
                    if (whiteGray)
                    {
                        if (r >= whiteMin && g >= whiteMin && b >= whiteMin)
                            kind = PixelKind.White;
                        else if (r >= grayMin && r <= grayMax &&
                                 g >= grayMin && g <= grayMax &&
                                 b >= grayMin && b <= grayMax)
                            kind = PixelKind.Gray;
                    }
                    else
                    {
                        if (r >= nMinR && g >= nMinG && b < nMaxB)
                            kind = PixelKind.White;
                        else if (r >= hMinR && r <= hMaxR &&
                                 g >= hMinG && g <= hMaxG &&
                                 b < hMaxB && (r + g) > hMinRG)
                            kind = PixelKind.Gray;
                    }

                    if (kind == PixelKind.White) whiteCount++;
                    else if (kind == PixelKind.Gray) grayCount++;

                    if (pixels != null)
                    {
                        int outX = writeOffsetX + col;
                        int outY = writeOffsetY + row;
                        pixels[outY * patchW + outX] = kind;
                    }
                }
            }
        }
        finally
        {
            _bmp.UnlockBits(data);
        }

        return new PatchAnalysis(whiteCount, grayCount, pixels, patchW, patchH);
    }

    /// <summary>
    /// Read a (2*contextHalf+1) square of raw screen pixels plus their classification,
    /// for the calibration magnifier. Pixels outside capture bounds are filled black / None.
    /// </summary>
    public unsafe ContextPatch GetContextPatch(int cx, int cy, int contextHalf)
    {
        int patchW = 2 * contextHalf + 1;
        int patchH = 2 * contextHalf + 1;
        var colors = new System.Drawing.Color[patchW * patchH];
        var kinds  = new PixelKind[patchW * patchH];

        int x0 = Math.Max(0, cx - contextHalf);
        int y0 = Math.Max(0, cy - contextHalf);
        int x1 = Math.Min(Width - 1, cx + contextHalf);
        int y1 = Math.Min(Height - 1, cy + contextHalf);
        if (x1 < x0 || y1 < y0) return new ContextPatch(colors, kinds, patchW, patchH);

        var data = _bmp.LockBits(
            new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            bool whiteGray = ConfigManager.Instance.IsWhiteGrayMode;
            var det = ConfigManager.Instance.Detection;
            int whiteMin = det.WhiteGray.WhiteMin;
            int grayMin  = det.WhiteGray.GrayMin;
            int grayMax  = det.WhiteGray.GrayMax;
            int nMinR = det.NoteColor.MinR, nMinG = det.NoteColor.MinG, nMaxB = det.NoteColor.MaxB;
            int hMinR = det.HoldColor.MinR, hMaxR = det.HoldColor.MaxR;
            int hMinG = det.HoldColor.MinG, hMaxG = det.HoldColor.MaxG;
            int hMaxB = det.HoldColor.MaxB,  hMinRG = det.HoldColor.MinRG;

            int stride = data.Stride;
            byte* ptr = (byte*)data.Scan0;
            int w = x1 - x0 + 1;
            int h = y1 - y0 + 1;

            int writeOffsetX = x0 - (cx - contextHalf);
            int writeOffsetY = y0 - (cy - contextHalf);

            for (int row = 0; row < h; row++)
            {
                byte* line = ptr + row * stride;
                for (int col = 0; col < w; col++)
                {
                    byte b = line[col * 4];
                    byte gChan = line[col * 4 + 1];
                    byte r = line[col * 4 + 2];

                    PixelKind kind = PixelKind.None;
                    if (whiteGray)
                    {
                        if (r >= whiteMin && gChan >= whiteMin && b >= whiteMin)
                            kind = PixelKind.White;
                        else if (r >= grayMin && r <= grayMax &&
                                 gChan >= grayMin && gChan <= grayMax &&
                                 b >= grayMin && b <= grayMax)
                            kind = PixelKind.Gray;
                    }
                    else
                    {
                        if (r >= nMinR && gChan >= nMinG && b < nMaxB)
                            kind = PixelKind.White;
                        else if (r >= hMinR && r <= hMaxR &&
                                 gChan >= hMinG && gChan <= hMaxG &&
                                 b < hMaxB && (r + gChan) > hMinRG)
                            kind = PixelKind.Gray;
                    }

                    int outIdx = (writeOffsetY + row) * patchW + (writeOffsetX + col);
                    colors[outIdx] = System.Drawing.Color.FromArgb(255, r, gChan, b);
                    kinds[outIdx] = kind;
                }
            }
        }
        finally
        {
            _bmp.UnlockBits(data);
        }

        return new ContextPatch(colors, kinds, patchW, patchH);
    }

    public void Dispose()
    {
        _gfx.Dispose();
        _bmp.Dispose();
    }
}
