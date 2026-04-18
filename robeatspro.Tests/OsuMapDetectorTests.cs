using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class OsuMapDetectorTests
{
    [Theory]
    [InlineData("osu!  - Thaehan - Insert Coin [Hard]", "Thaehan", "Insert Coin", "Hard")]
    [InlineData("osu!  - SOUND HOLIC - Airman [KK's Hard]", "SOUND HOLIC", "Airman", "KK's Hard")]
    [InlineData("osu!  - Artist With Dash - Title - Subtitle [Diff]", "Artist With Dash", "Title - Subtitle", "Diff")]
    public void parses_window_title_correctly(string title, string expectedArtist, string expectedTitle, string expectedDiff)
    {
        var result = OsuMapDetector.ParseWindowTitle(title);

        Assert.NotNull(result);
        Assert.Equal(expectedArtist, result.Value.artist);
        Assert.Equal(expectedTitle, result.Value.title);
        Assert.Equal(expectedDiff, result.Value.difficulty);
    }

    [Theory]
    [InlineData("osu!")]
    [InlineData("osu! cuttingedge")]
    [InlineData("Notepad")]
    [InlineData("")]
    public void returns_null_for_invalid_window_titles(string title)
    {
        var result = OsuMapDetector.ParseWindowTitle(title);
        Assert.Null(result);
    }

    [Fact]
    public void matches_beatmap_metadata_case_insensitive()
    {
        var beatmap = new OsuBeatmap
        {
            Title = "Insert Coin",
            Artist = "Thaehan",
            Version = "Hard",
            KeyCount = 4,
            Mode = 3
        };

        Assert.True(OsuMapDetector.MatchesBeatmap(beatmap, "thaehan", "insert coin", "hard"));
        Assert.True(OsuMapDetector.MatchesBeatmap(beatmap, "Thaehan", "Insert Coin", "Hard"));
        Assert.False(OsuMapDetector.MatchesBeatmap(beatmap, "Thaehan", "Insert Coin", "Normal"));
    }
}
