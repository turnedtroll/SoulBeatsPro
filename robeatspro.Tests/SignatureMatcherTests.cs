using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class SignatureMatcherTests
{
    [Fact]
    public void no_entries_matches_nothing()
    {
        var sig = new ColorSignature();
        Assert.False(SignatureMatcher.Matches(255, 255, 255, sig));
    }

    [Fact]
    public void exact_color_matches_with_zero_tolerance()
    {
        var sig = new ColorSignature();
        sig.Entries.Add(new ColorSignatureEntry(200, 100, 50, 0));
        Assert.True(SignatureMatcher.Matches(200, 100, 50, sig));
    }

    [Fact]
    public void outside_tolerance_does_not_match()
    {
        var sig = new ColorSignature();
        sig.Entries.Add(new ColorSignatureEntry(200, 100, 50, 10));
        Assert.False(SignatureMatcher.Matches(215, 100, 50, sig));
    }

    [Fact]
    public void within_tolerance_matches()
    {
        var sig = new ColorSignature();
        sig.Entries.Add(new ColorSignatureEntry(200, 100, 50, 10));
        Assert.True(SignatureMatcher.Matches(208, 95, 55, sig));
    }

    [Fact]
    public void boundary_tolerance_matches()
    {
        var sig = new ColorSignature();
        sig.Entries.Add(new ColorSignatureEntry(200, 100, 50, 10));
        Assert.True(SignatureMatcher.Matches(210, 90, 60, sig));
        Assert.True(SignatureMatcher.Matches(190, 110, 40, sig));
    }

    [Fact]
    public void any_matching_entry_suffices()
    {
        var sig = new ColorSignature();
        sig.Entries.Add(new ColorSignatureEntry(255, 255, 255, 5));  // white
        sig.Entries.Add(new ColorSignatureEntry(150, 150, 150, 20)); // gray
        Assert.True(SignatureMatcher.Matches(255, 255, 255, sig));
        Assert.True(SignatureMatcher.Matches(150, 150, 150, sig));
        Assert.False(SignatureMatcher.Matches(0, 0, 0, sig));
    }

    [Fact]
    public void count_matches_scans_patch_rowmajor()
    {
        var sig = new ColorSignature();
        sig.Entries.Add(new ColorSignatureEntry(255, 255, 255, 0));

        // 3x3 patch: center + two corners white
        var pixels = new (byte r, byte g, byte b)[9];
        pixels[0] = (255, 255, 255);
        pixels[4] = (255, 255, 255);
        pixels[8] = (255, 255, 255);

        int count = SignatureMatcher.CountMatches(pixels, sig);
        Assert.Equal(3, count);
    }
}
