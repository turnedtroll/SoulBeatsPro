using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class OsuConfigImporterTests
{
    private static string WriteTempConfig(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"osu!.test_{Guid.NewGuid():N}.cfg");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void parses_standard_7k_bindings()
    {
        string cfg = @"
# osu! user config
Mania4K1 = 68
Mania4K2 = 70
Mania4K3 = 74
Mania4K4 = 75
Mania7K1 = 83
Mania7K2 = 68
Mania7K3 = 70
Mania7K4 = 32
Mania7K5 = 74
Mania7K6 = 75
Mania7K7 = 76
";
        var path = WriteTempConfig(cfg);
        try
        {
            var keys = OsuConfigImporter.ImportManiaBindings(path, 7);
            Assert.NotNull(keys);
            Assert.Equal(7, keys!.Length);
            Assert.Equal("S", keys[0]);       // 83
            Assert.Equal("D", keys[1]);       // 68
            Assert.Equal("F", keys[2]);       // 70
            Assert.Equal("SPACE", keys[3]);   // 32
            Assert.Equal("J", keys[4]);       // 74
            Assert.Equal("K", keys[5]);       // 75
            Assert.Equal("L", keys[6]);       // 76
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void parses_underscore_separated_format()
    {
        string cfg = @"
Mania4K_1 = 68
Mania4K_2 = 70
Mania4K_3 = 74
Mania4K_4 = 75
";
        var path = WriteTempConfig(cfg);
        try
        {
            var keys = OsuConfigImporter.ImportManiaBindings(path, 4);
            Assert.NotNull(keys);
            Assert.Equal(new[] { "D", "F", "J", "K" }, keys);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void returns_null_when_bindings_incomplete()
    {
        string cfg = @"
Mania7K1 = 83
Mania7K2 = 68
# missing 3-7
";
        var path = WriteTempConfig(cfg);
        try
        {
            var keys = OsuConfigImporter.ImportManiaBindings(path, 7);
            Assert.Null(keys);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void returns_null_when_file_missing()
    {
        var keys = OsuConfigImporter.ImportManiaBindings(
            Path.Combine(Path.GetTempPath(), "does-not-exist.cfg"), 4);
        Assert.Null(keys);
    }

    [Fact]
    public void ignores_mania_bindings_for_other_key_counts()
    {
        string cfg = @"
Mania7K1 = 83
Mania7K2 = 68
Mania4K1 = 68
Mania4K2 = 70
Mania4K3 = 74
Mania4K4 = 75
";
        var path = WriteTempConfig(cfg);
        try
        {
            var keys = OsuConfigImporter.ImportManiaBindings(path, 4);
            Assert.NotNull(keys);
            Assert.Equal(new[] { "D", "F", "J", "K" }, keys);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void parsing_is_case_insensitive_for_prefix()
    {
        string cfg = @"
mania4k1 = 68
MANIA4K2 = 70
Mania4K3 = 74
mania4K4 = 75
";
        var path = WriteTempConfig(cfg);
        try
        {
            var keys = OsuConfigImporter.ImportManiaBindings(path, 4);
            Assert.NotNull(keys);
            Assert.Equal(new[] { "D", "F", "J", "K" }, keys);
        }
        finally { File.Delete(path); }
    }
}
