using System.Text.Json;
using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class MigrationTests
{
    /// <summary>Helper: deserialize a raw json string into AppSettings and run migration.</summary>
    private static AppSettings LoadAndMigrate(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.Migrate();
        return settings;
    }

    [Fact]
    public void new_schema_passes_through_unchanged()
    {
        string json = """
        {
          "gameMode": { "activeProfileName": "Funky Friday" },
          "profiles": [
            { "name": "Funky Friday", "isBuiltIn": true,
              "signatures": [ { "entries": [] }, { "entries": [] }, { "entries": [] }, { "entries": [] } ],
              "tap": [], "hold": [] }
          ]
        }
        """;
        var s = LoadAndMigrate(json);
        Assert.Single(s.Profiles);
        Assert.Equal("Funky Friday", s.Profiles[0].Name);
    }

    [Fact]
    public void two_profile_schema_migrates_ff_and_robeats_with_signatures()
    {
        string json = """
        {
          "gameMode": { "activeGame": "funkyFriday" },
          "profiles_legacy_twoProfile": {
            "funkyFriday": {
              "detection": {
                "whiteGray": { "whiteMin": 240, "grayMin": 130, "grayMax": 170 }
              },
              "tap":  [[100,950],[200,950],[300,950],[400,950]],
              "hold": [[100,800],[200,800],[300,800],[400,800]]
            },
            "robeats": {
              "detection": {
                "noteColor": { "minR":200,"minG":180,"maxB":80,"pickedR":255,"pickedG":215,"pickedB":0 },
                "holdColor": { "minR":120,"maxR":200,"minG":100,"maxG":180,"maxB":80,"minRG":230,"pickedR":160,"pickedG":120,"pickedB":40 }
              },
              "tap":  [[100,900],[200,900],[300,900],[400,900]],
              "hold": [[100,750],[200,750],[300,750],[400,750]]
            }
          }
        }
        """;
        var s = LoadAndMigrate(json);
        Assert.Equal(2, s.Profiles.Count);
        var ff = s.Profiles.Find(p => p.Name == "Funky Friday")!;
        var rb = s.Profiles.Find(p => p.Name == "RoBeats")!;
        Assert.True(ff.IsBuiltIn);
        Assert.True(rb.IsBuiltIn);

        foreach (var s1 in ff.Signatures)
        {
            Assert.Equal(2, s1.Entries.Count);
            Assert.Equal(247, s1.Entries[0].R); // (240+255)/2 = 247 — whiteMid
        }
        foreach (var s1 in rb.Signatures)
        {
            Assert.Equal(2, s1.Entries.Count);
            Assert.Equal(255, s1.Entries[0].R);
            Assert.Equal(215, s1.Entries[0].G);
            Assert.Equal(0,   s1.Entries[0].B);
        }

        Assert.Equal("Funky Friday", s.GameMode.ActiveProfileName);
    }

    [Fact]
    public void legacy_flat_schema_seeds_both_builtin_profiles()
    {
        string json = """
        {
          "gameMode": { "activeGame": "funkyFriday" },
          "detection": {
            "whiteGray": { "whiteMin": 240, "grayMin": 130, "grayMax": 170 }
          }
        }
        """;
        var s = LoadAndMigrate(json);
        Assert.Equal(2, s.Profiles.Count);
        Assert.Contains(s.Profiles, p => p.Name == "Funky Friday");
        Assert.Contains(s.Profiles, p => p.Name == "RoBeats");
    }
}
