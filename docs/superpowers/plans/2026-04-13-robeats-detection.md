# RoBeats Detection Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the RoBeats detection path to never miss (rising-edge detection), add per-game accuracy presets (Perfect/Sick-only → Sloppy, never-miss guaranteed), restructure config for per-game profiles, and surface an FPS warning indicator with a documented minimum.

**Architecture:** All work is inside `robeatspro/` (C# WinForms, .NET 8). We change `MacroEngine.cs` to trigger on note rising-edge and support a scheduled-press delay, restructure `ConfigManager.cs` to hold per-game profiles with a one-time migration, extend `GamesTab.cs` with an accuracy dropdown, and add an FPS label to `MainTab.cs`. No test project exists — verification is `dotnet build` plus manual in-app checks with the debug form and short in-game sessions.

**Tech Stack:** C# / .NET 8 / Windows Forms / System.Text.Json. Existing dependencies only — no new NuGet packages.

**Spec reference:** `docs/superpowers/specs/2026-04-13-robeats-detection-design.md`

---

## Working conventions

- **Project directory:** `C:/Users/G-Force/Downloads/coding journey/antoher macro/robeatspro`
- **Build command:** `dotnet build` (run from project directory). Expected: `Build succeeded.`
- **Run command:** `dotnet run` (launches the WinForms app). Debug form is opened with the Debug key (default `P`).
- **Commits:** The project is not a git repo. If the user wants version control, they can `git init` before starting. Treat each "Commit" step as: "save file, run `dotnet build`, ensure success."
- **Scope discipline:** Don't refactor files you aren't told to touch. Keep changes surgical.

---

## File Structure

**Created:**
- `robeatspro/AccuracyPreset.cs` — enum + preset → delay lookup

**Modified:**
- `robeatspro/MacroEngine.cs` — rising-edge detection, scheduled-press logic, pull preset from active profile
- `robeatspro/ConfigManager.cs` — introduce `GameProfile`, move `Detection` / `Tuning` / `Coords` / `AccuracyPreset` under profiles, migration from legacy flat config + `coords.json`
- `robeatspro/GamesTab.cs` — accuracy preset dropdown (labels swap per selected game)
- `robeatspro/MainTab.cs` — live FPS label with color-coded warning
- `robeatspro/TuningTab.cs`, `robeatspro/ColorsTab.cs`, `robeatspro/CalibrationTab.cs` — follow-through: re-read from `ActiveProfile` on game switch (event hookup)

**No changes:**
- `NativeApi.cs`, `ScreenCapture.cs`, `KeybindsTab.cs`, `AppearanceTab.cs`, `FeedbackTab.cs`, admin/telemetry files — left alone.

---

## Task 1: Rising-edge detection in MacroEngine

**Rationale first:** A lane gets stuck in `Tapped` on dense streams because `noteCount` never drops below `_minPixels` — one note replaces another in the sample patch with no gap. Rising-edge fixes this.

**Files:**
- Modify: `robeatspro/MacroEngine.cs`

- [ ] **Step 1.1: Add last-note-count array**

Near the other private state arrays (around line 50–53), add:

```csharp
private readonly int[] _lastNoteCount = new int[4];
```

- [ ] **Step 1.2: Reset last-note-count on Start**

In `Loop()` after the other `Array.Fill` calls (around line 109), add:

```csharp
Array.Fill(_lastNoteCount, 0);
```

Also add the same line inside the pause-toggle block that fills `_tapReleaseAt` etc. (around line 145) so resumption starts clean:

```csharp
Array.Fill(_lastNoteCount, 0);
```

- [ ] **Step 1.3: Change Idle branch to require rising edge**

Find (around line 250–252):

```csharp
if (state == LaneState.Idle && now - _holdReleasedAt[i] >= _holdReleaseCooldown)
{
    if (noteCount >= _minPixels)
    {
```

Replace the inner `if` with:

```csharp
if (noteCount >= _minPixels && _lastNoteCount[i] < _minPixels)
{
```

- [ ] **Step 1.4: Relax Tapped exit so streams can retrigger**

Find (around lines 269–276):

```csharp
else if (state == LaneState.Tapped)
{
    if (noteCount < _minPixels && _tapReleaseAt[i] == 0.0)
    {
        States[i] = LaneState.Idle;
        HoldIncoming[i] = false;
    }
}
```

Replace with:

```csharp
else if (state == LaneState.Tapped)
{
    bool released    = _tapReleaseAt[i] == 0.0;
    bool risingEdge  = noteCount >= _minPixels && _lastNoteCount[i] < _minPixels;
    bool cleared     = noteCount < _minPixels;

    if (released && (cleared || risingEdge))
    {
        States[i] = LaneState.Idle;
        HoldIncoming[i] = false;
    }
}
```

- [ ] **Step 1.5: Store current count at end of per-lane loop**

Find the last line inside the `for (int i = 0; i < 4; i++)` detection block, right before the closing `}` of the loop (after the `Tapped` block, around line 277). Add:

```csharp
_lastNoteCount[i] = noteCount;
```

- [ ] **Step 1.6: Verify build**

Run: `dotnet build` (in `robeatspro/`).
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 1.7: Manual verification — Funky Friday unchanged**

Run: `dotnet run`. In the app, set game to Funky Friday (already working reference). Play one short Funky Friday song and confirm no misses (same as before — rising-edge is behaviorally equivalent when notes have gaps).

- [ ] **Step 1.8: Manual verification — RoBeats stream**

Switch to RoBeats. Find a song with a fast 4-note stream in one lane (any "Hard" or "Expert" song with streams). Confirm the stream is now hit cleanly; before this change it would have dropped notes.

- [ ] **Step 1.9: Commit**

If git is initialized: `git add robeatspro/MacroEngine.cs && git commit -m "feat(detection): rising-edge triggering for reliable dense streams"`. Else: skip and proceed.

---

## Task 2: FPS warning indicator in MainTab

**Files:**
- Modify: `robeatspro/MainTab.cs`

- [ ] **Step 2.1: Read MainTab to find a layout slot**

Run: `grep -n "private.*Label" robeatspro/MainTab.cs | head -30` (use Grep tool).
Identify existing labels so you can match style (font, color scheme).

- [ ] **Step 2.2: Add FPS label field**

At the top of `MainTab` class alongside other private label fields, add:

```csharp
private Label _fpsLabel = null!;
private Label _fpsWarnLabel = null!;
private System.Windows.Forms.Timer _fpsTimer = null!;
```

- [ ] **Step 2.3: Create the labels and timer in the constructor**

Inside the `MainTab` constructor, after existing controls are added (use a sensible empty area — place at bottom-left of the tab, coordinates around `(12, Height - 48)` with `Anchor = Bottom | Left`):

```csharp
_fpsLabel = new Label
{
    Text = "FPS: --",
    Font = new Font("MS Sans Serif", 9f, FontStyle.Bold),
    AutoSize = true,
    Location = new Point(12, 430),
    Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
    ForeColor = Color.FromArgb(160, 160, 170)
};

_fpsWarnLabel = new Label
{
    Text = "",
    Font = new Font("MS Sans Serif", 8f),
    AutoSize = false,
    Size = new Size(360, 32),
    Location = new Point(12, 450),
    Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
    ForeColor = Color.FromArgb(255, 100, 100),
    Visible = false
};

Controls.Add(_fpsLabel);
Controls.Add(_fpsWarnLabel);

_fpsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
_fpsTimer.Tick += FpsTimer_Tick;
_fpsTimer.Start();
```

(Adjust `Location.Y` values so they don't overlap existing controls. If the tab has a visible empty area elsewhere, use that instead — just keep the two labels aligned vertically.)

- [ ] **Step 2.4: Add the timer handler**

Add this method inside the `MainTab` class:

```csharp
private void FpsTimer_Tick(object? sender, EventArgs e)
{
    var engine = MacroEngine.CurrentInstance;
    if (engine == null || !engine.Running)
    {
        _fpsLabel.Text = "FPS: --";
        _fpsLabel.ForeColor = Color.FromArgb(160, 160, 170);
        _fpsWarnLabel.Visible = false;
        return;
    }

    int fps = engine.Fps;
    _fpsLabel.Text = $"FPS: {fps}";

    if (fps >= 200)
    {
        _fpsLabel.ForeColor = Color.FromArgb(80, 220, 120);    // green
        _fpsWarnLabel.Visible = false;
    }
    else if (fps >= 120)
    {
        _fpsLabel.ForeColor = Color.FromArgb(230, 200, 80);    // yellow
        _fpsWarnLabel.Visible = false;
    }
    else
    {
        _fpsLabel.ForeColor = Color.FromArgb(255, 80, 80);     // red
        _fpsWarnLabel.Text =
            "Warning: FPS below 120 — macro may miss notes.\n" +
            "Close background apps or lower Roblox graphics to Quality 1.";
        _fpsWarnLabel.Visible = true;
    }
}
```

- [ ] **Step 2.5: Dispose the timer on form close**

If `MainTab` has an override for `Dispose(bool disposing)`, add `_fpsTimer?.Dispose();` inside it. If not, skip — form closure disposes child controls.

- [ ] **Step 2.6: Verify build**

Run: `dotnet build`.
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2.7: Manual verification — color coding**

Run the app, start the macro on any game. Verify:
- FPS number appears and updates every second.
- Color matches threshold (should be green for most modern PCs).
- To test red: temporarily add `Thread.Sleep(10);` to the macro loop (revert after), run, confirm red warning shows. **Revert the Thread.Sleep before moving on.**

- [ ] **Step 2.8: Commit**

If git: `git add robeatspro/MainTab.cs && git commit -m "feat(ui): live FPS indicator with low-FPS warning"`.

---

## Task 3: AccuracyPreset enum and delay table

Create a small shared utility file. This file will be referenced by both `ConfigManager` (for storage) and `MacroEngine` (for delay lookup).

**Files:**
- Create: `robeatspro/AccuracyPreset.cs`

- [ ] **Step 3.1: Create AccuracyPreset.cs**

Full file contents:

```csharp
namespace SoulBeatsPro;

/// <summary>
/// Shared enum for the accuracy preset dropdown. Labels displayed in the UI
/// swap per game (Perfect vs Sick) but the enum values are game-agnostic.
/// </summary>
internal enum AccuracyPreset
{
    PerfectOnly = 0,   // 0 ms delay — current behavior
    MostlyPerfects = 1,
    HumanLike = 2,
    Sloppy = 3
}

/// <summary>
/// Per-game max delay (in milliseconds) applied when scheduling a key press
/// after rising-edge detection. Uniform random in [0, maxDelayMs].
///
/// Values are set from research into RoBeats / Funky Friday timing windows
/// (see 2026-04-13-robeats-detection-design.md). Each max leaves at least
/// 20 ms of safety margin inside the Miss boundary.
/// </summary>
internal static class AccuracyPresetTable
{
    // RoBeats judgment windows: Perfect 60ms (20e/40l), Great ~+80ms late,
    // Okay ~+130-150ms late, Miss beyond ~+170ms.
    private static readonly double[] RoBeatsMaxMs =
    {
        0.0,     // PerfectOnly
        30.0,    // MostlyPerfects
        70.0,    // HumanLike
        120.0    // Sloppy
    };

    // FNF Psych Engine defaults: Sick ±45ms, Good ±90ms, Bad ±135ms, Shit >±166ms.
    private static readonly double[] FunkyFridayMaxMs =
    {
        0.0,     // PerfectOnly (labeled "Sick-only" for FF)
        30.0,    // MostlyPerfects (labeled "Mostly Sicks")
        75.0,    // HumanLike
        125.0    // Sloppy
    };

    /// <summary>Returns max delay in seconds for the preset + game combo.</summary>
    public static double GetMaxDelaySeconds(AccuracyPreset preset, bool whiteGrayMode)
    {
        var table = whiteGrayMode ? FunkyFridayMaxMs : RoBeatsMaxMs;
        int idx = (int)preset;
        if (idx < 0 || idx >= table.Length) return 0.0;
        return table[idx] / 1000.0;
    }

    /// <summary>Per-game UI labels for the dropdown.</summary>
    public static string[] GetLabels(bool whiteGrayMode)
    {
        return whiteGrayMode
            ? new[] { "Sick-only", "Mostly Sicks", "Human-like", "Sloppy" }
            : new[] { "Perfect-only", "Mostly Perfects", "Human-like", "Sloppy" };
    }
}
```

- [ ] **Step 3.2: Verify build**

Run: `dotnet build`.
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3.3: Commit**

If git: `git add robeatspro/AccuracyPreset.cs && git commit -m "feat(accuracy): add AccuracyPreset enum + per-game delay table"`.

---

## Task 4: Per-game profiles in ConfigManager

This is the biggest restructure. The goal is to move `Detection`, `Tuning`, `Coords`, and `AccuracyPreset` under a `profiles.<game>` key in `settings.json`, and migrate old flat configs on first launch.

**Files:**
- Modify: `robeatspro/ConfigManager.cs`

- [ ] **Step 4.1: Add GameProfile class**

Before `AppSettings` definition (around line 162), add:

```csharp
internal sealed class GameProfile
{
    [JsonPropertyName("detection")] public DetectionSettings Detection { get; set; } = new();
    [JsonPropertyName("tuning")] public TuningSettings Tuning { get; set; } = new();
    [JsonPropertyName("tap")] public int[][] Tap { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("hold")] public int[][] Hold { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("accuracyPreset")] public AccuracyPreset AccuracyPreset { get; set; } = AccuracyPreset.PerfectOnly;
}

internal sealed class ProfilesSettings
{
    [JsonPropertyName("funkyFriday")] public GameProfile FunkyFriday { get; set; } = new();
    [JsonPropertyName("robeats")] public GameProfile RoBeats { get; set; } = new();
}
```

(Using `int[][]` instead of `Point[]` to keep JSON as plain arrays, matching the existing `coords.json` format.)

- [ ] **Step 4.2: Add profiles to AppSettings, remove flat detection/tuning**

In `AppSettings` (currently around line 163–169), change:

```csharp
internal sealed class AppSettings
{
    [JsonPropertyName("keybinds")] public KeybindSettings Keybinds { get; set; } = new();
    [JsonPropertyName("detection")] public DetectionSettings Detection { get; set; } = new();
    [JsonPropertyName("theme")] public ThemeSettings Theme { get; set; } = new();
    [JsonPropertyName("tuning")] public TuningSettings Tuning { get; set; } = new();
    [JsonPropertyName("gameMode")] public GameModeSettings GameMode { get; set; } = new();
}
```

to:

```csharp
internal sealed class AppSettings
{
    [JsonPropertyName("keybinds")] public KeybindSettings Keybinds { get; set; } = new();
    [JsonPropertyName("theme")] public ThemeSettings Theme { get; set; } = new();
    [JsonPropertyName("gameMode")] public GameModeSettings GameMode { get; set; } = new();
    [JsonPropertyName("profiles")] public ProfilesSettings Profiles { get; set; } = new();

    // ── Legacy fields kept for one-shot migration read ──
    [JsonPropertyName("detection")] public DetectionSettings? LegacyDetection { get; set; }
    [JsonPropertyName("tuning")] public TuningSettings? LegacyTuning { get; set; }
}
```

- [ ] **Step 4.3: Replace Detection/Tuning accessors in ConfigManager**

Currently (around lines 207–212):

```csharp
public KeybindSettings Keybinds => _settings.Keybinds;
public DetectionSettings Detection => _settings.Detection;
public ThemeSettings Theme => _settings.Theme;
public TuningSettings Tuning => _settings.Tuning;
public GameModeSettings GameMode => _settings.GameMode;
```

Replace with:

```csharp
public KeybindSettings Keybinds => _settings.Keybinds;
public ThemeSettings Theme => _settings.Theme;
public GameModeSettings GameMode => _settings.GameMode;
public ProfilesSettings Profiles => _settings.Profiles;

public GameProfile ActiveProfile =>
    GameMode.ActiveGame == "funkyFriday" ? Profiles.FunkyFriday : Profiles.RoBeats;

public DetectionSettings Detection => ActiveProfile.Detection;
public TuningSettings Tuning => ActiveProfile.Tuning;
```

- [ ] **Step 4.4: Rewrite LoadCoords / SaveCoords to use ActiveProfile**

Currently `LoadCoords()` reads `coords.json` (around line 296–323) and `SaveCoords()` writes it (325–334).

Replace both methods with:

```csharp
public (Point[] tap, Point[] hold) LoadCoords()
{
    var p = ActiveProfile;
    if (p.Tap.Length != 4 || p.Hold.Length != 4)
        return GetDefaultCoords();

    try
    {
        var tap  = new Point[4];
        var hold = new Point[4];
        for (int i = 0; i < 4; i++)
        {
            tap[i]  = new Point(p.Tap[i][0],  p.Tap[i][1]);
            hold[i] = new Point(p.Hold[i][0], p.Hold[i][1]);
        }
        return (tap, hold);
    }
    catch { return GetDefaultCoords(); }
}

public void SaveCoords(Point[] tap, Point[] hold)
{
    var p = ActiveProfile;
    p.Tap  = tap.Select(pt => new[] { pt.X, pt.Y }).ToArray();
    p.Hold = hold.Select(pt => new[] { pt.X, pt.Y }).ToArray();
    SaveSettings();
}
```

- [ ] **Step 4.5: Add migration logic to LoadSettings**

Currently `LoadSettings()` (around lines 242–251):

```csharp
public void LoadSettings()
{
    if (!File.Exists(SettingsPath)) return;
    try
    {
        var json = File.ReadAllText(SettingsPath);
        _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
    catch { _settings = new AppSettings(); }
}
```

Replace with:

```csharp
public void LoadSettings()
{
    if (!File.Exists(SettingsPath)) return;
    try
    {
        var json = File.ReadAllText(SettingsPath);
        _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        MigrateLegacyIfNeeded();
    }
    catch { _settings = new AppSettings(); }
}

private void MigrateLegacyIfNeeded()
{
    // If either legacy field was deserialized, we came from an old settings.json.
    bool hasLegacyDetection = _settings.LegacyDetection != null;
    bool hasLegacyTuning    = _settings.LegacyTuning != null;
    bool hasLegacyCoords    = File.Exists(CoordsPath);

    if (!hasLegacyDetection && !hasLegacyTuning && !hasLegacyCoords) return;

    var activeGame = _settings.GameMode.ActiveGame;
    var target = activeGame == "funkyFriday"
        ? _settings.Profiles.FunkyFriday
        : _settings.Profiles.RoBeats;

    if (hasLegacyDetection) target.Detection = _settings.LegacyDetection!;
    if (hasLegacyTuning)    target.Tuning    = _settings.LegacyTuning!;

    if (hasLegacyCoords)
    {
        try
        {
            var json = File.ReadAllText(CoordsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tapArr  = root.GetProperty("tap");
            var holdArr = root.GetProperty("hold");
            target.Tap  = new int[4][];
            target.Hold = new int[4][];
            for (int i = 0; i < 4; i++)
            {
                target.Tap[i]  = new[] { tapArr[i][0].GetInt32(),  tapArr[i][1].GetInt32() };
                target.Hold[i] = new[] { holdArr[i][0].GetInt32(), holdArr[i][1].GetInt32() };
            }
        }
        catch { /* coords.json corrupt — fall through to defaults */ }
    }

    _settings.LegacyDetection = null;
    _settings.LegacyTuning    = null;
    SaveSettings();

    if (hasLegacyCoords)
    {
        try { File.Delete(CoordsPath); } catch { }
    }

    Log.Info($"Migrated legacy settings to profile: {activeGame}");
}
```

- [ ] **Step 4.6: Verify build**

Run: `dotnet build`.
Expected: `Build succeeded. 0 Error(s)`.

(If compilation fails with "TuningSettings.Reset not found" or similar — the existing `Reset()` on TuningSettings is fine; check the error. Usually compile errors here are in files not yet updated in later tasks, because `Tuning.Reset()` is still called from `TuningTab`.)

- [ ] **Step 4.7: Manual verification — fresh install case**

Back up the real settings: rename `%LocalAppData%/bigbart/settings.json` to `settings.bak.json`, delete `coords.json` if present. Run the app. Verify:
- App starts without crashing.
- A new `settings.json` is written containing a `profiles` key with `funkyFriday` and `robeats` entries.
- No `coords.json` is created.

- [ ] **Step 4.8: Manual verification — migration case**

Restore `settings.bak.json` back to `settings.json`. If you previously had a `coords.json`, restore that too. Run the app. Verify:
- App starts without crashing.
- `settings.json` now has the `profiles` key.
- The active game's profile has the detection/tuning values from the old flat config.
- The other game's profile has defaults.
- `coords.json` has been deleted.
- Log includes "Migrated legacy settings to profile: <activeGame>".

- [ ] **Step 4.9: Commit**

If git: `git add robeatspro/ConfigManager.cs && git commit -m "refactor(config): per-game profiles with one-time legacy migration"`.

---

## Task 5: Game-switch event plumbing

When the user switches games in `GamesTab`, the macro engine, debug form, tuning tab, and colors tab need to re-read from the new profile. Most already listen to `GameModeChanged` — we just need to ensure the macro engine restarts.

**Files:**
- Modify: `robeatspro/GamesTab.cs`
- Possibly: `robeatspro/MainForm.cs` (if `GameModeChanged` isn't already restarting the engine)

- [ ] **Step 5.1: Check existing handler wiring**

Use Grep to find `GameModeChanged`:

```
grep -n "GameModeChanged" robeatspro/*.cs
```

If `MainForm.cs` subscribes to `GameModeChanged` and restarts the engine, no change needed — note it and move on. If it only updates the UI, add engine restart.

- [ ] **Step 5.2: Ensure engine restarts on game switch (if needed)**

If not already handled, in `MainForm.cs` locate the `GameModeChanged` subscription and ensure it calls:

```csharp
var engine = MacroEngine.CurrentInstance;
if (engine != null && engine.Running)
{
    engine.Stop();
    // Wait for stop, then restart
    engine.OnStopped += () =>
    {
        var newEngine = new MacroEngine();
        newEngine.Start();
    };
}
```

(If this pattern is already present elsewhere in MainForm, follow that pattern instead of introducing a new one.)

- [ ] **Step 5.3: Update `ApplyDefaults_Click` in GamesTab to use ActiveProfile**

In `GamesTab.cs` around line 186, `var det = ConfigManager.Instance.Detection;` still works (it's a getter that now returns `ActiveProfile.Detection`) — no change. Same for `ConfigManager.Instance.Tuning.Reset()`. The SaveCoords call `ConfigManager.Instance.SaveCoords(defaultTap, defaultHold)` now writes to `ActiveProfile` automatically. **No code change needed — just verify the file still compiles after Task 4.**

- [ ] **Step 5.4: Verify build**

Run: `dotnet build`.
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5.5: Manual verification — profile isolation**

Run app. In RoBeats profile, tune `MinPixels` to 5 (unusual value). Switch to Funky Friday. Verify `MinPixels` shows its own value (default 3, not 5). Switch back to RoBeats. Verify it's still 5.

- [ ] **Step 5.6: Commit**

If git: `git add robeatspro/GamesTab.cs robeatspro/MainForm.cs && git commit -m "feat(config): game switch re-reads active profile"`.

---

## Task 6: Accuracy preset dropdown in GamesTab

**Files:**
- Modify: `robeatspro/GamesTab.cs`

- [ ] **Step 6.1: Add dropdown field**

Near existing fields (around line 13):

```csharp
private ComboBox _accuracyCombo = null!;
private Label _accuracyLabel = null!;
```

- [ ] **Step 6.2: Build the dropdown in the constructor**

After the `Detection Info` group is added (around line 141, after `Controls.Add(grpInfo);`), add:

```csharp
_accuracyLabel = new Label
{
    Text = "Accuracy:",
    Font = RetroFont,
    AutoSize = true,
    Location = new Point(12, 414)
};
Controls.Add(_accuracyLabel);

_accuracyCombo = new ComboBox
{
    DropDownStyle = ComboBoxStyle.DropDownList,
    Font = RetroFont,
    Location = new Point(80, 410),
    Size = new Size(160, 22)
};
_accuracyCombo.SelectedIndexChanged += AccuracyCombo_Changed;
Controls.Add(_accuracyCombo);

RefreshAccuracyCombo();
```

- [ ] **Step 6.3: Add RefreshAccuracyCombo method**

Inside `GamesTab`:

```csharp
private void RefreshAccuracyCombo()
{
    bool ff = ConfigManager.Instance.IsWhiteGrayMode;
    var labels = AccuracyPresetTable.GetLabels(ff);

    _accuracyCombo.SelectedIndexChanged -= AccuracyCombo_Changed;
    _accuracyCombo.Items.Clear();
    _accuracyCombo.Items.AddRange(labels);
    _accuracyCombo.SelectedIndex = (int)ConfigManager.Instance.ActiveProfile.AccuracyPreset;
    _accuracyCombo.SelectedIndexChanged += AccuracyCombo_Changed;
}

private void AccuracyCombo_Changed(object? sender, EventArgs e)
{
    int idx = _accuracyCombo.SelectedIndex;
    if (idx < 0 || idx > 3) return;
    ConfigManager.Instance.ActiveProfile.AccuracyPreset = (AccuracyPreset)idx;
    ConfigManager.Instance.SaveSettings();
}
```

- [ ] **Step 6.4: Refresh dropdown on game switch**

Inside `RbGame_CheckedChanged`, after `UpdateDescription();` add:

```csharp
RefreshAccuracyCombo();
```

- [ ] **Step 6.5: Verify build**

Run: `dotnet build`.
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6.6: Manual verification — dropdown labels swap**

Run app. On RoBeats, verify dropdown shows: "Perfect-only / Mostly Perfects / Human-like / Sloppy". Switch to Funky Friday, verify dropdown shows: "Sick-only / Mostly Sicks / Human-like / Sloppy". Change selection on each game and confirm it persists after app restart.

- [ ] **Step 6.7: Commit**

If git: `git add robeatspro/GamesTab.cs && git commit -m "feat(ui): accuracy preset dropdown with per-game labels"`.

---

## Task 7: Scheduled-press mechanics in MacroEngine

**Files:**
- Modify: `robeatspro/MacroEngine.cs`

- [ ] **Step 7.1: Add scheduled-press state + RNG**

Near the other private state arrays (lines 50–55 area):

```csharp
private readonly double[] _scheduledPressAt = new double[4];
private readonly Random _rng = new();
private AccuracyPreset _accuracyPreset;
private double _accuracyMaxDelay;    // seconds
```

- [ ] **Step 7.2: Load preset in Start()**

In `Start()` after `_whiteGrayMode = ConfigManager.Instance.IsWhiteGrayMode;` (around line 76), add:

```csharp
_accuracyPreset = ConfigManager.Instance.ActiveProfile.AccuracyPreset;
_accuracyMaxDelay = AccuracyPresetTable.GetMaxDelaySeconds(_accuracyPreset, _whiteGrayMode);
```

- [ ] **Step 7.3: Reset scheduled array in Loop()**

In `Loop()` near the other `Array.Fill` calls (line ~109), add:

```csharp
Array.Fill(_scheduledPressAt, 0.0);
```

Also inside the pause-toggle block (line ~145):

```csharp
Array.Fill(_scheduledPressAt, 0.0);
```

- [ ] **Step 7.4: Service scheduled presses each frame**

In `Loop()` near the existing "Non-blocking tap releases" block (around line 156–164), add a parallel service block right after it:

```csharp
// Service scheduled presses
for (int i = 0; i < 4; i++)
{
    if (_scheduledPressAt[i] > 0 && now >= _scheduledPressAt[i])
    {
        NativeApi.PressKey(i);
        _tapReleaseAt[i] = now + _tapKeyDuration;
        States[i] = LaneState.Tapped;
        _scheduledPressAt[i] = 0.0;
    }
}
```

- [ ] **Step 7.5: Change Idle branch to schedule or press**

Find the block from Task 1 (now around line 250–267):

```csharp
if (state == LaneState.Idle && now - _holdReleasedAt[i] >= _holdReleaseCooldown)
{
    if (noteCount >= _minPixels && _lastNoteCount[i] < _minPixels)
    {
        if (HoldIncoming[i])
        {
            NativeApi.PressKey(i);
            States[i] = LaneState.Holding;
            HoldIncoming[i] = false;
        }
        else
        {
            NativeApi.PressKey(i);
            _tapReleaseAt[i] = now + _tapKeyDuration;
            States[i] = LaneState.Tapped;
        }
    }
}
```

Replace the inner `else` branch (the tap case) with:

```csharp
else
{
    // Stream safety: if a press was already pending, fire it now first so
    // the old note doesn't get dropped, then fall through to schedule the new one.
    if (_scheduledPressAt[i] > 0)
    {
        NativeApi.PressKey(i);
        _tapReleaseAt[i] = now + _tapKeyDuration;
        States[i] = LaneState.Tapped;
        _scheduledPressAt[i] = 0.0;
        // Lane is now Tapped — next frame's rising-edge exit handles the new note.
    }
    else if (_accuracyMaxDelay <= 0.0)
    {
        NativeApi.PressKey(i);
        _tapReleaseAt[i] = now + _tapKeyDuration;
        States[i] = LaneState.Tapped;
    }
    else
    {
        double delay = _rng.NextDouble() * _accuracyMaxDelay;
        _scheduledPressAt[i] = now + delay;
        // Stay in Idle — scheduled-press service will transition us.
    }
}
```

- [ ] **Step 7.6: Release scheduled presses on Stop**

At the bottom of `Loop()` in the "Release all keys on stop" block (around line 282–288), add handling for pending scheduled presses. Replace:

```csharp
for (int i = 0; i < 4; i++)
{
    if (States[i] == LaneState.Holding || _tapReleaseAt[i] > 0)
        NativeApi.ReleaseKey(i);
}
```

with:

```csharp
for (int i = 0; i < 4; i++)
{
    if (States[i] == LaneState.Holding || _tapReleaseAt[i] > 0)
        NativeApi.ReleaseKey(i);
    _scheduledPressAt[i] = 0.0;
}
```

(We don't need to press+release a pending scheduled press on stop — just drop it.)

- [ ] **Step 7.7: Verify build**

Run: `dotnet build`.
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7.8: Manual verification — Perfect-only unchanged**

Start app on RoBeats, preset = Perfect-only. Play a song. Verify timing feels identical to before this task.

- [ ] **Step 7.9: Manual verification — Human-like varies**

Set preset to Human-like. Play the same song. Verify:
- No misses across the full song.
- End-of-song judgment summary shows a mix (not 100% Perfect anymore).

- [ ] **Step 7.10: Manual verification — Sloppy never misses**

Set preset to Sloppy. Play a hard song (one with streams). Verify:
- Still zero misses.
- Judgment summary shows Okay/Good hits.
- Streams still fire cleanly (stream safety rule working).

- [ ] **Step 7.11: Commit**

If git: `git add robeatspro/MacroEngine.cs && git commit -m "feat(accuracy): scheduled-press with per-preset random delay"`.

---

## Task 8: End-to-end verification

- [ ] **Step 8.1: Cross-game isolation check**

Run app. Set Funky Friday tuning `MinPixels = 2`, accuracy = Sick-only. Switch to RoBeats, set `MinPixels = 4`, accuracy = Sloppy. Save. Close app. Reopen. Switch between games a few times and confirm each profile's values hold.

- [ ] **Step 8.2: FPS indicator in all three regimes**

Run app, start macro. Indicator should be green on a modern PC. Temporarily set `Thread.Sleep(5)` in `MacroEngine.Loop()` to drop FPS into the 120–199 band → yellow. Set `Thread.Sleep(15)` → red with warning. **Revert the Sleep afterward.**

- [ ] **Step 8.3: Funky Friday regression check**

Full song on Funky Friday, any difficulty. Confirm:
- Still never misses (regression baseline).
- State machine handles holds correctly.
- Accuracy dropdown on Funky Friday labels as "Sick-only" etc.

- [ ] **Step 8.4: RoBeats stress test**

Pick the hardest RoBeats song you can. Play it with:
- Perfect-only — confirm no misses.
- Human-like — confirm no misses, varied judgments.
- Sloppy — confirm no misses even on hardest streams.

- [ ] **Step 8.5: Update docs**

Append to the Info panel of GamesTab (or anywhere visible) the minimum-FPS spec from the design:
> Minimum: 120 FPS / Recommended: 200+ / Roblox graphics: Quality 1.

This can be a one-line label addition next to the FPS indicator or inside the existing `Detection Info` groupbox.

- [ ] **Step 8.6: Final commit**

If git: `git add robeatspro/*.cs && git commit -m "docs: minimum FPS spec in-app"`.

---

## Self-Review Checklist (filled in)

**Spec coverage:**
- Section 1 (rising-edge) → Task 1 ✅
- Section 2 (per-game accuracy presets) → Tasks 3, 6, 7 ✅
- Section 3 (FPS warning + documented minimum) → Tasks 2, 8.5 ✅
- Section 4 (per-game profiles + migration) → Tasks 4, 5 ✅

**Placeholder scan:** No TBDs/TODOs — every code step shows concrete code.

**Type consistency:**
- `AccuracyPreset` enum values: `PerfectOnly`, `MostlyPerfects`, `HumanLike`, `Sloppy` — used consistently across `AccuracyPreset.cs`, `GameProfile`, `GamesTab`, `MacroEngine`.
- `AccuracyPresetTable.GetMaxDelaySeconds(preset, whiteGrayMode)` signature consistent between definition (Task 3) and call site (Task 7).
- `ConfigManager.Instance.ActiveProfile.AccuracyPreset` — same access pattern in both `GamesTab` (Task 6) and `MacroEngine.Start()` (Task 7).
- `Tap`/`Hold` as `int[][]` — matches across `GameProfile`, `LoadCoords`, `SaveCoords`, migration.

**Potentially useful skills for the implementation phase:**
- `superpowers:systematic-debugging` — if streams still drop notes after Task 1 or Sloppy introduces misses.
- `superpowers:verification-before-completion` — especially before declaring Task 8 done, since "never miss" is a hard claim that needs recorded play sessions.
- `superpowers:subagent-driven-development` — for executing this plan one task at a time with fresh context per task.
