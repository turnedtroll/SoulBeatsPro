using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class OsuManiaTimingWindowsTests
{
    [Fact]
    public void marvelous_window_is_constant_regardless_of_od()
    {
        Assert.Equal(16.5, OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Marvelous, 0));
        Assert.Equal(16.5, OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Marvelous, 5));
        Assert.Equal(16.5, OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Marvelous, 10));
    }

    // Judgment passed as int so this public test method doesn't expose the internal enum.
    [Theory]
    [InlineData((int)OsuManiaJudgment.Perfect, 0.0, 64.0)]
    [InlineData((int)OsuManiaJudgment.Perfect, 5.0, 49.0)]
    [InlineData((int)OsuManiaJudgment.Perfect, 8.0, 40.0)]
    [InlineData((int)OsuManiaJudgment.Perfect, 10.0, 34.0)]
    [InlineData((int)OsuManiaJudgment.Great,   8.0, 73.0)]
    [InlineData((int)OsuManiaJudgment.Good,    8.0, 103.0)]
    [InlineData((int)OsuManiaJudgment.OK,      8.0, 127.0)]
    [InlineData((int)OsuManiaJudgment.Miss,    8.0, 164.0)]
    public void windows_match_osu_wiki_formulas(int judgment, double od, double expectedMs)
    {
        Assert.Equal(expectedMs, OsuManiaTimingWindows.GetWindowMs((OsuManiaJudgment)judgment, od), 3);
    }

    [Fact]
    public void od_is_clamped_to_valid_range()
    {
        // Negative OD clamps to 0; >10 clamps to 10.
        Assert.Equal(64.0, OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Perfect, -5));
        Assert.Equal(34.0, OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Perfect, 20));
    }

    [Fact]
    public void windows_narrow_monotonically_as_od_increases()
    {
        double prev = double.MaxValue;
        for (double od = 0; od <= 10; od += 0.5)
        {
            double w = OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Perfect, od);
            Assert.True(w <= prev, $"Perfect window grew at OD {od}");
            prev = w;
        }
    }
}
