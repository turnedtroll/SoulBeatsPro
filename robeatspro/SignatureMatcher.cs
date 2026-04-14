namespace SoulBeatsPro;

/// <summary>Pure pixel classification against a color signature. No IO.</summary>
internal static class SignatureMatcher
{
    public static bool Matches(byte r, byte g, byte b, ColorSignature sig)
    {
        foreach (var e in sig.Entries)
        {
            int tol = e.Tolerance;
            if (Math.Abs(r - e.R) <= tol &&
                Math.Abs(g - e.G) <= tol &&
                Math.Abs(b - e.B) <= tol)
                return true;
        }
        return false;
    }

    public static int CountMatches((byte r, byte g, byte b)[] pixels, ColorSignature sig)
    {
        int count = 0;
        foreach (var (r, g, b) in pixels)
            if (Matches(r, g, b, sig)) count++;
        return count;
    }
}

internal static class SignatureCapture
{
    /// <summary>
    /// Given N sampled pixels, produce a ColorSignatureEntry whose (r,g,b) is
    /// the mean and whose tolerance is max(floor, max channel deviation from mean).
    /// </summary>
    public static ColorSignatureEntry BuildEntry(IReadOnlyList<(byte r, byte g, byte b)> samples, int floorTolerance = 8)
    {
        if (samples.Count == 0) return new ColorSignatureEntry(0, 0, 0, floorTolerance);
        int sumR = 0, sumG = 0, sumB = 0;
        foreach (var (r, g, b) in samples) { sumR += r; sumG += g; sumB += b; }
        int meanR = sumR / samples.Count;
        int meanG = sumG / samples.Count;
        int meanB = sumB / samples.Count;
        int maxDev = 0;
        foreach (var (r, g, b) in samples)
        {
            maxDev = Math.Max(maxDev, Math.Abs(r - meanR));
            maxDev = Math.Max(maxDev, Math.Abs(g - meanG));
            maxDev = Math.Max(maxDev, Math.Abs(b - meanB));
        }
        int tolerance = Math.Max(floorTolerance, maxDev);
        return new ColorSignatureEntry(meanR, meanG, meanB, tolerance);
    }
}
