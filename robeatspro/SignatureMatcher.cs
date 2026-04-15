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

/// <summary>
/// Result of a learning/quick-calibrate capture session for a single lane.
/// Either Success (carrying a learned entry) or Failed (with a reason for the UI).
/// </summary>
internal readonly struct CaptureResult
{
    public bool Ok { get; }
    public ColorSignatureEntry? Entry { get; }
    public string? FailureReason { get; }

    private CaptureResult(bool ok, ColorSignatureEntry? entry, string? reason)
    {
        Ok = ok; Entry = entry; FailureReason = reason;
    }

    public static CaptureResult Success(ColorSignatureEntry entry) => new(true, entry, null);
    public static CaptureResult Failed(string reason) => new(false, null, reason);
}

internal static class SignatureCapture
{
    /// <summary>
    /// Cluster-based capture. Finds the note cluster amongst a stream of
    /// (mostly-background) samples and produces a tight ColorSignatureEntry.
    ///
    /// Algorithm:
    /// 1. Channel-wise median = background centroid.
    /// 2. Discard samples within bgDelta (Chebyshev) of the median.
    /// 3. Try remaining candidates as cluster seeds, farthest-first. For each
    ///    seed, grow a cluster of all candidates within noteDelta, then refine
    ///    once (recompute mean, regrow). Accept the first cluster ≥ minNoteSamples.
    /// 4. Build entry with tolerance = max(floorTolerance, 2 × stddev), Learned = true.
    /// </summary>
    public static CaptureResult BuildEntry(
        IReadOnlyList<(byte r, byte g, byte b)> samples,
        int floorTolerance = 8,
        int bgDelta = 12,
        int noteDelta = 18,
        int minNoteSamples = 20)
    {
        if (samples.Count == 0) return CaptureResult.Failed("no samples");

        // 1. Channel-wise medians.
        var rs = new int[samples.Count];
        var gs = new int[samples.Count];
        var bs = new int[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            rs[i] = samples[i].r; gs[i] = samples[i].g; bs[i] = samples[i].b;
        }
        Array.Sort(rs); Array.Sort(gs); Array.Sort(bs);
        int medR = rs[rs.Length / 2];
        int medG = gs[gs.Length / 2];
        int medB = bs[bs.Length / 2];

        // 2. Filter out background.
        var candidates = new List<((byte r, byte g, byte b) px, int dist)>(samples.Count);
        foreach (var s in samples)
        {
            int d = Cheb(s.r, s.g, s.b, medR, medG, medB);
            if (d > bgDelta) candidates.Add((s, d));
        }
        if (candidates.Count < minNoteSamples)
            return CaptureResult.Failed(candidates.Count == 0
                ? "all samples were background"
                : "insufficient note samples");

        // 3. Try seeds farthest-first; first valid cluster wins.
        candidates.Sort((a, b) => b.dist.CompareTo(a.dist));

        foreach (var seed in candidates)
        {
            var cluster = GrowCluster(seed.px.r, seed.px.g, seed.px.b, candidates, noteDelta);
            if (cluster.Count == 0) continue;

            var (mr, mg, mb) = MeanOf(cluster);
            cluster = GrowCluster(mr, mg, mb, candidates, noteDelta);

            if (cluster.Count >= minNoteSamples)
            {
                var (r, g, b) = MeanOf(cluster);
                double sd = StdDevChebyshev(cluster, r, g, b);
                int tol = Math.Max(floorTolerance, (int)Math.Round(2 * sd));
                return CaptureResult.Success(new ColorSignatureEntry(r, g, b, tol, learned: true));
            }
        }

        return CaptureResult.Failed("insufficient note samples");
    }

    private static int Cheb(int r, int g, int b, int cr, int cg, int cb)
        => Math.Max(Math.Abs(r - cr), Math.Max(Math.Abs(g - cg), Math.Abs(b - cb)));

    private static List<(byte r, byte g, byte b)> GrowCluster(
        int cr, int cg, int cb,
        List<((byte r, byte g, byte b) px, int dist)> candidates,
        int delta)
    {
        var cluster = new List<(byte r, byte g, byte b)>();
        foreach (var c in candidates)
            if (Cheb(c.px.r, c.px.g, c.px.b, cr, cg, cb) <= delta)
                cluster.Add(c.px);
        return cluster;
    }

    private static (int r, int g, int b) MeanOf(List<(byte r, byte g, byte b)> cluster)
    {
        long sumR = 0, sumG = 0, sumB = 0;
        foreach (var (r, g, b) in cluster) { sumR += r; sumG += g; sumB += b; }
        int n = cluster.Count;
        return ((int)(sumR / n), (int)(sumG / n), (int)(sumB / n));
    }

    private static double StdDevChebyshev(List<(byte r, byte g, byte b)> cluster, int mr, int mg, int mb)
    {
        // Single scalar stddev computed over per-sample Chebyshev distance from mean.
        long sumSq = 0;
        foreach (var (r, g, b) in cluster)
        {
            int d = Cheb(r, g, b, mr, mg, mb);
            sumSq += (long)d * d;
        }
        return Math.Sqrt((double)sumSq / cluster.Count);
    }
}
