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
Mode: 1

[Metadata]
Title:Taiko Map
Artist:Test
Version:Easy

[Difficulty]
CircleSize:4

[HitObjects]
256,192,1000,1,0,0:0:0:0:
";

    private const string StandardForConvert = @"osu file format v14

[General]
AudioFilename: audio.mp3
Mode: 0

[Metadata]
Title:Std Map
Artist:Test
Version:Easy

[Difficulty]
CircleSize:4
OverallDifficulty:6

[HitObjects]
64,192,1000,1,0,0:0:0:0:
192,192,1200,1,0,0:0:0:0:
320,192,1400,2,0,B|256:192|128:192,1,100
448,192,1600,12,0,3000,0:0:0:0:
256,192,1800,1,0,0:0:0:0:
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
    public void returns_null_for_taiko_mode()
    {
        var beatmap = OsuBeatmapParser.Parse(NotMania);
        Assert.Null(beatmap);
    }

    [Fact]
    public void converts_standard_mode_to_mania_with_configured_key_count()
    {
        var beatmap = OsuBeatmapParser.Parse(StandardForConvert, convertKeyCount: 4);
        Assert.NotNull(beatmap);
        Assert.True(beatmap!.IsConvert);
        Assert.Equal(4, beatmap.KeyCount);
        // 3 circles + 1 slider = 4 notes; spinner (type & 8) is skipped.
        Assert.Equal(4, beatmap.Notes.Count);
        // All convert notes are taps for now (slider hold duration not yet supported).
        foreach (var n in beatmap.Notes)
            Assert.False(n.IsHold);
        // Column = x * 4 / 512 → x=64→0, x=192→1, x=320→2, x=256→2
        Assert.Equal(0, beatmap.Notes[0].Column);
        Assert.Equal(1, beatmap.Notes[1].Column);
        Assert.Equal(2, beatmap.Notes[2].Column);
        Assert.Equal(2, beatmap.Notes[3].Column);
    }

    [Fact]
    public void convert_respects_custom_key_count()
    {
        var beatmap = OsuBeatmapParser.Parse(StandardForConvert, convertKeyCount: 7);
        Assert.NotNull(beatmap);
        Assert.Equal(7, beatmap!.KeyCount);
        // x=64 → 64*7/512 = 0, x=192 → 2, x=320 → 4, x=256 → 3
        Assert.Equal(0, beatmap.Notes[0].Column);
        Assert.Equal(2, beatmap.Notes[1].Column);
        Assert.Equal(4, beatmap.Notes[2].Column);
        Assert.Equal(3, beatmap.Notes[3].Column);
    }

    [Fact]
    public void mania_maps_are_not_marked_as_convert()
    {
        var beatmap = OsuBeatmapParser.Parse(ValidMania4K);
        Assert.NotNull(beatmap);
        Assert.False(beatmap!.IsConvert);
    }

    [Fact]
    public void notes_are_sorted_by_time()
    {
        var beatmap = OsuBeatmapParser.Parse(ManiaWithHolds)!;
        for (int i = 1; i < beatmap.Notes.Count; i++)
            Assert.True(beatmap.Notes[i].TimeMs >= beatmap.Notes[i - 1].TimeMs);
    }

    [Fact]
    public void parses_overall_difficulty()
    {
        const string mapWithOd = @"osu file format v14

[General]
Mode: 3

[Metadata]
Title:OD Test
Artist:Test
Version:Insane

[Difficulty]
CircleSize:4
OverallDifficulty:8.5

[HitObjects]
64,192,1000,1,0,0:0:0:0:
";
        var beatmap = OsuBeatmapParser.Parse(mapWithOd);
        Assert.NotNull(beatmap);
        Assert.Equal(8.5, beatmap!.OverallDifficulty, 3);
    }

    [Fact]
    public void missing_overall_difficulty_falls_back_to_default()
    {
        var beatmap = OsuBeatmapParser.Parse(ValidMania4K);
        Assert.NotNull(beatmap);
        Assert.Equal(OsuManiaTimingWindows.DefaultOd, beatmap!.OverallDifficulty);
    }
}
