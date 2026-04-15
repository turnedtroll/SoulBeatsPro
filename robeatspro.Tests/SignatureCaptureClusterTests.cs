using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class SignatureCaptureClusterTests
{
    private static (byte r, byte g, byte b)[] Repeat((byte r, byte g, byte b) s, int n)
    {
        var arr = new (byte, byte, byte)[n];
        for (int i = 0; i < n; i++) arr[i] = s;
        return arr;
    }

    [Fact]
    public void Cluster_RejectsAllBackground()
    {
        // 900 near-identical background samples — no cluster far from median.
        var samples = new List<(byte r, byte g, byte b)>();
        var rng = new Random(42);
        for (int i = 0; i < 900; i++)
        {
            byte v = (byte)(40 + rng.Next(-3, 4));
            samples.Add((v, v, v));
        }

        var result = SignatureCapture.BuildEntry(samples);

        Assert.False(result.Ok);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Cluster_FindsNoteAgainstBackground()
    {
        // 850 dark gray background + 50 white note samples.
        var samples = new List<(byte r, byte g, byte b)>();
        for (int i = 0; i < 850; i++) samples.Add(((byte)40, (byte)40, (byte)45));
        for (int i = 0; i < 50; i++) samples.Add(((byte)253, (byte)254, (byte)255));

        var result = SignatureCapture.BuildEntry(samples);

        Assert.True(result.Ok);
        Assert.NotNull(result.Entry);
        Assert.InRange(result.Entry!.R, 245, 255);
        Assert.InRange(result.Entry.G, 245, 255);
        Assert.InRange(result.Entry.B, 245, 255);
        Assert.True(result.Entry.Tolerance <= 30);
        Assert.True(result.Entry.Learned);
    }

    [Fact]
    public void Cluster_TightToleranceFromStdDev()
    {
        // Background + tight cluster (low stddev) → tolerance close to floor.
        var samples = new List<(byte r, byte g, byte b)>();
        for (int i = 0; i < 800; i++) samples.Add(((byte)30, (byte)30, (byte)30));
        var rng = new Random(7);
        for (int i = 0; i < 50; i++)
        {
            byte v = (byte)(200 + rng.Next(-2, 3));
            samples.Add((v, v, v));
        }

        var result = SignatureCapture.BuildEntry(samples);

        Assert.True(result.Ok);
        Assert.NotNull(result.Entry);
        Assert.True(result.Entry!.Tolerance <= 14, $"tolerance was {result.Entry.Tolerance}");
    }

    [Fact]
    public void Cluster_IgnoresSparseOutliers()
    {
        // 800 background + 30 cohesive note samples + 5 random outliers.
        var samples = new List<(byte r, byte g, byte b)>();
        for (int i = 0; i < 800; i++) samples.Add(((byte)50, (byte)50, (byte)55));
        for (int i = 0; i < 30; i++) samples.Add(((byte)220, (byte)100, (byte)100));
        // sparse far-flung outliers — should not be picked as the seed cluster
        samples.Add(((byte)0, (byte)255, (byte)0));
        samples.Add(((byte)255, (byte)0, (byte)255));
        samples.Add(((byte)10, (byte)200, (byte)10));
        samples.Add(((byte)200, (byte)20, (byte)200));
        samples.Add(((byte)0, (byte)0, (byte)200));

        var result = SignatureCapture.BuildEntry(samples);

        Assert.True(result.Ok);
        Assert.NotNull(result.Entry);
        Assert.InRange(result.Entry!.R, 200, 240);
        Assert.InRange(result.Entry.G, 80, 120);
        Assert.InRange(result.Entry.B, 80, 120);
    }

    [Fact]
    public void Cluster_MinSampleCountEnforced()
    {
        // 800 background + only 15 note samples (below minNoteSamples=20).
        var samples = new List<(byte r, byte g, byte b)>();
        for (int i = 0; i < 800; i++) samples.Add(((byte)40, (byte)40, (byte)40));
        for (int i = 0; i < 15; i++) samples.Add(((byte)255, (byte)255, (byte)255));

        var result = SignatureCapture.BuildEntry(samples);

        Assert.False(result.Ok);
        Assert.NotNull(result.FailureReason);
    }
}
