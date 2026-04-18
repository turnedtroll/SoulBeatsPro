using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class OsuBeatmapParserTests
{
    private const string ValidMania4K = @"osu file format v14

[General]
AudioFilename: audio.mp3
Mode: 3

[Metadata]
Title:Test Song
Artist:Test Artist
Version:Hard

[Difficulty]
CircleSize:4

[HitObjects]
64,192,1000,1,0,0:0:0:0:
192,192,1200,1,0,0:0:0:0:
320,192,1400,1,0,0:0:0:0:
448,192,1600,1,0,0:0:0:0:
";

    private const string ManiaWithHolds = @"osu file format v14

[General]
AudioFilename: audio.mp3
Mode: 3

[Metadata]
Title:Hold Test
Artist:Test Artist
Version:Normal

[Difficulty]
CircleSize:4

[HitObjects]
64,192,1000,128,0,2000:0:0:0:0:
192,192,1500,1,0,0:0:0:0:
320,192,1800,128,0,2500:0:0:0:0:
";

    private const string Mania7K = @"osu file format v14

[General]
AudioFilename: audio.mp3
Mode: 3

[Metadata]
Title:Seven Keys
Artist:Test
Version:Insane

[Difficulty]
CircleSize:7

[HitObjects]
36,192,1000,1,0,0:0:0:0:
109,192,1000,1,0,0:0:0:0:
182,192,1000,1,0,0:0:0:0:
256,192,1000,1,0,0:0:0:0:
329,192,1000,1,0,0:0:0:0:
402,192,1000,1,0,0:0:0:0:
475,192,1000,1,0,0:0:0:0:
";

    private const string NotMania = @"osu file format v14

[General]
AudioFilename: audio.mp3
Mode: 0

[Metadata]
Title:Std Map
Artist:Test
Version:Easy

[Difficulty]
CircleSize:4

[HitObjects]
256,192,1000,1,0,0:0:0:0:
";

    [Fact]
    public void parses_4k_tap_notes_with_correct_columns()
    {
        var beatmap = OsuBeatmapParser.Parse(ValidMania4K);
        Assert.NotNull(beatmap);
        Assert.Equal("Test Song", beatmap.Title);
        Assert.Equal("Test Artist", beatmap.Artist);
        Assert.Equal("Hard", beatmap.Version);
        Assert.Equal(4, beatmap.KeyCount);
        Assert.Equal(3, beatmap.Mode);
        Assert.Equal(4, beatmap.Notes.Count);
        Assert.Equal(0, beatmap.Notes[0].Column);
        Assert.Equal(1000, beatmap.Notes[0].TimeMs);
        Assert.False(beatmap.Notes[0].IsHold);
        Assert.Equal(1, beatmap.Notes[1].Column);
        Assert.Equal(2, beatmap.Notes[2].Column);
        Assert.Equal(3, beatmap.Notes[3].Column);
    }

    [Fact]
    public void parses_hold_notes_with_end_time()
    {
        var beatmap = OsuBeatmapParser.Parse(ManiaWithHolds);
        Assert.NotNull(beatmap);
        Assert.Equal(3, beatmap.Notes.Count);
        Assert.True(beatmap.Notes[0].IsHold);
        Assert.Equal(0, beatmap.Notes[0].Column);
        Assert.Equal(1000, beatmap.Notes[0].TimeMs);
        Assert.Equal(2000, beatmap.Notes[0].EndTimeMs);
        Assert.False(beatmap.Notes[1].IsHold);
        Assert.Equal(1, beatmap.Notes[1].Column);
        Assert.Equal(1500, beatmap.Notes[1].TimeMs);
        Assert.Equal(0, beatmap.Notes[1].EndTimeMs);
        Assert.True(beatmap.Notes[2].IsHold);
        Assert.Equal(2, beatmap.Notes[2].Column);
        Assert.Equal(1800, beatmap.Notes[2].TimeMs);
        Assert.Equal(2500, beatmap.Notes[2].EndTimeMs);
    }

    [Fact]
    public void parses_7k_columns_correctly()
    {
        var beatmap = OsuBeatmapParser.Parse(Mania7K);
        Assert.NotNull(beatmap);
        Assert.Equal(7, beatmap.KeyCount);
        Assert.Equal(7, beatmap.Notes.Count);
        for (int i = 0; i < 7; i++)
            Assert.Equal(i, beatmap.Notes[i].Column);
    }

    [Fact]
    public void returns_null_for_non_mania_mode()
    {
        var beatmap = OsuBeatmapParser.Parse(NotMania);
        Assert.Null(beatmap);
    }

    [Fact]
    public void notes_are_sorted_by_time()
    {
        var beatmap = OsuBeatmapParser.Parse(ManiaWithHolds)!;
        for (int i = 1; i < beatmap.Notes.Count; i++)
            Assert.True(beatmap.Notes[i].TimeMs >= beatmap.Notes[i - 1].TimeMs);
    }
}
