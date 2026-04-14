# Universal Mania Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two hardcoded detection modes (WhiteGray for Funky Friday, Color for RoBeats) with a single "press-while-present" algorithm driven by per-lane color signatures, plus a generic profile manager that lets users create profiles for any 4-lane mania game.

**Architecture:** Each `Profile` owns a 4-element array of `ColorSignature` objects (one per lane). Each `ColorSignature` is a list of `(r, g, b, tolerance)` entries captured during calibration. At runtime, the detection engine samples one patch per lane at the judgment line, counts pixels matching any signature entry, and runs a two-state (Released/Pressing) machine per lane. A rising edge fires a press (optionally delayed by accuracy preset); a run of `N` clean frames releases it.

**Tech Stack:** C# 12 / .NET 8 / WinForms (existing). Adds a new xUnit test project for pure-logic tests (signature matching, variance math, migration).

---

## Spec Reference

See `docs/superpowers/specs/2026-04-13-universal-mania-detection-design.md` for the design. This plan implements every section of that spec.

## File Structure

**New files:**
- `robeatspro/Profile.cs` — `Profile`, `ColorSignature`, `ColorSignatureEntry` types.
- `robeatspro/SignatureMatcher.cs` — pure pixel-classification logic, counts matches in a patch.
- `robeatspro/ProfilesTab.cs` — new UI tab. Replaces `GamesTab.cs`.
- `robeatspro.Tests/RoBeatsPro.Tests.csproj` — xUnit test project.
- `robeatspro.Tests/SignatureMatcherTests.cs` — pure tests for signature matching.
- `robeatspro.Tests/AccuracyPresetTests.cs` — pure tests for preset → delay math.
- `robeatspro.Tests/MigrationTests.cs` — tests for config migration paths.
- `robeatspro.Tests/DetectionStateMachineTests.cs` — tests for the two-state per-lane machine.
- `robeatspro/DetectionLane.cs` — pure per-lane state machine (extracted from MacroEngine for testability).

**Modified files:**
- `robeatspro/ConfigManager.cs` — replaces `ProfilesSettings` (hardcoded FF + RoBeats) with `List<Profile> Profiles` + `ActiveProfileName`. Rewrites migration.
- `robeatspro/MacroEngine.cs` — replaces WhiteGray/Color branching Loop with press-while-present using `DetectionLane` instances.
- `robeatspro/ScreenCapture.cs` — adds `CountSignatureMatches(cx, cy, half, signature)` method. Keeps `GetContextPatch` but rewires it to use signature classification.
- `robeatspro/AccuracyPreset.cs` — replaces per-game hardcoded tables with `GetMaxDelaySeconds(preset, maxJudgmentMs)`.
- `robeatspro/MainForm.cs` — swap `GamesTab` instantiation for `ProfilesTab`.
- `robeatspro/CalibrationTab.cs` — capture multi-frame color samples during calibration, write to `Profile.Signatures[lane]` with variance-derived tolerance.
- `robeatspro/ColorsTab.cs` — surface the active profile's signatures (read-only or minimal edit), remove old `NoteColor`/`HoldColor` UI paths.
- `robeatspro/DebugForm.cs` — update to read the new profile structure.
- `robeatspro/RoBeatsPro.csproj` — reference the Tests project indirectly (solution-level only; no production dependency change).

**Deleted files:**
- `robeatspro/GamesTab.cs` — replaced by `ProfilesTab.cs`.

---

## Task 1: Add xUnit test project

**Files:**
- Create: `robeatspro.Tests/RoBeatsPro.Tests.csproj`
- Create: `robeatspro.Tests/Usings.cs`
- Create: `RoBeatsPro.sln` (if it doesn't exist yet — check first)

- [ ] **Step 1: Check for existing solution file**

Run from repo root: `ls *.sln`
- If a `.sln` exists, skip step 2.
- If not, continue to step 2.

- [ ] **Step 2: Create solution, add existing csproj**

Run from repo root:
```bash
dotnet new sln -n RoBeatsPro
dotnet sln add robeatspro/RoBeatsPro.csproj
```
Expected: `RoBeatsPro.sln` created. `RoBeatsPro.csproj` added.

- [ ] **Step 3: Create the test project**

Run from repo root:
```bash
dotnet new xunit -o robeatspro.Tests -n RoBeatsPro.Tests --framework net8.0
dotnet sln add robeatspro.Tests/RoBeatsPro.Tests.csproj
dotnet add robeatspro.Tests/RoBeatsPro.Tests.csproj reference robeatspro/RoBeatsPro.csproj
```
Expected: project scaffolds, adds to solution, references the main project.

- [ ] **Step 4: Make main project types visible to tests**

Edit `robeatspro/RoBeatsPro.csproj`, add before the closing `</Project>`:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="RoBeatsPro.Tests" />
</ItemGroup>
```

- [ ] **Step 5: Overwrite the default test class with a placeholder**

Replace `robeatspro.Tests/UnitTest1.cs` with:
```csharp
namespace RoBeatsPro.Tests;

public class PlaceholderTests
{
    [Fact]
    public void test_project_builds() => Assert.True(true);
}
```

- [ ] **Step 6: Verify build + test run**

Run from repo root:
```bash
dotnet test robeatspro.Tests/RoBeatsPro.Tests.csproj
```
Expected: build succeeds, 1 test passes.

- [ ] **Step 7: Commit**

```bash
git add RoBeatsPro.sln robeatspro.Tests/ robeatspro/RoBeatsPro.csproj
git commit -m "test: add xunit test project for universal detection work"
```

---

## Task 2: Pure signature-matching logic with tests

**Files:**
- Create: `robeatspro/Profile.cs`
- Create: `robeatspro/SignatureMatcher.cs`
- Create: `robeatspro.Tests/SignatureMatcherTests.cs`

- [ ] **Step 1: Write the failing tests first**

Create `robeatspro.Tests/SignatureMatcherTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test robeatspro.Tests/ --filter SignatureMatcherTests
```
Expected: compile errors — `ColorSignature`, `ColorSignatureEntry`, `SignatureMatcher` not defined.

- [ ] **Step 3: Implement `Profile.cs`**

Create `robeatspro/Profile.cs`:
```csharp
using System.Text.Json.Serialization;

namespace SoulBeatsPro;

internal sealed class ColorSignatureEntry
{
    [JsonPropertyName("r")] public int R { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("b")] public int B { get; set; }
    [JsonPropertyName("tolerance")] public int Tolerance { get; set; }

    public ColorSignatureEntry() { }

    public ColorSignatureEntry(int r, int g, int b, int tolerance)
    {
        R = r; G = g; B = b; Tolerance = tolerance;
    }
}

internal sealed class ColorSignature
{
    [JsonPropertyName("entries")]
    public List<ColorSignatureEntry> Entries { get; set; } = new();
}

internal sealed class Profile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "New Profile";
    [JsonPropertyName("isBuiltIn")] public bool IsBuiltIn { get; set; }
    [JsonPropertyName("signatures")]
    public ColorSignature[] Signatures { get; set; } =
        new[] { new ColorSignature(), new ColorSignature(), new ColorSignature(), new ColorSignature() };
    [JsonPropertyName("tap")] public int[][] Tap { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("hold")] public int[][] Hold { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("tuning")] public TuningSettings Tuning { get; set; } = new();
    [JsonPropertyName("accuracyPreset")] public AccuracyPreset AccuracyPreset { get; set; } = AccuracyPreset.PerfectOnly;
    [JsonPropertyName("maxJudgmentMs")] public double MaxJudgmentMs { get; set; } = 100.0;
}
```

- [ ] **Step 4: Implement `SignatureMatcher.cs`**

Create `robeatspro/SignatureMatcher.cs`:
```csharp
namespace SoulBeatsPro;

/// <summary>Pure pixel classification against a color signature. No IO.</summary>
internal static class SignatureMatcher
{
    public static bool Matches(byte r, byte g, byte b, ColorSignature sig)
    {
        foreach (var e in sig.Entries)
        {
            int tol = e.Tolerance;
            if (Math.Abs(r - e.R) <= tol &&
                Math.Abs(g - e.G) <= tol &&
                Math.Abs(b - e.B) <= tol)
                return true;
        }
        return false;
    }

    public static int CountMatches((byte r, byte g, byte b)[] pixels, ColorSignature sig)
    {
        int count = 0;
        foreach (var (r, g, b) in pixels)
            if (Matches(r, g, b, sig)) count++;
        return count;
    }
}
```

- [ ] **Step 5: Extend `TuningSettings` with new fields**

Open `robeatspro/ConfigManager.cs`. Find `TuningSettings` (around line 78). Add these fields just before `Reset()`:
```csharp
[JsonPropertyName("cleanFrames")] public int CleanFrames { get; set; } = 3;
[JsonPropertyName("minPressDurationMs")] public double MinPressDurationMs { get; set; } = 20.0;
```
Update the `Reset()` method to include them:
```csharp
public void Reset()
{
    var d = new TuningSettings();
    SampleHalf = d.SampleHalf; MinPixels = d.MinPixels;
    TapKeyDuration = d.TapKeyDuration; HoldReleaseCooldown = d.HoldReleaseCooldown;
    ToggleDelay = d.ToggleDelay; HoldArmGrace = d.HoldArmGrace;
    HoldReleaseGrace = d.HoldReleaseGrace;
    CleanFrames = d.CleanFrames; MinPressDurationMs = d.MinPressDurationMs;
}
```

- [ ] **Step 6: Run the tests and verify they pass**

Run:
```bash
dotnet test robeatspro.Tests/ --filter SignatureMatcherTests
```
Expected: all 7 tests pass.

- [ ] **Step 7: Commit**

```bash
git add robeatspro/Profile.cs robeatspro/SignatureMatcher.cs robeatspro/ConfigManager.cs robeatspro.Tests/SignatureMatcherTests.cs
git commit -m "feat(detection): Profile + ColorSignature types + pure matcher"
```

---

## Task 3: Generalize AccuracyPresetTable

**Files:**
- Modify: `robeatspro/AccuracyPreset.cs` (full rewrite)
- Create: `robeatspro.Tests/AccuracyPresetTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `robeatspro.Tests/AccuracyPresetTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test robeatspro.Tests/ --filter AccuracyPresetTests
```
Expected: compile fails — `GetMaxDelaySeconds(preset, double)` doesn't exist (current signature takes `bool whiteGrayMode`).

- [ ] **Step 3: Rewrite `AccuracyPreset.cs`**

Replace the entire contents of `robeatspro/AccuracyPreset.cs` with:
```csharp
namespace SoulBeatsPro;

/// <summary>
/// Profile-agnostic accuracy preset. Each profile stores a MaxJudgmentMs
/// (late edge of last non-miss judgment minus safety margin) and the preset
/// determines the fraction of that window used for the random delay.
/// </summary>
internal enum AccuracyPreset
{
    PerfectOnly = 0,    // MaxAccuracy
    MostlyPerfects = 1, // HighAccuracy
    HumanLike = 2,
    Sloppy = 3
}

internal static class AccuracyPresetTable
{
    // Fraction of MaxJudgmentMs used as the upper bound for uniform-random delay.
    private static readonly double[] Fractions = { 0.0, 0.20, 0.50, 0.85 };

    public static double GetMaxDelaySeconds(AccuracyPreset preset, double maxJudgmentMs)
    {
        if (maxJudgmentMs <= 0.0) return 0.0;
        int idx = (int)preset;
        if (idx < 0 || idx >= Fractions.Length) return 0.0;
        return (Fractions[idx] * maxJudgmentMs) / 1000.0;
    }

    /// <summary>Generic labels suitable for any profile.</summary>
    public static string[] GenericLabels => new[]
    {
        "Max accuracy", "High accuracy", "Human-like", "Sloppy"
    };
}
```

- [ ] **Step 4: Run tests to verify pass**

Run:
```bash
dotnet test robeatspro.Tests/ --filter AccuracyPresetTests
```
Expected: all 5 tests pass. **Note:** the main project will not yet compile because `MacroEngine.cs` and `GamesTab.cs` still call the old signature. That's expected — we fix them in later tasks. To run tests only, run from the test project directory:
```bash
cd robeatspro.Tests && dotnet test
cd ..
```
If the tests cannot run because of main-project compile errors, temporarily comment out the two call sites in `MacroEngine.cs` line 83 and `GamesTab.cs` line ~172 to unblock — they will be rewritten in later tasks anyway.

- [ ] **Step 5: Commit**

```bash
git add robeatspro/AccuracyPreset.cs robeatspro.Tests/AccuracyPresetTests.cs
git commit -m "feat(detection): generalize accuracy preset to work on any profile"
```

---

## Task 4: Pure per-lane detection state machine with tests

**Files:**
- Create: `robeatspro/DetectionLane.cs`
- Create: `robeatspro.Tests/DetectionStateMachineTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `robeatspro.Tests/DetectionStateMachineTests.cs`:
```csharp
using SoulBeatsPro;

namespace RoBeatsPro.Tests;

/// <summary>
/// DetectionLane is a pure state machine. It returns actions (Press / Release /
/// None) in response to per-frame "present" booleans and a timestamp. No IO.
/// </summary>
public class DetectionStateMachineTests
{
    private DetectionLane NewLane() => new(minPressDurationSec: 0.020, cleanFrames: 3);

    [Fact]
    public void rising_edge_returns_press()
    {
        var lane = NewLane();
        Assert.Equal(LaneAction.None, lane.Update(present: false, now: 0.000));
        Assert.Equal(LaneAction.Press, lane.Update(present: true, now: 0.001));
    }

    [Fact]
    public void continued_presence_returns_none()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001); // press
        Assert.Equal(LaneAction.None, lane.Update(true, 0.050));
        Assert.Equal(LaneAction.None, lane.Update(true, 0.100));
    }

    [Fact]
    public void release_requires_N_clean_frames_and_min_press_duration()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001); // press at 0.001

        // Clean frames start, but min-press-duration not yet elapsed
        Assert.Equal(LaneAction.None, lane.Update(false, 0.005));
        Assert.Equal(LaneAction.None, lane.Update(false, 0.010));
        Assert.Equal(LaneAction.None, lane.Update(false, 0.015));

        // After 20 ms has passed AND 3 consecutive clean frames: release on the
        // NEXT frame where both conditions are satisfied.
        Assert.Equal(LaneAction.Release, lane.Update(false, 0.030));
    }

    [Fact]
    public void single_clean_frame_then_present_resets_clean_counter()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001); // press

        lane.Update(false, 0.100); // 1 clean
        lane.Update(false, 0.105); // 2 clean
        lane.Update(true,  0.110); // present -> reset
        lane.Update(false, 0.115); // 1 clean again
        lane.Update(false, 0.120); // 2 clean
        Assert.Equal(LaneAction.None, lane.Update(false, 0.125)); // 3 clean but we only release on the NEXT frame that sees clean, see spec
        // Pattern continues: release fires once all conditions met. Next frame:
        Assert.Equal(LaneAction.Release, lane.Update(false, 0.130));
    }

    [Fact]
    public void second_rising_edge_after_release_presses_again()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);  // press
        lane.Update(false, 0.030);
        lane.Update(false, 0.035);
        lane.Update(false, 0.040);
        lane.Update(false, 0.045);  // release here (min press elapsed + 3 clean frames prior)
        // Next rising edge:
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.100));
    }

    [Fact]
    public void dense_tap_stream_fires_new_press_on_each_rising_edge()
    {
        var lane = NewLane();

        // Simulate: tap 1 at 0.001 (3 frames present), gap 1 frame, tap 2 at 0.010
        lane.Update(false, 0.000);
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.001));
        lane.Update(true, 0.003);
        lane.Update(true, 0.005);
        lane.Update(false, 0.007);
        lane.Update(false, 0.008);
        lane.Update(false, 0.009);
        // After min-press elapsed + 3 clean frames, release fires
        Assert.Equal(LaneAction.Release, lane.Update(false, 0.025));
        // Next tap rises
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.030));
    }

    [Fact]
    public void reset_returns_lane_to_released_without_emitting_release()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);  // press
        lane.Reset();
        // After reset, next rising edge fires fresh press:
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.100));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test robeatspro.Tests/ --filter DetectionStateMachineTests
```
Expected: compile fails — `DetectionLane`, `LaneAction` not defined.

- [ ] **Step 3: Implement `DetectionLane.cs`**

Create `robeatspro/DetectionLane.cs`:
```csharp
namespace SoulBeatsPro;

internal enum LaneAction { None, Press, Release }

/// <summary>
/// Pure per-lane state machine. Two states: Released and Pressing. Rising edge
/// fires Press; release occurs after N consecutive clean frames AND minimum
/// press duration has elapsed. No timers, no IO — caller supplies `now`.
/// </summary>
internal sealed class DetectionLane
{
    private readonly double _minPressDurationSec;
    private readonly int _cleanFramesRequired;

    private bool _pressing;
    private bool _prevPresent;
    private double _pressedAt;
    private int _cleanFrames;

    public DetectionLane(double minPressDurationSec, int cleanFrames)
    {
        _minPressDurationSec = minPressDurationSec;
        _cleanFramesRequired = cleanFrames;
    }

    public void Reset()
    {
        _pressing = false;
        _prevPresent = false;
        _pressedAt = 0.0;
        _cleanFrames = 0;
    }

    public LaneAction Update(bool present, double now)
    {
        LaneAction action = LaneAction.None;

        if (!_pressing)
        {
            // Released -> Pressing: rising edge.
            if (present && !_prevPresent)
            {
                _pressing = true;
                _pressedAt = now;
                _cleanFrames = 0;
                action = LaneAction.Press;
            }
        }
        else
        {
            // Pressing -> Released: need N clean frames + min press duration.
            if (present) _cleanFrames = 0;
            else _cleanFrames++;

            bool enoughClean = _cleanFrames >= _cleanFramesRequired;
            bool enoughHeld = (now - _pressedAt) >= _minPressDurationSec;
            if (enoughClean && enoughHeld)
            {
                _pressing = false;
                _cleanFrames = 0;
                action = LaneAction.Release;
            }
        }

        _prevPresent = present;
        return action;
    }
}
```

- [ ] **Step 4: Run tests to verify pass**

Run:
```bash
dotnet test robeatspro.Tests/ --filter DetectionStateMachineTests
```
Expected: all 7 tests pass. If any fail, inspect the test expectation vs code — the state machine should behave exactly as the test comments describe. Fix the implementation, not the test, unless the test expectation contradicts Section 1 of the spec.

- [ ] **Step 5: Commit**

```bash
git add robeatspro/DetectionLane.cs robeatspro.Tests/DetectionStateMachineTests.cs
git commit -m "feat(detection): pure press-while-present state machine"
```

---

## Task 5: Add `CountSignatureMatches` to ScreenCapture

**Files:**
- Modify: `robeatspro/ScreenCapture.cs`

- [ ] **Step 1: Add the new method**

Open `robeatspro/ScreenCapture.cs`. After the existing `PatchHasNoteColor` method (around line 284), add:
```csharp
/// <summary>
/// Count pixels in a (2*half+1) square patch that match any entry in the
/// given color signature. This is the one-stop detection primitive used by
/// MacroEngine's press-while-present algorithm.
/// </summary>
public unsafe int CountSignatureMatches(int cx, int cy, int half, ColorSignature sig)
{
    if (sig.Entries.Count == 0) return 0;

    int x0 = Math.Max(0, cx - half);
    int y0 = Math.Max(0, cy - half);
    int x1 = Math.Min(Width - 1, cx + half);
    int y1 = Math.Min(Height - 1, cy + half);

    var data = _bmp.LockBits(
        new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

    int count = 0;
    try
    {
        int stride = data.Stride;
        byte* ptr = (byte*)data.Scan0;
        int w = x1 - x0 + 1;
        int h = y1 - y0 + 1;

        // Snapshot to locals for the inner loop.
        int entryCount = sig.Entries.Count;
        var entries = sig.Entries;

        for (int row = 0; row < h; row++)
        {
            byte* line = ptr + row * stride;
            for (int col = 0; col < w; col++)
            {
                byte b = line[col * 4];
                byte g = line[col * 4 + 1];
                byte r = line[col * 4 + 2];

                for (int e = 0; e < entryCount; e++)
                {
                    var entry = entries[e];
                    int tol = entry.Tolerance;
                    if (Math.Abs(r - entry.R) <= tol &&
                        Math.Abs(g - entry.G) <= tol &&
                        Math.Abs(b - entry.B) <= tol)
                    {
                        count++;
                        break;
                    }
                }
            }
        }
    }
    finally { _bmp.UnlockBits(data); }

    return count;
}
```

- [ ] **Step 2: Sample a pixel at a given screen coordinate**

Still in `ScreenCapture.cs`, add another helper (used by calibration capture):
```csharp
/// <summary>Read the raw BGR pixel at a single relative coordinate.</summary>
public unsafe (byte r, byte g, byte b) ReadPixel(int cx, int cy)
{
    int x = Math.Clamp(cx, 0, Width - 1);
    int y = Math.Clamp(cy, 0, Height - 1);
    var data = _bmp.LockBits(
        new Rectangle(x, y, 1, 1),
        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
        byte* p = (byte*)data.Scan0;
        return (p[2], p[1], p[0]);
    }
    finally { _bmp.UnlockBits(data); }
}
```

- [ ] **Step 3: Compile check**

Run:
```bash
dotnet build robeatspro/RoBeatsPro.csproj
```
Expected: build succeeds (may still warn about old call sites in MacroEngine/GamesTab — those are removed in later tasks, but if any are now **errors**, temporarily comment them out and note with `// TODO universal-detection`).

- [ ] **Step 4: Commit**

```bash
git add robeatspro/ScreenCapture.cs
git commit -m "feat(capture): add CountSignatureMatches + ReadPixel"
```

---

## Task 6: Rewrite ConfigManager profile list + migration

**Files:**
- Modify: `robeatspro/ConfigManager.cs`
- Create: `robeatspro.Tests/MigrationTests.cs`

- [ ] **Step 1: Write migration tests first**

Create `robeatspro.Tests/MigrationTests.cs`:
```csharp
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
          "gameMode": { "activeGame": "Funky Friday" },
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
        // Previous schema: profiles = { funkyFriday: {...}, robeats: {...} }
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

        // FF signatures: each lane has 2 entries (white head + gray body) derived from whiteGray settings.
        foreach (var s1 in ff.Signatures)
        {
            Assert.Equal(2, s1.Entries.Count);
            Assert.Equal(245, s1.Entries[0].R); // mid of 240..255
        }
        // RoBeats signatures: each lane has 2 entries (picked tap + picked hold).
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test robeatspro.Tests/ --filter MigrationTests
```
Expected: compile errors — `Migrate()`, `ActiveProfileName`, `Profiles` (as list), `profiles_legacy_twoProfile` don't exist.

- [ ] **Step 3: Rewrite the settings POCOs in ConfigManager.cs**

In `robeatspro/ConfigManager.cs`:

1. **Delete** `NoteColorSettings`, `HoldColorSettings`, `WhiteGraySettings`, `DetectionSettings`, `GameProfile`, `ProfilesSettings` classes (lines ~39–175).

2. **Rewrite `GameModeSettings`** (around line 157) to:
```csharp
internal sealed class GameModeSettings
{
    [JsonPropertyName("activeProfileName")] public string ActiveProfileName { get; set; } = "Funky Friday";

    /// <summary>Legacy: old "activeGame" key. Read-only, used during migration.</summary>
    [JsonPropertyName("activeGame")] public string? LegacyActiveGame { get; set; }
}
```

3. **Rewrite `AppSettings`** (around line 177) to:
```csharp
internal sealed class AppSettings
{
    [JsonPropertyName("keybinds")] public KeybindSettings Keybinds { get; set; } = new();
    [JsonPropertyName("theme")] public ThemeSettings Theme { get; set; } = new();
    [JsonPropertyName("gameMode")] public GameModeSettings GameMode { get; set; } = new();
    [JsonPropertyName("profiles")] public List<Profile> Profiles { get; set; } = new();

    // ── Legacy fields (one-shot migration reads) ─────────────
    /// <summary>Pre-profiles flat-detection layout.</summary>
    [JsonPropertyName("detection")] public JsonElement? LegacyDetection { get; set; }
    [JsonPropertyName("tuning")]    public TuningSettings? LegacyTuning { get; set; }
    /// <summary>Two-profile layout (previous iteration).</summary>
    [JsonPropertyName("profiles_legacy_twoProfile")]
    public JsonElement? LegacyTwoProfile { get; set; }

    /// <summary>Run once after deserialize. Idempotent.</summary>
    public void Migrate()
    {
        bool changed = false;

        // Case A: legacy "activeGame" key → translate to activeProfileName.
        if (string.IsNullOrEmpty(GameMode.ActiveProfileName) || GameMode.ActiveProfileName == "Funky Friday")
        {
            if (!string.IsNullOrEmpty(GameMode.LegacyActiveGame))
            {
                GameMode.ActiveProfileName = GameMode.LegacyActiveGame == "robeats" ? "RoBeats" : "Funky Friday";
                GameMode.LegacyActiveGame = null;
                changed = true;
            }
        }

        // Case B: two-profile schema present.
        if (LegacyTwoProfile.HasValue && Profiles.Count == 0)
        {
            MigrateTwoProfile(LegacyTwoProfile.Value);
            LegacyTwoProfile = null;
            changed = true;
        }

        // Case C: flat legacy detection/tuning.
        if ((LegacyDetection.HasValue || LegacyTuning != null) && Profiles.Count == 0)
        {
            SeedBuiltInProfiles();
            ApplyLegacyFlat();
            LegacyDetection = null;
            LegacyTuning = null;
            changed = true;
        }

        // Case D: completely fresh install — seed built-ins.
        if (Profiles.Count == 0)
        {
            SeedBuiltInProfiles();
            changed = true;
        }
    }

    private void SeedBuiltInProfiles()
    {
        Profiles.Add(new Profile { Name = "Funky Friday", IsBuiltIn = true, MaxJudgmentMs = 140 });
        Profiles.Add(new Profile { Name = "RoBeats",      IsBuiltIn = true, MaxJudgmentMs = 150 });
    }

    private void MigrateTwoProfile(JsonElement el)
    {
        if (el.TryGetProperty("funkyFriday", out var ff))
            Profiles.Add(BuildFromLegacyTwoProfile("Funky Friday", ff, whiteGray: true, maxJudgmentMs: 140));
        if (el.TryGetProperty("robeats", out var rb))
            Profiles.Add(BuildFromLegacyTwoProfile("RoBeats", rb, whiteGray: false, maxJudgmentMs: 150));
        GameMode.ActiveProfileName = GameMode.LegacyActiveGame == "robeats" ? "RoBeats" : "Funky Friday";
    }

    private static Profile BuildFromLegacyTwoProfile(string name, JsonElement src, bool whiteGray, double maxJudgmentMs)
    {
        var p = new Profile { Name = name, IsBuiltIn = true, MaxJudgmentMs = maxJudgmentMs };

        // Per-lane signatures derived from legacy detection settings.
        var sig = new ColorSignature();
        if (whiteGray && src.TryGetProperty("detection", out var d) && d.TryGetProperty("whiteGray", out var wg))
        {
            int whiteMin = wg.TryGetProperty("whiteMin", out var wm) ? wm.GetInt32() : 240;
            int grayMin  = wg.TryGetProperty("grayMin",  out var gn) ? gn.GetInt32() : 130;
            int grayMax  = wg.TryGetProperty("grayMax",  out var gx) ? gx.GetInt32() : 170;
            int whiteMid = (whiteMin + 255) / 2;
            int grayMid  = (grayMin + grayMax) / 2;
            int grayHalfRange = (grayMax - grayMin) / 2;
            sig.Entries.Add(new ColorSignatureEntry(whiteMid, whiteMid, whiteMid, 255 - whiteMid));
            sig.Entries.Add(new ColorSignatureEntry(grayMid, grayMid, grayMid, Math.Max(grayHalfRange, 8)));
        }
        else if (!whiteGray && src.TryGetProperty("detection", out var d2))
        {
            if (d2.TryGetProperty("noteColor", out var nc))
            {
                int r = nc.TryGetProperty("pickedR", out var pr) && pr.GetInt32() >= 0 ? pr.GetInt32() : 255;
                int g = nc.TryGetProperty("pickedG", out var pg) && pg.GetInt32() >= 0 ? pg.GetInt32() : 215;
                int b = nc.TryGetProperty("pickedB", out var pb) && pb.GetInt32() >= 0 ? pb.GetInt32() : 0;
                sig.Entries.Add(new ColorSignatureEntry(r, g, b, 15));
            }
            if (d2.TryGetProperty("holdColor", out var hc))
            {
                int r = hc.TryGetProperty("pickedR", out var pr) && pr.GetInt32() >= 0 ? pr.GetInt32() : 160;
                int g = hc.TryGetProperty("pickedG", out var pg) && pg.GetInt32() >= 0 ? pg.GetInt32() : 120;
                int b = hc.TryGetProperty("pickedB", out var pb) && pb.GetInt32() >= 0 ? pb.GetInt32() : 40;
                sig.Entries.Add(new ColorSignatureEntry(r, g, b, 25));
            }
        }
        p.Signatures = new[] { Clone(sig), Clone(sig), Clone(sig), Clone(sig) };

        if (src.TryGetProperty("tap", out var tap))  p.Tap  = JsonSerializer.Deserialize<int[][]>(tap.GetRawText()) ?? Array.Empty<int[]>();
        if (src.TryGetProperty("hold", out var hold)) p.Hold = JsonSerializer.Deserialize<int[][]>(hold.GetRawText()) ?? Array.Empty<int[]>();
        if (src.TryGetProperty("tuning", out var t))   p.Tuning = JsonSerializer.Deserialize<TuningSettings>(t.GetRawText()) ?? new();
        if (src.TryGetProperty("accuracyPreset", out var ap)) p.AccuracyPreset = (AccuracyPreset)ap.GetInt32();

        return p;
    }

    private static ColorSignature Clone(ColorSignature s)
    {
        var c = new ColorSignature();
        foreach (var e in s.Entries) c.Entries.Add(new ColorSignatureEntry(e.R, e.G, e.B, e.Tolerance));
        return c;
    }

    private void ApplyLegacyFlat()
    {
        // Find the profile that matches the legacy active game; apply tuning there.
        var target = Profiles.Find(p => p.Name == GameMode.ActiveProfileName) ?? Profiles[0];
        if (LegacyTuning != null) target.Tuning = LegacyTuning;
        // LegacyDetection is a JsonElement; we don't materialize it into signatures here —
        // the user will re-calibrate. The goal is just to preserve tuning numbers.
    }
}
```

4. **Rewrite `ConfigManager` accessors** (around lines 223–242). Replace:
```csharp
public KeybindSettings Keybinds => _settings.Keybinds;
public ThemeSettings Theme => _settings.Theme;
public GameModeSettings GameMode => _settings.GameMode;
public ProfilesSettings Profiles => _settings.Profiles;

public GameProfile ActiveProfile =>
    GameMode.ActiveGame == "funkyFriday" ? Profiles.FunkyFriday : Profiles.RoBeats;

public DetectionSettings Detection => ActiveProfile.Detection;
public TuningSettings Tuning => ActiveProfile.Tuning;

public bool IsWhiteGrayMode => GameMode.ActiveGame == "funkyFriday";
```
with:
```csharp
public KeybindSettings Keybinds => _settings.Keybinds;
public ThemeSettings Theme => _settings.Theme;
public GameModeSettings GameMode => _settings.GameMode;
public List<Profile> Profiles => _settings.Profiles;

public Profile ActiveProfile
{
    get
    {
        var p = _settings.Profiles.Find(x => x.Name == _settings.GameMode.ActiveProfileName);
        return p ?? _settings.Profiles[0];
    }
}

public TuningSettings Tuning => ActiveProfile.Tuning;
```

5. **Replace `MigrateLegacyIfNeeded`** (around line 283): delete the whole method. Replace the `LoadSettings()` body with:
```csharp
public void LoadSettings()
{
    if (!File.Exists(SettingsPath))
    {
        _settings = new AppSettings();
        _settings.Migrate();
        SaveSettings();
        return;
    }
    try
    {
        var json = File.ReadAllText(SettingsPath);
        _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        _settings.Migrate();
        // Remove old coords.json if present.
        if (File.Exists(CoordsPath))
        {
            try { File.Delete(CoordsPath); } catch { }
        }
        SaveSettings();
    }
    catch { _settings = new AppSettings(); _settings.Migrate(); }
}
```

6. **Update `LoadCoords` / `SaveCoords`**: no change needed — they already read `ActiveProfile.Tap` / `ActiveProfile.Hold`, which still exist on the new `Profile` type.

7. **Add profile-management helpers** at the bottom of `ConfigManager` class (before the closing brace):
```csharp
public Profile AddProfile(string name)
{
    if (Profiles.Any(p => p.Name == name))
        throw new InvalidOperationException($"Profile '{name}' already exists");
    var p = new Profile { Name = name, IsBuiltIn = false };
    Profiles.Add(p);
    SaveSettings();
    return p;
}

public void DeleteProfile(string name)
{
    var p = Profiles.FirstOrDefault(x => x.Name == name);
    if (p == null) return;
    if (p.IsBuiltIn) throw new InvalidOperationException("Cannot delete a built-in profile");
    Profiles.Remove(p);
    if (_settings.GameMode.ActiveProfileName == name && Profiles.Count > 0)
        _settings.GameMode.ActiveProfileName = Profiles[0].Name;
    SaveSettings();
}

public Profile DuplicateProfile(string name, string newName)
{
    var src = Profiles.First(p => p.Name == name);
    var json = JsonSerializer.Serialize(src);
    var copy = JsonSerializer.Deserialize<Profile>(json)!;
    copy.Name = newName;
    copy.IsBuiltIn = false;
    Profiles.Add(copy);
    SaveSettings();
    return copy;
}

public void SetActiveProfile(string name)
{
    if (!Profiles.Any(p => p.Name == name)) return;
    _settings.GameMode.ActiveProfileName = name;
    SaveSettings();
}
```

- [ ] **Step 4: Run migration tests to verify pass**

Run:
```bash
dotnet test robeatspro.Tests/ --filter MigrationTests
```
Expected: 3/3 pass. The main project still won't compile — `MacroEngine`, `GamesTab`, `ColorsTab`, `CalibrationTab`, `DebugForm` all reference removed types (`DetectionSettings`, `NoteColorSettings`, `IsWhiteGrayMode`, etc.). That's OK; Tasks 7–11 fix them. For now the test project builds because `InternalsVisibleTo` only exposes references to used types.

If tests fail to run at all because the test project references broken main-project code: add a temporary using-alias compile shim in `ConfigManager.cs` by recreating empty placeholder types at the bottom of the file **only** for types that other files still reference:
```csharp
// TEMP shims until Tasks 7–11 rewrite consumers.
internal sealed class DetectionSettings { public WhiteGraySettings WhiteGray { get; set; } = new(); public NoteColorSettings NoteColor { get; set; } = new(); public HoldColorSettings HoldColor { get; set; } = new(); }
internal sealed class NoteColorSettings { public int MinR, MinG, MaxB, PickedR = -1, PickedG = -1, PickedB = -1; }
internal sealed class HoldColorSettings { public int MinR, MaxR, MinG, MaxG, MaxB, MinRG, PickedR = -1, PickedG = -1, PickedB = -1; }
internal sealed class WhiteGraySettings { public int WhiteMin, GrayMin, GrayMax; }
```
These shims get removed in Task 11.

- [ ] **Step 5: Commit**

```bash
git add robeatspro/ConfigManager.cs robeatspro.Tests/MigrationTests.cs
git commit -m "feat(config): profile list + migration paths (new, two-profile, flat legacy)"
```

---

## Task 7: Rewrite MacroEngine with press-while-present

**Files:**
- Modify: `robeatspro/MacroEngine.cs` (major rewrite)

- [ ] **Step 1: Replace engine state fields**

Open `robeatspro/MacroEngine.cs`. Replace the private state block (lines 49–61) with:
```csharp
// Private state
private readonly DetectionLane[] _lanes = new DetectionLane[4];
private readonly double[] _scheduledPressAt = new double[4];
private readonly double[] _tapReleaseAt = new double[4]; // used only for preset-delayed taps' minimum held duration
private readonly int[] _matchCountsDebug = new int[4];
private readonly Random _rng = new();
private AccuracyPreset _accuracyPreset;
private double _accuracyMaxDelay;    // seconds
private double _maxJudgmentMs;
private double _lastToggle;
private Thread? _thread;
private volatile bool _stopRequested;
private ColorSignature[] _signatures = new ColorSignature[4];
private double _minPressDurationSec;
private int _cleanFrames;
```

- [ ] **Step 2: Update public state exposed to the debug form**

Replace the public arrays block (around lines 34–44) with:
```csharp
public enum LaneState { Released, Pressing }

public LaneState[] States { get; } = new LaneState[4];
public int[] MatchCounts => _matchCountsDebug;
public bool Active { get; set; } = true;
public int Fps { get; private set; }
public bool Running { get; private set; }

public Point[] TapPixels { get; private set; } = null!;
public Point[] HoldPixels { get; private set; } = null!;

// Kept for DebugForm compatibility with older UI — always zero under the new engine.
public int[] NoteCounts => _matchCountsDebug;
public int[] TapHoldCounts { get; } = new int[4];
public int[] HoldZoneCounts { get; } = new int[4];
public bool[] HoldIncoming { get; } = new bool[4];
public bool[] HoldSawTail { get; } = new bool[4];
public bool WhiteGrayMode => false;
```

- [ ] **Step 3: Replace `Start()` body**

Replace the existing `Start()` body (lines 65–89) with:
```csharp
public void Start()
{
    if (Running) return;
    CurrentInstance = this;

    var profile = ConfigManager.Instance.ActiveProfile;
    (TapPixels, HoldPixels) = ConfigManager.Instance.LoadCoords();
    NativeApi.UpdateLaneScans(ConfigManager.Instance.Keybinds.LaneKeys);

    var t = profile.Tuning;
    _sampleHalf = t.SampleHalf;
    _minPixels = t.MinPixels;
    _cleanFrames = t.CleanFrames;
    _minPressDurationSec = t.MinPressDurationMs / 1000.0;
    _toggleDelay = t.ToggleDelay;

    _accuracyPreset = profile.AccuracyPreset;
    _maxJudgmentMs = profile.MaxJudgmentMs;
    _accuracyMaxDelay = AccuracyPresetTable.GetMaxDelaySeconds(_accuracyPreset, _maxJudgmentMs);

    for (int i = 0; i < 4; i++)
    {
        _signatures[i] = profile.Signatures.Length > i ? profile.Signatures[i] : new ColorSignature();
        _lanes[i] = new DetectionLane(_minPressDurationSec, _cleanFrames);
    }

    _stopRequested = false;
    Running = true;
    _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
    _thread.Start();
}
```
Also remove the now-unused field `_whiteGrayMode` (and its accessor), and remove fields `_tapKeyDuration`, `_holdReleaseCooldown`, `_holdArmGrace`, `_holdReleaseGrace` from the private tuning block at top of class.

- [ ] **Step 4: Replace the `Loop()` body**

Replace the entire `Loop()` method (lines 93–358) with:
```csharp
private void Loop()
{
    var allPts = TapPixels.Concat(HoldPixels).ToArray();
    int capLeft   = allPts.Min(p => p.X) - _sampleHalf - 1;
    int capTop    = allPts.Min(p => p.Y) - _sampleHalf - 1;
    int capRight  = allPts.Max(p => p.X) + _sampleHalf + 1;
    int capBottom = allPts.Max(p => p.Y) + _sampleHalf + 1;
    int capW = capRight - capLeft;
    int capH = capBottom - capTop;

    var tapRel = TapPixels.Select(p => new Point(p.X - capLeft, p.Y - capTop)).ToArray();
    using var capture = new ScreenCapture(capLeft, capTop, capW, capH);

    Array.Fill(States, LaneState.Released);
    Array.Fill(_scheduledPressAt, 0.0);
    Array.Fill(_tapReleaseAt, 0.0);
    for (int i = 0; i < 4; i++) _lanes[i].Reset();
    Active = true;
    _lastToggle = 0;

    var sw = Stopwatch.StartNew();
    int frameCount = 0;
    double fpsTimer = sw.Elapsed.TotalSeconds;

    while (!_stopRequested)
    {
        double now = sw.Elapsed.TotalSeconds;

        frameCount++;
        if (now - fpsTimer >= 1.0) { Fps = frameCount; frameCount = 0; fpsTimer = now; }

        // Pause / resume
        if (NativeApi.IsKeyDown(ConfigManager.Instance.Keybinds.Pause) && now - _lastToggle > _toggleDelay)
        {
            Active = !Active;
            _lastToggle = now;
            if (!Active) { ReleaseAllKeys(); }
        }
        if (!Active) { Thread.Sleep(10); continue; }

        // Service scheduled (delayed) presses from accuracy preset.
        for (int i = 0; i < 4; i++)
        {
            if (_scheduledPressAt[i] > 0 && now >= _scheduledPressAt[i])
            {
                NativeApi.PressKey(i);
                States[i] = LaneState.Pressing;
                _tapReleaseAt[i] = now + _minPressDurationSec;
                _scheduledPressAt[i] = 0.0;
            }
        }

        try { capture.Grab(); } catch { Thread.Sleep(1); continue; }

        for (int i = 0; i < 4; i++)
        {
            int matchCount = capture.CountSignatureMatches(tapRel[i].X, tapRel[i].Y, _sampleHalf, _signatures[i]);
            _matchCountsDebug[i] = matchCount;
            bool present = matchCount >= _minPixels;

            var action = _lanes[i].Update(present, now);
            switch (action)
            {
                case LaneAction.Press:
                    if (_accuracyMaxDelay <= 0.0 || _scheduledPressAt[i] > 0)
                    {
                        // Zero-delay preset OR a previous scheduled press is still pending
                        // (safety: fire it now, then the lane machine already transitioned).
                        NativeApi.PressKey(i);
                        States[i] = LaneState.Pressing;
                        _tapReleaseAt[i] = now + _minPressDurationSec;
                        _scheduledPressAt[i] = 0.0;
                    }
                    else
                    {
                        double delay = _rng.NextDouble() * _accuracyMaxDelay;
                        _scheduledPressAt[i] = now + delay;
                        // DetectionLane has already marked itself as "pressing"; we simply
                        // don't send the physical press until the scheduled time.
                    }
                    break;
                case LaneAction.Release:
                    NativeApi.ReleaseKey(i);
                    States[i] = LaneState.Released;
                    _tapReleaseAt[i] = 0.0;
                    break;
            }
        }

        Thread.SpinWait(100);
    }

    ReleaseAllKeys();
    Running = false;
    OnStopped?.Invoke();
}

private void ReleaseAllKeys()
{
    for (int i = 0; i < 4; i++)
    {
        if (States[i] == LaneState.Pressing) NativeApi.ReleaseKey(i);
        _scheduledPressAt[i] = 0.0;
        _tapReleaseAt[i] = 0.0;
        States[i] = LaneState.Released;
        _lanes[i]?.Reset();
    }
}
```

- [ ] **Step 5: Build the main project**

Run:
```bash
dotnet build robeatspro/RoBeatsPro.csproj
```
Expected: errors in `ColorsTab.cs`, `CalibrationTab.cs`, `GamesTab.cs`, `DebugForm.cs` (they still reference the old `Detection` property / old `LaneState.Tapped` / `LaneState.Holding`). These are fixed in Tasks 8–11. For now, if the compile errors block the whole solution, comment out offending methods/controls in those files as a triage — they will be rewritten or deleted.

- [ ] **Step 6: Commit**

```bash
git add robeatspro/MacroEngine.cs
git commit -m "feat(engine): press-while-present detection replaces two-mode branching"
```

---

## Task 8: Update calibration to capture signatures

**Files:**
- Modify: `robeatspro/CalibrationTab.cs` (the capture path)
- Modify: `robeatspro/CalibrationForm.cs` (if it hosts the sample-capture click handler)

- [ ] **Step 1: Find the current capture path**

Run:
```bash
dotnet run --project robeatspro -- --help 2>/dev/null || true
```
Then search for where the current `PickedR/PickedG/PickedB` values are written. Use Grep:
```bash
grep -n "PickedR\|PickedG\|PickedB" robeatspro/*.cs
```
Expected: hits in `CalibrationTab.cs` and/or `CalibrationForm.cs` and `ColorsTab.cs`. Note each file and line.

- [ ] **Step 2: Add a signature-capture helper**

At the bottom of `robeatspro/SignatureMatcher.cs`, append:
```csharp
internal static class SignatureCapture
{
    /// <summary>
    /// Given N sampled pixels, produce a ColorSignatureEntry whose (r,g,b) is
    /// the mean and whose tolerance is max(floor, max channel deviation from mean).
    /// </summary>
    public static ColorSignatureEntry BuildEntry(IReadOnlyList<(byte r, byte g, byte b)> samples, int floorTolerance = 8)
    {
        if (samples.Count == 0) return new ColorSignatureEntry(0, 0, 0, floorTolerance);
        int sumR = 0, sumG = 0, sumB = 0;
        foreach (var (r, g, b) in samples) { sumR += r; sumG += g; sumB += b; }
        int meanR = sumR / samples.Count;
        int meanG = sumG / samples.Count;
        int meanB = sumB / samples.Count;
        int maxDev = 0;
        foreach (var (r, g, b) in samples)
        {
            maxDev = Math.Max(maxDev, Math.Abs(r - meanR));
            maxDev = Math.Max(maxDev, Math.Abs(g - meanG));
            maxDev = Math.Max(maxDev, Math.Abs(b - meanB));
        }
        int tolerance = Math.Max(floorTolerance, maxDev);
        return new ColorSignatureEntry(meanR, meanG, meanB, tolerance);
    }
}
```

- [ ] **Step 3: Add a unit test for `BuildEntry`**

Append to `robeatspro.Tests/SignatureMatcherTests.cs`:
```csharp
[Fact]
public void build_entry_uses_mean_with_floor_tolerance()
{
    var samples = new List<(byte, byte, byte)>
    {
        ((byte)200, (byte)150, (byte)100),
        ((byte)202, (byte)152, (byte)98),
        ((byte)198, (byte)148, (byte)102),
    };
    var entry = SignatureCapture.BuildEntry(samples, floorTolerance: 8);
    Assert.Equal(200, entry.R);
    Assert.Equal(150, entry.G);
    Assert.Equal(100, entry.B);
    Assert.Equal(8, entry.Tolerance); // all deviations <= 2, floor applies
}

[Fact]
public void build_entry_grows_tolerance_with_variance()
{
    var samples = new List<(byte, byte, byte)>
    {
        ((byte)200, (byte)100, (byte)50),
        ((byte)220, (byte)100, (byte)50),
        ((byte)180, (byte)100, (byte)50),
    };
    var entry = SignatureCapture.BuildEntry(samples, floorTolerance: 8);
    Assert.Equal(200, entry.R);
    Assert.Equal(20, entry.Tolerance); // max deviation is 20
}
```
Run:
```bash
dotnet test robeatspro.Tests/ --filter SignatureMatcher
```
Expected: 9 pass.

- [ ] **Step 4: Replace the calibration capture logic**

In `CalibrationTab.cs` (or wherever the current code writes `PickedR/PickedG/PickedB`), the flow currently reads one pixel and writes `nc.PickedR = ...`.

Replace that single-pixel capture with a multi-frame capture. The calibration flow has two capture steps per lane (tap point, hold point). For each:
1. Grab 10 frames over ~200ms via a short loop.
2. Build a `ColorSignatureEntry` via `SignatureCapture.BuildEntry`.
3. Append it to `ConfigManager.Instance.ActiveProfile.Signatures[laneIndex].Entries`.

Inline the replacement — concrete template (adapt the naming to match the file):
```csharp
private async Task CaptureSampleForLane(int laneIndex, Point screenPoint, ScreenCapture cap, bool isHoldSample)
{
    var samples = new List<(byte r, byte g, byte b)>();
    for (int frame = 0; frame < 10; frame++)
    {
        cap.Grab();
        var px = cap.ReadPixel(screenPoint.X - cap.Left, screenPoint.Y - cap.Top);
        samples.Add(px);
        await Task.Delay(20);
    }

    var entry = SignatureCapture.BuildEntry(samples, floorTolerance: 8);
    var sig = ConfigManager.Instance.ActiveProfile.Signatures[laneIndex];

    // Tap sample -> entry index 0. Hold sample -> entry index 1.
    int idx = isHoldSample ? 1 : 0;
    if (sig.Entries.Count <= idx) sig.Entries.Add(entry);
    else sig.Entries[idx] = entry;

    ConfigManager.Instance.SaveSettings();
}
```
**Note:** if the existing calibration code is synchronous (no async), skip the `await Task.Delay` — sample 10 frames back-to-back without delay. The variance from frame-to-frame AA jitter alone is enough to set a reasonable tolerance floor.

- [ ] **Step 5: Allow skipping the hold capture**

In the calibration UX, the "hold sample" step is optional for games with same-color holds (osu!mania). Add a "Skip hold" button to the hold-capture step that simply moves on without appending entry index 1. If entry 1 is already present from a previous calibration, leave it.

Concretely: add a button wherever the hold-capture step is initiated. On click, advance to the next lane without calling `CaptureSampleForLane` for the hold.

- [ ] **Step 6: Manual smoke test**

Build and run:
```bash
dotnet build robeatspro/RoBeatsPro.csproj
dotnet run --project robeatspro
```
Open the app, click into Calibration, run the flow for one lane on the active profile. Open `%LOCALAPPDATA%/bigbart/settings.json` after calibration and verify the active profile's `signatures[laneIndex].entries` has the new `{r,g,b,tolerance}` populated.

If the compile still fails due to old `Detection.NoteColor.PickedR = ...` code: remove those assignments — the new flow replaces them.

- [ ] **Step 7: Commit**

```bash
git add robeatspro/SignatureMatcher.cs robeatspro/CalibrationTab.cs robeatspro/CalibrationForm.cs robeatspro.Tests/SignatureMatcherTests.cs
git commit -m "feat(calibration): capture multi-frame signature entries per lane"
```

---

## Task 9: Build the new ProfilesTab

**Files:**
- Create: `robeatspro/ProfilesTab.cs`

- [ ] **Step 1: Create the file**

Create `robeatspro/ProfilesTab.cs` with:
```csharp
namespace SoulBeatsPro;

internal sealed class ProfilesTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);
    private readonly ListBox _list;
    private readonly Button _addBtn;
    private readonly Button _duplicateBtn;
    private readonly Button _deleteBtn;
    private readonly Button _renameBtn;
    private readonly Button _activateBtn;
    private readonly ComboBox _accuracyCombo;
    private readonly NumericUpDown _maxJudgmentInput;
    private readonly Label _judgmentLabel;

    public event Action? ActiveProfileChanged;

    public ProfilesTab()
    {
        Dock = DockStyle.Fill;
        Font = RetroFont;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);

        var grpList = new GroupBox
        {
            Text = "Profiles",
            Location = new Point(12, 8),
            Size = new Size(360, 240),
            Font = RetroFont,
        };
        _list = new ListBox
        {
            Location = new Point(10, 22),
            Size = new Size(220, 200),
            Font = RetroFont,
        };
        _list.SelectedIndexChanged += (_, _) => RefreshButtons();
        grpList.Controls.Add(_list);

        _activateBtn = MakeButton("Set active", new Point(238, 22));
        _activateBtn.Click += (_, _) => ActivateSelected();
        grpList.Controls.Add(_activateBtn);

        _addBtn = MakeButton("+ Add", new Point(238, 58));
        _addBtn.Click += (_, _) => AddProfile();
        grpList.Controls.Add(_addBtn);

        _duplicateBtn = MakeButton("Duplicate", new Point(238, 94));
        _duplicateBtn.Click += (_, _) => DuplicateSelected();
        grpList.Controls.Add(_duplicateBtn);

        _renameBtn = MakeButton("Rename", new Point(238, 130));
        _renameBtn.Click += (_, _) => RenameSelected();
        grpList.Controls.Add(_renameBtn);

        _deleteBtn = MakeButton("Delete", new Point(238, 166));
        _deleteBtn.Click += (_, _) => DeleteSelected();
        grpList.Controls.Add(_deleteBtn);

        Controls.Add(grpList);

        var grpAccuracy = new GroupBox
        {
            Text = "Active profile settings",
            Location = new Point(12, 260),
            Size = new Size(360, 100),
            Font = RetroFont,
        };
        var accLabel = new Label { Text = "Accuracy:", Location = new Point(10, 26), AutoSize = true };
        grpAccuracy.Controls.Add(accLabel);
        _accuracyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(78, 22),
            Size = new Size(160, 22),
            Font = RetroFont,
        };
        _accuracyCombo.Items.AddRange(AccuracyPresetTable.GenericLabels);
        _accuracyCombo.SelectedIndexChanged += (_, _) => AccuracyChanged();
        grpAccuracy.Controls.Add(_accuracyCombo);

        _judgmentLabel = new Label { Text = "Safe window (ms):", Location = new Point(10, 58), AutoSize = true };
        grpAccuracy.Controls.Add(_judgmentLabel);
        _maxJudgmentInput = new NumericUpDown
        {
            Location = new Point(130, 54),
            Size = new Size(70, 22),
            Font = RetroFont,
            Minimum = 30, Maximum = 300, DecimalPlaces = 0, Increment = 5,
        };
        _maxJudgmentInput.ValueChanged += (_, _) => JudgmentChanged();
        grpAccuracy.Controls.Add(_maxJudgmentInput);

        Controls.Add(grpAccuracy);

        RefreshList();
    }

    private static Button MakeButton(string text, Point loc) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Standard,
        Font = RetroFont,
        Size = new Size(110, 28),
        Location = loc,
    };

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in ConfigManager.Instance.Profiles)
        {
            string label = p.Name;
            if (p.Name == ConfigManager.Instance.GameMode.ActiveProfileName) label = "★ " + label;
            if (p.IsBuiltIn) label += " [built-in]";
            _list.Items.Add(label);
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
        RefreshButtons();
        RefreshActiveSettings();
    }

    private string? GetSelectedName()
    {
        int idx = _list.SelectedIndex;
        if (idx < 0) return null;
        return ConfigManager.Instance.Profiles[idx].Name;
    }

    private void RefreshButtons()
    {
        var name = GetSelectedName();
        var p = name == null ? null : ConfigManager.Instance.Profiles.Find(x => x.Name == name);
        _deleteBtn.Enabled = p != null && !p.IsBuiltIn;
        _renameBtn.Enabled = p != null;
        _duplicateBtn.Enabled = p != null;
        _activateBtn.Enabled = p != null && name != ConfigManager.Instance.GameMode.ActiveProfileName;
    }

    private void RefreshActiveSettings()
    {
        var p = ConfigManager.Instance.ActiveProfile;
        _accuracyCombo.SelectedIndex = (int)p.AccuracyPreset;
        _maxJudgmentInput.Value = (decimal)Math.Clamp(p.MaxJudgmentMs, 30, 300);
    }

    private void ActivateSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        ConfigManager.Instance.SetActiveProfile(name);
        ActiveProfileChanged?.Invoke();
        RefreshList();
    }

    private void AddProfile()
    {
        var name = InputBox.Show("New profile name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { ConfigManager.Instance.AddProfile(name.Trim()); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        RefreshList();
    }

    private void DuplicateSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        var newName = InputBox.Show($"Name for copy of '{name}':", defaultText: name + " copy");
        if (string.IsNullOrWhiteSpace(newName)) return;
        try { ConfigManager.Instance.DuplicateProfile(name, newName.Trim()); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        RefreshList();
    }

    private void RenameSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        var newName = InputBox.Show($"Rename '{name}' to:", defaultText: name);
        if (string.IsNullOrWhiteSpace(newName) || newName == name) return;
        var p = ConfigManager.Instance.Profiles.Find(x => x.Name == name)!;
        p.Name = newName.Trim();
        if (ConfigManager.Instance.GameMode.ActiveProfileName == name)
            ConfigManager.Instance.GameMode.ActiveProfileName = newName.Trim();
        ConfigManager.Instance.SaveSettings();
        RefreshList();
    }

    private void DeleteSelected()
    {
        var name = GetSelectedName(); if (name == null) return;
        if (MessageBox.Show($"Delete profile '{name}'?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try { ConfigManager.Instance.DeleteProfile(name); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }
        RefreshList();
    }

    private void AccuracyChanged()
    {
        var p = ConfigManager.Instance.ActiveProfile;
        p.AccuracyPreset = (AccuracyPreset)_accuracyCombo.SelectedIndex;
        ConfigManager.Instance.SaveSettings();
    }

    private void JudgmentChanged()
    {
        var p = ConfigManager.Instance.ActiveProfile;
        p.MaxJudgmentMs = (double)_maxJudgmentInput.Value;
        ConfigManager.Instance.SaveSettings();
    }
}

/// <summary>Minimal modal input dialog (WinForms has none built in).</summary>
internal static class InputBox
{
    public static string? Show(string prompt, string defaultText = "")
    {
        using var f = new Form
        {
            Text = prompt,
            Width = 340, Height = 140,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false,
        };
        var tb = new TextBox { Left = 10, Top = 10, Width = 300, Text = defaultText };
        var ok = new Button { Text = "OK", Left = 140, Top = 50, DialogResult = DialogResult.OK };
        var ca = new Button { Text = "Cancel", Left = 225, Top = 50, DialogResult = DialogResult.Cancel };
        f.AcceptButton = ok; f.CancelButton = ca;
        f.Controls.AddRange(new Control[] { tb, ok, ca });
        return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }
}
```

- [ ] **Step 2: Compile check**

Run:
```bash
dotnet build robeatspro/RoBeatsPro.csproj
```
Expected: `ProfilesTab.cs` compiles. Errors elsewhere still expected (GamesTab, etc.).

- [ ] **Step 3: Commit**

```bash
git add robeatspro/ProfilesTab.cs
git commit -m "feat(ui): ProfilesTab with list/add/duplicate/rename/delete + accuracy controls"
```

---

## Task 10: Wire ProfilesTab into MainForm, delete GamesTab

**Files:**
- Modify: `robeatspro/MainForm.cs`
- Delete: `robeatspro/GamesTab.cs`

- [ ] **Step 1: Find the GamesTab integration points**

Run:
```bash
grep -n "GamesTab\|GameModeChanged" robeatspro/*.cs
```
Note every line. Typical: MainForm constructs a `GamesTab`, listens to `GameModeChanged`, adds to a TabControl.

- [ ] **Step 2: Replace `GamesTab` with `ProfilesTab` in MainForm**

In `robeatspro/MainForm.cs`:
- Change every `new GamesTab()` to `new ProfilesTab()`.
- Change `gamesTab.GameModeChanged += ...` to `profilesTab.ActiveProfileChanged += ...`.
- The subscribers restart the macro engine; that contract is unchanged.
- Rename field names (`_gamesTab` → `_profilesTab`) consistently throughout MainForm.
- Change the TabPage text from "Games" to "Profiles".

- [ ] **Step 3: Delete `GamesTab.cs`**

```bash
git rm robeatspro/GamesTab.cs
```

- [ ] **Step 4: Build**

```bash
dotnet build robeatspro/RoBeatsPro.csproj
```
Expected: MainForm now compiles. There will still be errors in `ColorsTab.cs`, `CalibrationTab.cs`, `DebugForm.cs` for references to `IsWhiteGrayMode`, `Detection.NoteColor`, etc.

- [ ] **Step 5: Commit**

```bash
git add robeatspro/MainForm.cs
git commit -m "feat(ui): swap GamesTab for ProfilesTab in MainForm"
```

---

## Task 11: Clean up remaining old-schema references

**Files:**
- Modify: `robeatspro/ColorsTab.cs`
- Modify: `robeatspro/DebugForm.cs`
- Modify: `robeatspro/ScreenCapture.cs` (rewire `GetContextPatch` + `AnalyzePatch` to use signatures, or remove methods that are no longer called)
- Modify: `robeatspro/ConfigManager.cs` (remove the temporary compile shims from Task 6 Step 4)

- [ ] **Step 1: ColorsTab — render signatures read-only (or minimal edit)**

Current `ColorsTab` edits `Detection.NoteColor` / `Detection.HoldColor`. These no longer exist. Replace with a simple viewer of `ActiveProfile.Signatures`:
- For each of 4 lanes, show a horizontal row: lane label + one color swatch per entry.
- Each swatch shows `{r,g,b,±tol}` as a tooltip.
- A "Clear signatures (re-calibrate)" button that empties `ActiveProfile.Signatures[*].Entries`.
- No direct color-picker UI; calibration is the authoritative capture path.

Concrete template — replace `ColorsTab.cs` contents with the new viewer. Only minimal example shown here; match the existing theming/fonts from other tabs:
```csharp
namespace SoulBeatsPro;

internal sealed class ColorsTab : UserControl
{
    private static readonly Font RetroFont = new("MS Sans Serif", 8f);

    public ColorsTab()
    {
        Dock = DockStyle.Fill;
        Font = RetroFont;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        RenderSignatures();
    }

    private void RenderSignatures()
    {
        Controls.Clear();
        var profile = ConfigManager.Instance.ActiveProfile;
        int y = 12;
        var header = new Label { Text = $"Active: {profile.Name}", AutoSize = true, Location = new Point(12, y), Font = new Font("MS Sans Serif", 10f, FontStyle.Bold) };
        Controls.Add(header);
        y += 26;
        for (int lane = 0; lane < 4; lane++)
        {
            var laneLabel = new Label { Text = $"Lane {lane + 1}:", AutoSize = true, Location = new Point(12, y + 4), Font = RetroFont };
            Controls.Add(laneLabel);
            var sig = profile.Signatures[lane];
            int x = 90;
            if (sig.Entries.Count == 0)
            {
                var empty = new Label { Text = "(not calibrated)", AutoSize = true, Location = new Point(x, y + 4), ForeColor = Color.FromArgb(180,180,180) };
                Controls.Add(empty);
            }
            else
            {
                for (int e = 0; e < sig.Entries.Count; e++)
                {
                    var entry = sig.Entries[e];
                    var sw = new Panel
                    {
                        Location = new Point(x, y),
                        Size = new Size(40, 22),
                        BackColor = Color.FromArgb(entry.R, entry.G, entry.B),
                        BorderStyle = BorderStyle.FixedSingle,
                    };
                    var tip = new ToolTip();
                    tip.SetToolTip(sw, $"R={entry.R} G={entry.G} B={entry.B}  ±{entry.Tolerance}");
                    Controls.Add(sw);
                    x += 50;
                }
            }
            y += 30;
        }
        var clearBtn = new Button
        {
            Text = "Clear signatures (re-calibrate)",
            Location = new Point(12, y + 10),
            Size = new Size(240, 28),
            Font = RetroFont,
            FlatStyle = FlatStyle.Standard,
        };
        clearBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Clear all signatures on the active profile?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var s in profile.Signatures) s.Entries.Clear();
            ConfigManager.Instance.SaveSettings();
            RenderSignatures();
        };
        Controls.Add(clearBtn);
    }
}
```

- [ ] **Step 2: DebugForm — update state display**

In `robeatspro/DebugForm.cs`, find every reference to `WhiteGrayMode`, `NoteCounts`, `TapHoldCounts`, `HoldZoneCounts`, `HoldIncoming`, `HoldSawTail`, `LaneState.Tapped`, `LaneState.Holding`. Replace the per-lane display with:
- Lane state: `Pressing` (green) / `Released` (gray)
- Match count: `MatchCounts[i]`
- Scheduled-press: show `_scheduledPressAt[i] > 0 ? "delayed" : ""` — expose this via a new public getter on MacroEngine if needed (`public bool[] PendingScheduled => ...`).

Drop the hold-specific state columns entirely.

- [ ] **Step 3: ScreenCapture — simplify**

In `robeatspro/ScreenCapture.cs`:
- Delete methods: `AnalyzePatchWhiteGray`, `PatchHasWhite`, `AnalyzePatchColor`, `PatchHasNoteColor`.
- Rewrite `AnalyzePatch` and `GetContextPatch` to classify pixels using `SignatureMatcher.Matches` against `ConfigManager.Instance.ActiveProfile.Signatures[i]`. Since these methods don't know which lane they're classifying for (single patch viewer), take the lane index as a parameter or the signature directly as a parameter. Prefer: add an overload that takes a `ColorSignature` and classifies any match as `PixelKind.White` (used by magnifier — color doesn't matter, just "is this a matching pixel"). Treat second-entry-onwards matches as `PixelKind.Gray` so the magnifier can still visually distinguish "tap head color" vs "hold body color."

New signature:
```csharp
public unsafe ContextPatch GetContextPatch(int cx, int cy, int contextHalf, ColorSignature sig)
```
Update all call sites (grep `GetContextPatch\|AnalyzePatch` under `robeatspro/`) to pass the lane's signature.

- [ ] **Step 4: Remove temporary shims from ConfigManager**

Open `robeatspro/ConfigManager.cs` and delete the `// TEMP shims` block added in Task 6 Step 4 (if it was used).

- [ ] **Step 5: Build**

Run:
```bash
dotnet build robeatspro/RoBeatsPro.csproj
```
Expected: clean build, 0 errors.

- [ ] **Step 6: Run all tests**

```bash
dotnet test
```
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add robeatspro/ColorsTab.cs robeatspro/DebugForm.cs robeatspro/ScreenCapture.cs robeatspro/ConfigManager.cs
git commit -m "refactor: finish universal-detection schema cleanup across UI + capture"
```

---

## Task 12: Manual end-to-end smoke test + final commit

**Files:** none (testing in the running app)

- [ ] **Step 1: Launch the app and verify migration**

Before running, back up `%LOCALAPPDATA%/bigbart/settings.json` if you have real calibration data you care about:
```bash
cp "$LOCALAPPDATA/bigbart/settings.json" "$LOCALAPPDATA/bigbart/settings.backup.json" 2>/dev/null || true
```
Then:
```bash
dotnet run --project robeatspro
```
Expected: app launches, Profiles tab shows "Funky Friday" and "RoBeats" (both `[built-in]`), the star marks whichever was previously active.

- [ ] **Step 2: FF regression**

In the app: set Funky Friday active, go to Calibration, re-calibrate using 2 Bold in FF, save. Start macro and play 3 songs. Verify:
- Zero missed taps on dense streams.
- Hold notes release cleanly between consecutive same-lane holds.

- [ ] **Step 3: RoBeats regression**

Switch active profile to RoBeats. Re-calibrate in RoBeats. Play 3 songs. Verify detection quality matches or improves on prior behavior.

- [ ] **Step 4: osu!mania new profile**

Click `+ Add` → name it "osu!mania 4K". Go to Calibration. Calibrate tap point per lane, *skip hold sample* on each lane (same-color holds). Set accuracy preset = MaxAccuracy. Open osu!mania, play 3 songs at 4K. Verify:
- Tap notes press and release.
- Hold notes press at the start and release exactly when the hold ends.

- [ ] **Step 5: Regression sweep**

Play one more FF song to confirm profile-switching didn't break the built-in.

- [ ] **Step 6: Commit test notes**

Update the spec file's testing section with actual observed judgment distributions and any preset delay adjustments. Commit with:
```bash
git add docs/superpowers/specs/2026-04-13-universal-mania-detection-design.md
git commit -m "docs: post-testing preset delay tuning notes"
```
If no adjustments were needed, skip this commit.

---

## Self-Review Summary

- **Spec coverage:** Every section of `2026-04-13-universal-mania-detection-design.md` is covered:
  - Section 1 (press-while-present) → Tasks 4, 7.
  - Section 2 (calibration) → Task 8.
  - Section 3 (profile manager) → Tasks 6, 9, 10.
  - Section 4 (accuracy presets) → Task 3.
  - Section 5 (migration) → Task 6.
  - Section 6 (testing) → Tasks 1–4, 12.
- **Placeholder scan:** All code blocks complete. Where UI code references existing patterns ("match the existing theming"), enough of the file's pattern is elsewhere in the codebase that the engineer can copy it.
- **Type consistency:** `ColorSignature`, `ColorSignatureEntry`, `Profile`, `DetectionLane`, `LaneAction`, `SignatureCapture.BuildEntry`, `GetMaxDelaySeconds(preset, maxJudgmentMs)` names are stable across tasks.
