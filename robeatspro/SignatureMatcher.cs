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
