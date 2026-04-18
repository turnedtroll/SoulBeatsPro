# osu!mania Beatmap-Driven Engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add osu!mania support to SoulBeatsPro that reads `.osu` beatmap files for note timing instead of pixel detection, fixing the hold note persistence issue.

**Architecture:** New `OsuManiaEngine` runs alongside existing `MacroEngine`. Pixel detection syncs to the first note, then all subsequent notes are scheduled from the parsed `.osu` file. A `DetectionMode` enum on `Profile` routes between the two engines. Accuracy presets apply identically.

**Tech Stack:** C# / .NET 8 / WinForms / xUnit. Win32 `SendInput` for keypresses. No new dependencies.

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `robeatspro/OsuBeatmapParser.cs` | Create | Parse `.osu` files into `OsuBeatmap` / `OsuNote` |
| `robeatspro/OsuMapDetector.cs` | Create | Find active beatmap from osu! window title |
| `robeatspro/OsuManiaEngine.cs` | Create | Beatmap-driven engine with sync + scheduling |
| `robeatspro/Profile.cs` | Modify | Add `DetectionMode`, `ManiaKeys`, `OsuSongsPath` |
| `robeatspro/ConfigManager.cs` | Modify | Seed osu!mania profile, add `ManiaKeys` to coord loading |
| `robeatspro/NativeApi.cs` | Modify | N-key scan code support |
| `robeatspro/MacroEngine.cs` | Modify | Route to `OsuManiaEngine` when `DetectionMode == BeatmapFile` |
| `robeatspro/MainTab.cs` | Modify | Dynamic lane count, beatmap status display |
| `robeatspro/DebugForm.cs` | Modify | Dynamic lane count rendering |
| `robeatspro/KeybindsTab.cs` | Modify | N-key keybind UI for mania profiles |
| `robeatspro.Tests/OsuBeatmapParserTests.cs` | Create | Parser unit tests |
| `robeatspro.Tests/OsuMapDetectorTests.cs` | Create | Detector matching tests |
| `robeatspro.Tests/OsuManiaEngineTests.cs` | Create | Engine scheduling tests |

---

### Task 1: Data Model — Profile.DetectionMode + ManiaKeys

**Files:**
- Modify: `robeatspro/Profile.cs`

- [ ] **Step 1: Add DetectionMode enum and new Profile fields**

In `Profile.cs`, add the enum before the `Profile` class and add new properties to `Profile`:

```csharp
// Add after ColorSignature class, before Profile class:

internal enum DetectionMode
{
    PixelBased = 0,
    BeatmapFile = 1
}

// Add these properties inside the Profile class, after the existing MaxJudgmentMs property:

[JsonPropertyName("detectionMode")] public DetectionMode DetectionMode { get; set; } = DetectionMode.PixelBased;
[JsonPropertyName("maniaKeys")] public string[] ManiaKeys { get; set; } = ["D", "F", "J", "K"];
[JsonPropertyName("osuSongsPath")] public string OsuSongsPath { get; set; } = "";
```

- [ ] **Step 2: Build to verify no compilation errors**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add robeatspro/Profile.cs
git commit -m "feat: add DetectionMode, ManiaKeys, OsuSongsPath to Profile"
```

---

### Task 2: Seed osu!mania built-in profile

**Files:**
- Modify: `robeatspro/ConfigManager.cs`

- [ ] **Step 1: Add osu!mania to SeedBuiltInProfiles**

In `ConfigManager.cs`, inside `AppSettings.SeedBuiltInProfiles()`, add a third line after the RoBeats profile:

```csharp
Profiles.Add(new Profile
{
    Name = "osu!mania",
    IsBuiltIn = true,
    MaxJudgmentMs = 100,
    DetectionMode = DetectionMode.BeatmapFile,
    ManiaKeys = ["D", "F", "J", "K", "S", "D", "F", "SPACE", "J", "K"]
});
```

Note: ManiaKeys has 10 slots (osu!mania max). Only the first `keyCount` keys are used. Default covers 4K (D/F/J/K) and has sensible fallbacks for higher key counts that the user can customize.

- [ ] **Step 2: Build to verify**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 3: Run existing tests to verify no regressions**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add robeatspro/ConfigManager.cs
git commit -m "feat: seed osu!mania built-in profile with BeatmapFile detection mode"
```

---

### Task 3: OsuBeatmapParser — Tests First

**Files:**
- Create: `robeatspro.Tests/OsuBeatmapParserTests.cs`

- [ ] **Step 1: Write parser tests**

Create `robeatspro.Tests/OsuBeatmapParserTests.cs`:

```csharp
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

        Assert.Equal(3, beatmap.Notes.Count);

        // First note: hold on column 0, 1000-2000ms
        Assert.True(beatmap.Notes[0].IsHold);
        Assert.Equal(0, beatmap.Notes[0].Column);
        Assert.Equal(1000, beatmap.Notes[0].TimeMs);
        Assert.Equal(2000, beatmap.Notes[0].EndTimeMs);

        // Second note: tap on column 1 at 1500ms
        Assert.False(beatmap.Notes[1].IsHold);
        Assert.Equal(1, beatmap.Notes[1].Column);
        Assert.Equal(1500, beatmap.Notes[1].TimeMs);
        Assert.Equal(0, beatmap.Notes[1].EndTimeMs);

        // Third note: hold on column 2, 1800-2500ms
        Assert.True(beatmap.Notes[2].IsHold);
        Assert.Equal(2, beatmap.Notes[2].Column);
        Assert.Equal(1800, beatmap.Notes[2].TimeMs);
        Assert.Equal(2500, beatmap.Notes[2].EndTimeMs);
    }

    [Fact]
    public void parses_7k_columns_correctly()
    {
        var beatmap = OsuBeatmapParser.Parse(Mania7K);

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
        // Notes in the test data are already sorted, but let's verify
        var beatmap = OsuBeatmapParser.Parse(ManiaWithHolds)!;
        for (int i = 1; i < beatmap.Notes.Count; i++)
            Assert.True(beatmap.Notes[i].TimeMs >= beatmap.Notes[i - 1].TimeMs);
    }

    [Fact]
    public void parses_real_osu_file_from_disk()
    {
        // Uses an actual mania map from the osu! Songs folder
        var songsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "osu!", "Songs");

        if (!Directory.Exists(songsDir))
            return; // Skip on machines without osu! installed

        // Find first mania map
        string? osuFile = null;
        foreach (var dir in Directory.GetDirectories(songsDir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.osu"))
            {
                var text = File.ReadAllText(f);
                if (text.Contains("Mode: 3"))
                {
                    osuFile = f;
                    break;
                }
            }
            if (osuFile != null) break;
        }

        if (osuFile == null) return; // No mania maps installed

        var content = File.ReadAllText(osuFile);
        var beatmap = OsuBeatmapParser.Parse(content);

        Assert.NotNull(beatmap);
        Assert.Equal(3, beatmap.Mode);
        Assert.True(beatmap.KeyCount >= 1 && beatmap.KeyCount <= 10);
        Assert.True(beatmap.Notes.Count > 0);
        Assert.False(string.IsNullOrEmpty(beatmap.Title));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj --filter "FullyQualifiedName~OsuBeatmapParserTests"`
Expected: FAIL — `OsuBeatmapParser` type does not exist

- [ ] **Step 3: Commit failing tests**

```bash
git add robeatspro.Tests/OsuBeatmapParserTests.cs
git commit -m "test: add OsuBeatmapParser tests (red)"
```

---

### Task 4: OsuBeatmapParser — Implementation

**Files:**
- Create: `robeatspro/OsuBeatmapParser.cs`

- [ ] **Step 1: Implement the parser**

Create `robeatspro/OsuBeatmapParser.cs`:

```csharp
namespace SoulBeatsPro;

internal sealed class OsuBeatmap
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Version { get; set; } = "";
    public int KeyCount { get; set; }
    public int Mode { get; set; }
    public List<OsuNote> Notes { get; set; } = new();
}

internal readonly struct OsuNote
{
    public int Column { get; }
    public int TimeMs { get; }
    public int EndTimeMs { get; }
    public bool IsHold { get; }

    public OsuNote(int column, int timeMs, int endTimeMs, bool isHold)
    {
        Column = column;
        TimeMs = timeMs;
        EndTimeMs = endTimeMs;
        IsHold = isHold;
    }
}

internal static class OsuBeatmapParser
{
    /// <summary>
    /// Parse .osu file content into an OsuBeatmap.
    /// Returns null if the file is not osu!mania (Mode != 3).
    /// </summary>
    public static OsuBeatmap? Parse(string content)
    {
        var beatmap = new OsuBeatmap();
        var lines = content.Split('\n');
        string section = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1];
                continue;
            }

            switch (section)
            {
                case "General":
                    ParseGeneral(line, beatmap);
                    break;
                case "Metadata":
                    ParseMetadata(line, beatmap);
                    break;
                case "Difficulty":
                    ParseDifficulty(line, beatmap);
                    break;
                case "HitObjects":
                    ParseHitObject(line, beatmap);
                    break;
            }
        }

        if (beatmap.Mode != 3) return null;

        beatmap.Notes.Sort((a, b) =>
        {
            int cmp = a.TimeMs.CompareTo(b.TimeMs);
            return cmp != 0 ? cmp : a.Column.CompareTo(b.Column);
        });

        return beatmap;
    }

    /// <summary>
    /// Parse a .osu file from disk. Returns null if not mania or file not found.
    /// </summary>
    public static OsuBeatmap? ParseFile(string path)
    {
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path));
    }

    private static void ParseGeneral(string line, OsuBeatmap b)
    {
        if (TryGetValue(line, "Mode", out var val) && int.TryParse(val, out int mode))
            b.Mode = mode;
    }

    private static void ParseMetadata(string line, OsuBeatmap b)
    {
        if (TryGetValue(line, "Title", out var title)) b.Title = title;
        else if (TryGetValue(line, "Artist", out var artist)) b.Artist = artist;
        else if (TryGetValue(line, "Version", out var version)) b.Version = version;
    }

    private static void ParseDifficulty(string line, OsuBeatmap b)
    {
        if (TryGetValue(line, "CircleSize", out var val) && int.TryParse(val, out int cs))
            b.KeyCount = cs;
    }

    private static void ParseHitObject(string line, OsuBeatmap b)
    {
        // Format: x,y,time,type,hitSound,objectParams,hitSample
        // Hold:   x,y,time,type,hitSound,endTime:hitSample
        var parts = line.Split(',');
        if (parts.Length < 5) return;

        if (!int.TryParse(parts[0], out int x)) return;
        if (!int.TryParse(parts[2], out int time)) return;
        if (!int.TryParse(parts[3], out int type)) return;

        int keyCount = b.KeyCount > 0 ? b.KeyCount : 4;
        int column = Math.Clamp(x * keyCount / 512, 0, keyCount - 1);
        bool isHold = (type & 128) != 0; // bit 7

        int endTime = 0;
        if (isHold && parts.Length >= 6)
        {
            // endTime is before the colon in the last param: "endTime:hitSample"
            var endParts = parts[5].Split(':');
            if (endParts.Length > 0)
                int.TryParse(endParts[0], out endTime);
        }

        b.Notes.Add(new OsuNote(column, time, endTime, isHold));
    }

    private static bool TryGetValue(string line, string key, out string value)
    {
        value = "";
        // Match "Key:Value" or "Key: Value"
        if (!line.StartsWith(key, StringComparison.Ordinal)) return false;
        int colonIdx = line.IndexOf(':');
        if (colonIdx < 0 || colonIdx != key.Length) return false;
        value = line[(colonIdx + 1)..].Trim();
        return true;
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj --filter "FullyQualifiedName~OsuBeatmapParserTests"`
Expected: All pass

- [ ] **Step 3: Commit**

```bash
git add robeatspro/OsuBeatmapParser.cs
git commit -m "feat: implement OsuBeatmapParser for .osu file parsing"
```

---

### Task 5: OsuMapDetector — Tests First

**Files:**
- Create: `robeatspro.Tests/OsuMapDetectorTests.cs`

- [ ] **Step 1: Write detector tests**

Create `robeatspro.Tests/OsuMapDetectorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj --filter "FullyQualifiedName~OsuMapDetectorTests"`
Expected: FAIL — `OsuMapDetector` type does not exist

- [ ] **Step 3: Commit failing tests**

```bash
git add robeatspro.Tests/OsuMapDetectorTests.cs
git commit -m "test: add OsuMapDetector tests (red)"
```

---

### Task 6: OsuMapDetector — Implementation

**Files:**
- Create: `robeatspro/OsuMapDetector.cs`

- [ ] **Step 1: Implement the detector**

Create `robeatspro/OsuMapDetector.cs`:

```csharp
namespace SoulBeatsPro;

internal static class OsuMapDetector
{
    private static readonly Dictionary<string, OsuBeatmap> _cache = new();

    /// <summary>
    /// Parse the osu! window title into artist, title, difficulty.
    /// Format: "osu!  - Artist - Title [Difficulty]"
    /// Returns null if the title doesn't match.
    /// </summary>
    public static (string artist, string title, string difficulty)? ParseWindowTitle(string windowTitle)
    {
        // Must start with "osu!" and contain " - "
        const string prefix = "osu!";
        if (!windowTitle.StartsWith(prefix)) return null;

        // Find the first " - " after "osu!"
        int firstDash = windowTitle.IndexOf(" - ", prefix.Length, StringComparison.Ordinal);
        if (firstDash < 0) return null;

        string afterPrefix = windowTitle[(firstDash + 3)..];

        // Find difficulty in [brackets] at the end
        int bracketOpen = afterPrefix.LastIndexOf('[');
        int bracketClose = afterPrefix.LastIndexOf(']');
        if (bracketOpen < 0 || bracketClose <= bracketOpen) return null;

        string difficulty = afterPrefix[(bracketOpen + 1)..bracketClose].Trim();
        string artistAndTitle = afterPrefix[..bracketOpen].Trim();

        // Split artist and title on first " - "
        int titleDash = artistAndTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (titleDash < 0) return null;

        string artist = artistAndTitle[..titleDash].Trim();
        string title = artistAndTitle[(titleDash + 3)..].Trim();

        if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(difficulty))
            return null;

        return (artist, title, difficulty);
    }

    /// <summary>
    /// Check if a parsed beatmap matches the given artist/title/difficulty (case-insensitive).
    /// </summary>
    public static bool MatchesBeatmap(OsuBeatmap beatmap, string artist, string title, string difficulty)
    {
        return string.Equals(beatmap.Artist, artist, StringComparison.OrdinalIgnoreCase)
            && string.Equals(beatmap.Title, title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(beatmap.Version, difficulty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Find the osu! window and read its title. Returns null if osu! is not open or no map is selected.
    /// </summary>
    public static string? GetOsuWindowTitle()
    {
        string? found = null;
        NativeApi.EnumWindows((hWnd, _) =>
        {
            if (!NativeApi.IsWindowVisible(hWnd)) return true;
            int len = NativeApi.GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var buf = new char[len + 1];
            NativeApi.GetWindowText(hWnd, buf, buf.Length);
            var title = new string(buf, 0, len);
            if (title.StartsWith("osu!", StringComparison.OrdinalIgnoreCase) && title.Contains(" - "))
            {
                found = title;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Detect the currently playing beatmap.
    /// 1. Read osu! window title
    /// 2. Parse artist/title/difficulty
    /// 3. Search Songs folder for matching .osu file
    /// 4. Parse and return the beatmap
    /// Returns null with a reason string if detection fails.
    /// </summary>
    public static (OsuBeatmap? beatmap, string status) Detect(string songsPath)
    {
        var windowTitle = GetOsuWindowTitle();
        if (windowTitle == null)
            return (null, "osu! window not found — is osu! running?");

        var parsed = ParseWindowTitle(windowTitle);
        if (parsed == null)
            return (null, "No beatmap selected in osu! — select a map first");

        var (artist, title, difficulty) = parsed.Value;

        if (!Directory.Exists(songsPath))
            return (null, $"Songs folder not found: {songsPath}");

        // Search for matching .osu file
        foreach (var songDir in Directory.GetDirectories(songsPath))
        {
            foreach (var osuFile in Directory.GetFiles(songDir, "*.osu"))
            {
                // Check cache first
                if (_cache.TryGetValue(osuFile, out var cached))
                {
                    if (MatchesBeatmap(cached, artist, title, difficulty))
                        return (cached, $"Detected: {artist} - {title} [{difficulty}] ({cached.KeyCount}K)");
                    continue;
                }

                var beatmap = OsuBeatmapParser.ParseFile(osuFile);
                if (beatmap == null) continue;

                _cache[osuFile] = beatmap;

                if (MatchesBeatmap(beatmap, artist, title, difficulty))
                    return (beatmap, $"Detected: {artist} - {title} [{difficulty}] ({beatmap.KeyCount}K)");
            }
        }

        return (null, $"Could not find beatmap: {artist} - {title} [{difficulty}]");
    }

    /// <summary>
    /// Get the default osu! Songs path.
    /// </summary>
    public static string DefaultSongsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Songs");

    /// <summary>
    /// Clear the beatmap cache.
    /// </summary>
    public static void ClearCache() => _cache.Clear();
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj --filter "FullyQualifiedName~OsuMapDetectorTests"`
Expected: All pass

- [ ] **Step 3: Commit**

```bash
git add robeatspro/OsuMapDetector.cs
git commit -m "feat: implement OsuMapDetector for beatmap detection via window title"
```

---

### Task 7: NativeApi — N-key scan code support

**Files:**
- Modify: `robeatspro/NativeApi.cs`

- [ ] **Step 1: Add N-key UpdateLaneScans overload**

In `NativeApi.cs`, add this method after the existing `UpdateLaneScans(string[] keyNames)` method:

```csharp
/// Build scan codes for N keys (osu!mania variable key count).
public static ushort[] BuildScanCodes(string[] keyNames, int count)
{
    int n = Math.Min(count, keyNames.Length);
    var scans = new ushort[n];
    for (int i = 0; i < n; i++)
    {
        int vk = VkFromName(keyNames[i]);
        scans[i] = vk != 0 ? (ushort)MapVirtualKey((uint)vk, 0) : (ushort)0;
    }
    return scans;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add robeatspro/NativeApi.cs
git commit -m "feat: add NativeApi.BuildScanCodes for N-key support"
```

---

### Task 8: OsuManiaEngine — Tests First

**Files:**
- Create: `robeatspro.Tests/OsuManiaEngineTests.cs`

- [ ] **Step 1: Write scheduling logic tests**

Create `robeatspro.Tests/OsuManiaEngineTests.cs`:

```csharp
using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class OsuManiaEngineTests
{
    [Fact]
    public void build_schedule_creates_press_and_release_events_for_taps()
    {
        var notes = new List<OsuNote>
        {
            new(column: 0, timeMs: 1000, endTimeMs: 0, isHold: false),
            new(column: 1, timeMs: 1200, endTimeMs: 0, isHold: false),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30);

        // Each tap produces a press + release
        Assert.Equal(4, events.Count);

        Assert.Equal(ScheduledEventKind.Press, events[0].Kind);
        Assert.Equal(0, events[0].Column);
        Assert.Equal(1000, events[0].TimeMs);

        Assert.Equal(ScheduledEventKind.Release, events[1].Kind);
        Assert.Equal(0, events[1].Column);
        Assert.Equal(1030, events[1].TimeMs); // 1000 + 30ms min press

        Assert.Equal(ScheduledEventKind.Press, events[2].Kind);
        Assert.Equal(1, events[2].Column);

        Assert.Equal(ScheduledEventKind.Release, events[3].Kind);
        Assert.Equal(1, events[3].Column);
        Assert.Equal(1230, events[3].TimeMs);
    }

    [Fact]
    public void build_schedule_creates_press_and_release_for_holds()
    {
        var notes = new List<OsuNote>
        {
            new(column: 2, timeMs: 1000, endTimeMs: 2000, isHold: true),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30);

        Assert.Equal(2, events.Count);

        Assert.Equal(ScheduledEventKind.Press, events[0].Kind);
        Assert.Equal(2, events[0].Column);
        Assert.Equal(1000, events[0].TimeMs);

        Assert.Equal(ScheduledEventKind.Release, events[1].Kind);
        Assert.Equal(2, events[1].Column);
        Assert.Equal(2000, events[1].TimeMs); // uses endTime, not minPress
    }

    [Fact]
    public void build_schedule_sorts_events_by_time()
    {
        var notes = new List<OsuNote>
        {
            new(column: 0, timeMs: 2000, endTimeMs: 0, isHold: false),
            new(column: 1, timeMs: 1000, endTimeMs: 0, isHold: false),
            new(column: 2, timeMs: 1500, endTimeMs: 3000, isHold: true),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30);

        for (int i = 1; i < events.Count; i++)
            Assert.True(events[i].TimeMs >= events[i - 1].TimeMs,
                $"Event {i} at {events[i].TimeMs}ms is before event {i-1} at {events[i-1].TimeMs}ms");
    }

    [Fact]
    public void skip_first_note_returns_schedule_without_first_note_events()
    {
        var notes = new List<OsuNote>
        {
            new(column: 0, timeMs: 1000, endTimeMs: 0, isHold: false),
            new(column: 1, timeMs: 1200, endTimeMs: 0, isHold: false),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30, skipFirstNote: true);

        // Only second note's press+release
        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].Column);
        Assert.Equal(1200, events[0].TimeMs);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj --filter "FullyQualifiedName~OsuManiaEngineTests"`
Expected: FAIL — types don't exist

- [ ] **Step 3: Commit failing tests**

```bash
git add robeatspro.Tests/OsuManiaEngineTests.cs
git commit -m "test: add OsuManiaEngine scheduling tests (red)"
```

---

### Task 9: OsuManiaEngine — Implementation

**Files:**
- Create: `robeatspro/OsuManiaEngine.cs`

- [ ] **Step 1: Implement the engine**

Create `robeatspro/OsuManiaEngine.cs`:

```csharp
using System.Diagnostics;

namespace SoulBeatsPro;

internal enum ScheduledEventKind { Press, Release }

internal readonly struct ScheduledEvent
{
    public ScheduledEventKind Kind { get; }
    public int Column { get; }
    public int TimeMs { get; }

    public ScheduledEvent(ScheduledEventKind kind, int column, int timeMs)
    {
        Kind = kind; Column = column; TimeMs = timeMs;
    }
}

/// <summary>
/// Beatmap-driven engine for osu!mania. Reads note timing from a parsed .osu file
/// and schedules keypresses. Uses pixel detection for the first note to establish
/// a time sync anchor, then plays all remaining notes from file data.
/// </summary>
internal sealed class OsuManiaEngine
{
    private readonly MacroEngine _parent;
    private readonly OsuBeatmap _beatmap;
    private readonly ushort[] _scanCodes;
    private readonly int _keyCount;
    private volatile bool _stopRequested;
    private readonly Random _rng = new();

    public string SyncStatus { get; private set; } = "Not synced";
    public string DetectionStatus { get; set; } = "";

    public OsuManiaEngine(MacroEngine parent, OsuBeatmap beatmap, ushort[] scanCodes)
    {
        _parent = parent;
        _beatmap = beatmap;
        _scanCodes = scanCodes;
        _keyCount = beatmap.KeyCount;
    }

    public void Stop() => _stopRequested = true;

    /// <summary>
    /// Build a sorted list of press/release events from the beatmap notes.
    /// Pure function — no IO, no state. Used by tests.
    /// </summary>
    public static List<ScheduledEvent> BuildSchedule(
        List<OsuNote> notes,
        double minPressDurationMs,
        bool skipFirstNote = false)
    {
        var events = new List<ScheduledEvent>(notes.Count * 2);
        int start = skipFirstNote ? 1 : 0;

        for (int i = start; i < notes.Count; i++)
        {
            var note = notes[i];
            events.Add(new ScheduledEvent(ScheduledEventKind.Press, note.Column, note.TimeMs));

            int releaseTime;
            if (note.IsHold && note.EndTimeMs > note.TimeMs)
                releaseTime = note.EndTimeMs;
            else
                releaseTime = note.TimeMs + (int)minPressDurationMs;

            events.Add(new ScheduledEvent(ScheduledEventKind.Release, note.Column, releaseTime));
        }

        events.Sort((a, b) =>
        {
            int cmp = a.TimeMs.CompareTo(b.TimeMs);
            if (cmp != 0) return cmp;
            // Releases before presses at same time (so a release-then-press on same column works)
            return a.Kind.CompareTo(b.Kind);
        });

        return events;
    }

    /// <summary>
    /// Main engine loop. Call from a background thread.
    /// </summary>
    public void Run()
    {
        var profile = ConfigManager.Instance.ActiveProfile;
        var tuning = profile.Tuning;
        var preset = profile.AccuracyPreset;
        var maxJudgmentMs = profile.MaxJudgmentMs;
        double toggleDelay = tuning.ToggleDelay;

        // Build schedule (skip first note — pixel detection handles it)
        var schedule = BuildSchedule(_beatmap.Notes, tuning.MinPressDurationMs, skipFirstNote: true);
        if (schedule.Count == 0 && _beatmap.Notes.Count <= 1)
        {
            SyncStatus = "Beatmap has no notes to play";
            return;
        }

        // === SYNC PHASE ===
        // Use pixel detection on the first note's column to establish timing anchor.
        SyncStatus = "Waiting for first note...";

        var firstNote = _beatmap.Notes[0];
        int syncColumn = firstNote.Column;

        // Set up pixel detection for sync
        (var tapPixels, _) = ConfigManager.Instance.LoadCoords();
        var signatures = profile.Signatures;
        int sampleHalf = tuning.SampleHalf;
        int minPixels = tuning.MinPixels;

        // We need at least one tap pixel for the sync column
        if (tapPixels.Length == 0 || syncColumn >= tapPixels.Length)
        {
            SyncStatus = "No tap pixels configured — calibrate first";
            return;
        }

        var syncPoint = tapPixels[Math.Min(syncColumn, tapPixels.Length - 1)];
        var syncSig = syncColumn < signatures.Length ? signatures[syncColumn] : new ColorSignature();

        int capSize = sampleHalf * 2 + 4;
        int capLeft = syncPoint.X - sampleHalf - 1;
        int capTop = syncPoint.Y - sampleHalf - 1;

        using var capture = new ScreenCapture(capLeft, capTop, capSize, capSize);
        int relX = syncPoint.X - capLeft;
        int relY = syncPoint.Y - capTop;

        var sw = Stopwatch.StartNew();
        double lastToggle = 0;
        bool synced = false;
        double anchorWallTime = 0;
        int anchorBeatmapMs = firstNote.TimeMs;

        // Sync loop: wait for first note to appear
        while (!_stopRequested && !synced)
        {
            double now = sw.Elapsed.TotalSeconds;

            // Pause toggle
            if (NativeApi.IsKeyDown(ConfigManager.Instance.Keybinds.Pause) && now - lastToggle > toggleDelay)
            {
                _parent.Active = !_parent.Active;
                lastToggle = now;
                if (!_parent.Active) ReleaseAll();
            }
            if (!_parent.Active) { Thread.Sleep(10); continue; }

            try { capture.Grab(); } catch { Thread.Sleep(1); continue; }

            int matchCount = capture.CountSignatureMatches(relX, relY, sampleHalf, syncSig);
            if (matchCount >= minPixels)
            {
                // First note detected — press its key and record anchor time
                if (syncColumn < _scanCodes.Length)
                    NativeApi.PressScan(_scanCodes[syncColumn]);

                _parent.States[syncColumn] = MacroEngine.LaneState.Pressing;
                anchorWallTime = sw.Elapsed.TotalSeconds;
                synced = true;
                SyncStatus = "Synced";

                // Schedule release for first note
                double firstReleaseDelaySec;
                if (firstNote.IsHold && firstNote.EndTimeMs > firstNote.TimeMs)
                    firstReleaseDelaySec = (firstNote.EndTimeMs - firstNote.TimeMs) / 1000.0;
                else
                    firstReleaseDelaySec = tuning.MinPressDurationMs / 1000.0;

                // Wait and release first note
                while (!_stopRequested && (sw.Elapsed.TotalSeconds - anchorWallTime) < firstReleaseDelaySec)
                    Thread.SpinWait(100);

                if (syncColumn < _scanCodes.Length)
                    NativeApi.ReleaseScan(_scanCodes[syncColumn]);
                _parent.States[syncColumn] = MacroEngine.LaneState.Released;
            }

            Thread.SpinWait(100);
        }

        if (_stopRequested) { ReleaseAll(); return; }

        // === PLAYBACK PHASE ===
        int eventIdx = 0;
        int frameCount = 0;
        double fpsTimer = sw.Elapsed.TotalSeconds;

        while (!_stopRequested && eventIdx < schedule.Count)
        {
            double now = sw.Elapsed.TotalSeconds;

            // FPS counter
            frameCount++;
            if (now - fpsTimer >= 1.0)
            {
                _parent.Fps = frameCount;
                frameCount = 0;
                fpsTimer = now;
            }

            // Pause toggle
            if (NativeApi.IsKeyDown(ConfigManager.Instance.Keybinds.Pause) && now - lastToggle > toggleDelay)
            {
                _parent.Active = !_parent.Active;
                lastToggle = now;
                if (!_parent.Active) ReleaseAll();
            }
            if (!_parent.Active) { Thread.Sleep(10); continue; }

            // Calculate what beatmap time we're at
            double elapsedSinceAnchor = now - anchorWallTime;
            double currentBeatmapMs = anchorBeatmapMs + elapsedSinceAnchor * 1000.0;

            // Process all events whose time has arrived
            while (eventIdx < schedule.Count)
            {
                var evt = schedule[eventIdx];
                double targetMs = evt.TimeMs;

                // Apply accuracy preset jitter to press events only
                if (evt.Kind == ScheduledEventKind.Press)
                {
                    double delayMs = AccuracyPresetTable.SampleDelaySeconds(preset, maxJudgmentMs, _rng) * 1000.0;
                    targetMs += delayMs;
                }

                if (currentBeatmapMs < targetMs) break;

                // Fire the event
                if (evt.Column < _scanCodes.Length)
                {
                    if (evt.Kind == ScheduledEventKind.Press)
                    {
                        NativeApi.PressScan(_scanCodes[evt.Column]);
                        if (evt.Column < _parent.States.Length)
                            _parent.States[evt.Column] = MacroEngine.LaneState.Pressing;
                    }
                    else
                    {
                        NativeApi.ReleaseScan(_scanCodes[evt.Column]);
                        if (evt.Column < _parent.States.Length)
                            _parent.States[evt.Column] = MacroEngine.LaneState.Released;
                    }
                }

                eventIdx++;
            }

            Thread.SpinWait(100);
        }

        // Done — release everything
        ReleaseAll();
    }

    private void ReleaseAll()
    {
        for (int i = 0; i < _scanCodes.Length; i++)
        {
            NativeApi.ReleaseScan(_scanCodes[i]);
            if (i < _parent.States.Length)
                _parent.States[i] = MacroEngine.LaneState.Released;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj --filter "FullyQualifiedName~OsuManiaEngineTests"`
Expected: All pass

- [ ] **Step 3: Run all tests**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add robeatspro/OsuManiaEngine.cs
git commit -m "feat: implement OsuManiaEngine with sync and beatmap-driven scheduling"
```

---

### Task 10: Wire MacroEngine to OsuManiaEngine

**Files:**
- Modify: `robeatspro/MacroEngine.cs`

- [ ] **Step 1: Add routing in MacroEngine.Start()**

In `MacroEngine.cs`, modify the `Start()` method. After the existing setup lines (loading profile, coords, tuning), add the BeatmapFile branch before `_thread.Start()`:

Replace the `_thread` creation block at the bottom of `Start()`:

```csharp
// Replace these lines at the end of Start():
_stopRequested = false;
Running = true;
_thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
_thread.Start();
```

With:

```csharp
_stopRequested = false;
Running = true;

if (profile.DetectionMode == DetectionMode.BeatmapFile)
{
    _thread = new Thread(OsuManiaLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
    _thread.Start();
}
else
{
    _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
    _thread.Start();
}
```

- [ ] **Step 2: Add OsuManiaLoop method to MacroEngine**

Add this method after the existing `Loop()` method in `MacroEngine.cs`:

```csharp
private OsuManiaEngine? _osuEngine;

private void OsuManiaLoop()
{
    var profile = ConfigManager.Instance.ActiveProfile;
    string songsPath = string.IsNullOrEmpty(profile.OsuSongsPath)
        ? OsuMapDetector.DefaultSongsPath
        : profile.OsuSongsPath;

    var (beatmap, status) = OsuMapDetector.Detect(songsPath);
    if (beatmap == null)
    {
        Log.Warn($"[OsuMania] Detection failed: {status}");
        Running = false;
        OnStopped?.Invoke();
        return;
    }

    // Validate key count
    if (beatmap.KeyCount > profile.ManiaKeys.Length)
    {
        Log.Warn($"[OsuMania] Beatmap is {beatmap.KeyCount}K but only {profile.ManiaKeys.Length} keys configured");
        Running = false;
        OnStopped?.Invoke();
        return;
    }

    var scanCodes = NativeApi.BuildScanCodes(profile.ManiaKeys, beatmap.KeyCount);

    _osuEngine = new OsuManiaEngine(this, beatmap, scanCodes);
    _osuEngine.DetectionStatus = status;
    _osuEngine.Run();

    _osuEngine = null;
    Running = false;
    OnStopped?.Invoke();
}
```

- [ ] **Step 3: Update Stop() to also stop the osu engine**

Modify the `Stop()` method:

```csharp
public void Stop()
{
    _stopRequested = true;
    _osuEngine?.Stop();
}
```

- [ ] **Step 4: Add field declaration for _osuEngine at class level**

Move the `_osuEngine` field declaration to the top of the class with the other private fields (remove it from inside `OsuManiaLoop` if you put it there):

```csharp
private OsuManiaEngine? _osuEngine;
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 6: Run all tests**

Run: `dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj`
Expected: All pass

- [ ] **Step 7: Commit**

```bash
git add robeatspro/MacroEngine.cs
git commit -m "feat: route MacroEngine to OsuManiaEngine when DetectionMode is BeatmapFile"
```

---

### Task 11: MainTab — Dynamic lane display + beatmap status

**Files:**
- Modify: `robeatspro/MainTab.cs`

- [ ] **Step 1: Make lane labels dynamic**

In `MainTab.cs`, change the hardcoded `_laneLabels` array and lane group to support up to 10 lanes. Replace the lane labels field and the lane group creation code.

Replace the field:
```csharp
private readonly Label[] _laneLabels = new Label[4];
```
With:
```csharp
private Label[] _laneLabels = new Label[4];
```

In the constructor, after the lane group creation, replace the hardcoded 4-lane loop:

```csharp
// Replace the lane label creation loop and lane group size
_laneGroup = new GroupBox
{
    Text = "Lane States",
    Font = font,
    Location = new Point(10, y),
    Size = new Size(280, 85),
    Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
};
Controls.Add(_laneGroup);

int laneSpacing = 16;
for (int i = 0; i < 4; i++)
{
    _laneLabels[i] = new Label
    {
        Text = $"{MacroEngine.LaneNames[i]}: IDLE",
        Font = font,
        AutoSize = true,
        Location = new Point(12, 18 + i * laneSpacing),
        TextAlign = ContentAlignment.MiddleLeft
    };
    _laneGroup.Controls.Add(_laneLabels[i]);
}
```

With:

```csharp
_laneGroup = new GroupBox
{
    Text = "Lane States",
    Font = font,
    Location = new Point(10, y),
    Size = new Size(280, 85),
    Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
};
Controls.Add(_laneGroup);

int laneSpacing = 16;
RebuildLaneLabels(font, laneSpacing);
```

Then add a helper method to the class:

```csharp
private void RebuildLaneLabels(Font font, int spacing)
{
    _laneGroup.Controls.Clear();
    int count = 4;
    var profile = ConfigManager.Instance.ActiveProfile;
    if (profile.DetectionMode == DetectionMode.BeatmapFile)
        count = Math.Min(profile.ManiaKeys.Length, 10);

    _laneLabels = new Label[count];
    for (int i = 0; i < count; i++)
    {
        string name = i < MacroEngine.LaneNames.Length ? MacroEngine.LaneNames[i] : $"{i + 1}";
        _laneLabels[i] = new Label
        {
            Text = $"{name}: IDLE",
            Font = font,
            AutoSize = true,
            Location = new Point(12, 18 + i * spacing),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _laneGroup.Controls.Add(_laneLabels[i]);
    }
    _laneGroup.Size = new Size(280, Math.Max(85, 22 + count * spacing));
}
```

- [ ] **Step 2: Update Timer_Tick to handle variable lane count**

In `Timer_Tick`, replace the hardcoded `for (int i = 0; i < 4; i++)` lane state loop:

```csharp
for (int i = 0; i < _laneLabels.Length; i++)
{
    string name = i < MacroEngine.LaneNames.Length ? MacroEngine.LaneNames[i] : $"{i + 1}";
    if (i < _engine.States.Length)
    {
        var s = _engine.States[i];
        _laneLabels[i].Text = $"{name}: {s}";
        _laneLabels[i].ForeColor = s == MacroEngine.LaneState.Pressing
            ? (i < MacroEngine.LaneColors.Length ? MacroEngine.LaneColors[i] : Color.LimeGreen)
            : ConfigManager.Instance.Theme.GetTextColor();
    }
    else
    {
        _laneLabels[i].Text = $"{name}: IDLE";
    }
}
```

Also update `OnEngineStopped` similarly:

```csharp
for (int i = 0; i < _laneLabels.Length; i++)
{
    string name = i < MacroEngine.LaneNames.Length ? MacroEngine.LaneNames[i] : $"{i + 1}";
    _laneLabels[i].Text = $"{name}: IDLE";
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add robeatspro/MainTab.cs
git commit -m "feat: dynamic lane count display in MainTab for osu!mania"
```

---

### Task 12: DebugForm — Dynamic lane count

**Files:**
- Modify: `robeatspro/DebugForm.cs`

- [ ] **Step 1: Replace hardcoded 4-lane loops**

In `DebugForm.cs`, in the `Timer_Tick` method, replace both `for (int i = 0; i < 4; i++)` loops.

For the tap point overlay loop (around line 98), replace:
```csharp
for (int i = 0; i < 4; i++)
```
With:
```csharp
int laneCount = Math.Min(_engine.States.Length, _engine.TapPixels?.Length ?? 4);
for (int i = 0; i < laneCount; i++)
```

For the left panel lane status loop (around line 160), replace:
```csharp
for (int i = 0; i < 4; i++)
```
With:
```csharp
for (int i = 0; i < laneCount; i++)
```

Also guard the `laneKeys`, `MatchCounts`, and `PendingScheduled` accesses with bounds checks:

```csharp
string keyDisp = i < laneKeys.Length ? NativeApi.DisplayName(laneKeys[i]) : $"{i+1}";
```

And in the lane status section:
```csharp
int matchCount = i < _engine.MatchCounts.Length ? _engine.MatchCounts[i] : 0;
bool isPending = i < pending.Length && pending[i];
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add robeatspro/DebugForm.cs
git commit -m "feat: dynamic lane count in DebugForm overlay"
```

---

### Task 13: KeybindsTab — N-key support for mania profiles

**Files:**
- Modify: `robeatspro/KeybindsTab.cs`

- [ ] **Step 1: Add mania keybinds group**

In the `KeybindsTab` constructor, after the existing Control Keys GroupBox, add a conditional mania keys section. Add this before the hint label:

```csharp
// ── Mania Keys GroupBox (only for BeatmapFile profiles) ──
var activeProfile = ConfigManager.Instance.ActiveProfile;
if (activeProfile.DetectionMode == DetectionMode.BeatmapFile)
{
    var maniaGroup = new GroupBox
    {
        Text = "osu!mania Keys (key count auto-detected from beatmap)",
        Font = RetroFont,
        Location = new Point(12, 304),
        Size = new Size(320, 310),
        FlatStyle = FlatStyle.Standard,
        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
    };

    var maniaKeys = activeProfile.ManiaKeys;
    var maniaButtons = new Button[maniaKeys.Length];

    for (int i = 0; i < maniaKeys.Length && i < 10; i++)
    {
        int idx = i;
        var lbl = new Label
        {
            Text = $"Key {i + 1}:",
            Font = RetroFont,
            AutoSize = true,
            Location = new Point(14, 22 + i * 28)
        };
        maniaGroup.Controls.Add(lbl);

        var btn = new Button
        {
            Text = NativeApi.DisplayName(maniaKeys[i]),
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
            Size = new Size(80, 23),
            Location = new Point(220, 18 + i * 28),
            Tag = $"mania{idx}",
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btn.Click += KeyButton_Click;
        maniaGroup.Controls.Add(btn);
        maniaButtons[i] = btn;
    }

    Controls.Add(maniaGroup);
}
```

- [ ] **Step 2: Update ApplyKey to handle mania keys**

In the `ApplyKey` method, add a branch for mania keys after the lane key handling:

```csharp
// Add after the existing isLane block:
else if (target.StartsWith("mania") && int.TryParse(target.AsSpan(5), out int maniaIdx))
{
    var profile = ConfigManager.Instance.ActiveProfile;
    if (maniaIdx >= 0 && maniaIdx < profile.ManiaKeys.Length)
    {
        profile.ManiaKeys[maniaIdx] = keyName;
    }
}
```

Also update `RestoreButtonText` to handle mania keys:

```csharp
// Add a branch in the switch:
_ when target.StartsWith("mania") => 
    int.TryParse(target.AsSpan(5), out int mi) && mi < ConfigManager.Instance.ActiveProfile.ManiaKeys.Length
        ? ConfigManager.Instance.ActiveProfile.ManiaKeys[mi] : "",
```

Actually, it's cleaner to add it as an if-else before the switch:

In `RestoreButtonText`, replace the switch with:

```csharp
private void RestoreButtonText(Button btn, string? target)
{
    if (target == null) return;
    var kb = ConfigManager.Instance.Keybinds;

    string key;
    if (target.StartsWith("mania") && int.TryParse(target.AsSpan(5), out int mi))
    {
        var maniaKeys = ConfigManager.Instance.ActiveProfile.ManiaKeys;
        key = mi < maniaKeys.Length ? maniaKeys[mi] : "";
    }
    else
    {
        key = target switch
        {
            "lane0" => kb.Lane1,
            "lane1" => kb.Lane2,
            "lane2" => kb.Lane3,
            "lane3" => kb.Lane4,
            "pause" => kb.Pause,
            "debug" => kb.Debug,
            "screenshot" => kb.Screenshot,
            "quit" => kb.Quit,
            _ => ""
        };
    }
    btn.Text = NativeApi.DisplayName(key);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build robeatspro/RoBeatsPro.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add robeatspro/KeybindsTab.cs
git commit -m "feat: add osu!mania N-key keybind configuration UI"
```

---

### Task 14: Integration — Build + Full Test Pass

**Files:** All

- [ ] **Step 1: Full build**

Run: `dotnet build RoBeatsPro.sln`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Full test suite**

Run: `dotnet test RoBeatsPro.sln`
Expected: All tests pass

- [ ] **Step 3: Verify the solution runs**

Run: `dotnet run --project robeatspro/RoBeatsPro.csproj`
Expected: Application starts without errors. The profile selector should show "osu!mania" as a third option.

- [ ] **Step 4: Final commit with all remaining changes**

```bash
git add -A
git commit -m "feat: complete osu!mania beatmap-driven engine integration"
```
