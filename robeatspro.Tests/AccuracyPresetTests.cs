using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class AccuracyPresetTests
{
    [Fact]
    public void max_accuracy_has_zero_tuning()
    {
        var t = AccuracyPresetTable.GetTuning(AccuracyPreset.PerfectOnly);
        Assert.Equal(0.0, t.MeanMs);
        Assert.Equal(0.0, t.StdDevMs);
        Assert.Equal(0.0, t.ClampMaxMs);
    }

    [Fact]
    public void sloppy_has_non_zero_tuning()
    {
        var t = AccuracyPresetTable.GetTuning(AccuracyPreset.Sloppy);
        Assert.True(t.MeanMs > 0);
        Assert.True(t.StdDevMs > 0);
        Assert.True(t.ClampMaxMs > 0);
    }

    [Fact]
    public void get_max_delay_honors_profile_judgment_window()
    {
        // Sloppy clamp is 95ms; with a tighter 80ms profile window it should cap there.
        double d = AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, 80);
        Assert.Equal(0.080, d, 3);
    }

    [Fact]
    public void get_max_delay_uses_preset_clamp_when_profile_is_looser()
    {
        // Funky-Friday-ish 140ms profile; Sloppy's 95ms clamp should win.
        double d = AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, 140);
        Assert.Equal(0.095, d, 3);
    }

    [Fact]
    public void negative_or_zero_max_judgment_returns_zero()
    {
        Assert.Equal(0.0, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, 0));
        Assert.Equal(0.0, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, -5));
        Assert.Equal(0.0, AccuracyPresetTable.SampleDelaySeconds(AccuracyPreset.Sloppy, 0, new Random(1)));
    }

    [Fact]
    public void sample_delay_stays_in_clamp_range()
    {
        // Run many samples; all should fall within [0, clamp].
        var rng = new Random(123);
        double maxJudgment = 140;
        double cap = AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.HumanLike, maxJudgment);
        for (int i = 0; i < 5000; i++)
        {
            double d = AccuracyPresetTable.SampleDelaySeconds(AccuracyPreset.HumanLike, maxJudgment, rng);
            Assert.InRange(d, 0.0, cap);
        }
    }

    [Fact]
    public void sample_delay_mean_approximates_preset_mean()
    {
        // Empirical mean over many samples should be within a few ms of the preset mean.
        var rng = new Random(7);
        double maxJudgment = 200; // loose so clamp doesn't bias the mean much
        double total = 0;
        const int n = 20_000;
        for (int i = 0; i < n; i++)
            total += AccuracyPresetTable.SampleDelaySeconds(AccuracyPreset.HumanLike, maxJudgment, rng) * 1000.0;
        double empiricalMean = total / n;
        // HumanLike mean is 25ms; allow 3ms tolerance (truncation at 0 biases up slightly).
        Assert.InRange(empiricalMean, 22, 33);
    }

    [Fact]
    public void perfect_only_always_samples_zero()
    {
        var rng = new Random(1);
        for (int i = 0; i < 100; i++)
            Assert.Equal(0.0, AccuracyPresetTable.SampleDelaySeconds(AccuracyPreset.PerfectOnly, 140, rng));
    }

    [Fact]
    public void bidirectional_perfect_only_always_zero()
    {
        var rng = new Random(1);
        for (int i = 0; i < 100; i++)
            Assert.Equal(0.0, AccuracyPresetTable.SampleJitterMsBidirectional(AccuracyPreset.PerfectOnly, 140, rng));
    }

    [Fact]
    public void bidirectional_stays_inside_marvelous_window()
    {
        // All presets must keep jitter inside ±16ms so hits never drop below Marvelous (300g).
        var rng = new Random(42);
        foreach (var preset in new[] { AccuracyPreset.MostlyPerfects, AccuracyPreset.HumanLike, AccuracyPreset.Sloppy })
        {
            for (int i = 0; i < 5000; i++)
            {
                double ms = AccuracyPresetTable.SampleJitterMsBidirectional(preset, 140, rng);
                Assert.InRange(ms, -16.0, 16.0);
            }
        }
    }

    [Fact]
    public void bidirectional_produces_both_early_and_late_hits()
    {
        // Humanlike should scatter around zero — not only-late like the unidirectional sampler.
        var rng = new Random(9);
        int early = 0, late = 0;
        for (int i = 0; i < 5000; i++)
        {
            double ms = AccuracyPresetTable.SampleJitterMsBidirectional(AccuracyPreset.HumanLike, 140, rng);
            if (ms < -0.5) early++;
            else if (ms > 0.5) late++;
        }
        // Expect roughly balanced distribution (Gaussian mean 0) — at least 30% each side.
        Assert.True(early > 1500, $"Expected more early hits, got {early}");
        Assert.True(late  > 1500, $"Expected more late hits, got {late}");
    }

    [Fact]
    public void bidirectional_mean_approximates_zero()
    {
        var rng = new Random(7);
        double total = 0;
        const int n = 20_000;
        for (int i = 0; i < n; i++)
            total += AccuracyPresetTable.SampleJitterMsBidirectional(AccuracyPreset.HumanLike, 140, rng);
        double empiricalMean = total / n;
        Assert.InRange(empiricalMean, -1.0, 1.0);
    }
}
