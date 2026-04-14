using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class AccuracyPresetTests
{
    [Fact]
    public void max_accuracy_is_always_zero_delay()
    {
        Assert.Equal(0.0, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.PerfectOnly, 140));
        Assert.Equal(0.0, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.PerfectOnly, 100));
    }

    [Fact]
    public void high_accuracy_is_twenty_percent_of_max_judgment()
    {
        // 140ms window -> 28ms max delay -> 0.028s
        Assert.Equal(0.028, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.MostlyPerfects, 140), 3);
    }

    [Fact]
    public void human_like_is_fifty_percent()
    {
        Assert.Equal(0.07, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.HumanLike, 140), 3);
    }

    [Fact]
    public void sloppy_is_eighty_five_percent()
    {
        Assert.Equal(0.119, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, 140), 3);
    }

    [Fact]
    public void negative_or_zero_max_judgment_returns_zero()
    {
        Assert.Equal(0.0, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, 0));
        Assert.Equal(0.0, AccuracyPresetTable.GetMaxDelaySeconds(AccuracyPreset.Sloppy, -5));
    }
}
