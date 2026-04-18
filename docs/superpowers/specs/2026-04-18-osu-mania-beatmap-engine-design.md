# osu!mania Beatmap-Driven Engine — Design Spec

## Problem

SoulBeatsPro's pixel-based detection works for Funky Friday and RoBeats but fails on osu!mania hold notes. Hold note bodies persist visually in the receptor area, keeping the color signature matched continuously. The `DetectionLane` state machine never gets the "clean frames" gap it needs to detect a release, so it can't distinguish between "still holding" and "a new note arrived."

## Solution

A new **beatmap-file engine** for osu!mania that reads note timing directly from `.osu` files instead of pixel detection. Pixel detection is used only once — to catch the first note and establish a time sync anchor. All subsequent keypresses (taps and holds) are scheduled from the parsed beatmap data.

## Approach

**Approach A: Hybrid Engine** — a separate `OsuManiaEngine` class alongside `MacroEngine`. Clean separation ensures zero risk to existing Funky Friday / RoBeats behavior.

---

## Section 1: Data Model — OsuBeatmapParser

### Output structures

```csharp
class OsuBeatmap {
    string Title;
    string Artist;
    string Version;      // difficulty name
    int KeyCount;         // from CircleSize
    int Mode;             // must be 3
    List<OsuNote> Notes;  // sorted by TimeMs ascending
}

struct OsuNote {
    int Column;       // 0-based, floor(x * keyCount / 512), clamped [0, keyCount-1]
    int TimeMs;       // ms from audio start
    int EndTimeMs;    // 0 for taps, >0 for holds
    bool IsHold;      // type bit 7
}
```

### Parsing logic

- `[General]` — verify `Mode: 3` (osu!mania)
- `[Difficulty]` — `CircleSize` = key count
- `[Metadata]` — `Title`, `Artist`, `Version`
- `[HitObjects]` — for each line: `x,y,time,type,hitSound,...`
  - Bit 7 of `type` → hold note → parse `endTime` from after colon separator
  - Column = `floor(x * keyCount / 512)`, clamped to `[0, keyCount-1]`
- Sort by `TimeMs`, then `Column` for ties

---

## Section 2: OsuMapDetector

### Window title detection

osu! stable window title format: `osu! - Artist - Title [Difficulty]`

1. Enumerate windows via `EnumWindows`, find one starting with `osu! - `
2. Parse `Artist`, `Title`, `Difficulty` from the title string
3. Scan `%LocalAppData%\osu!\Songs\` subfolders
4. For each folder, read `.osu` files' `[Metadata]` sections
5. Match `Artist` + `Title` + `Version` (case-insensitive, trimmed)

### Caching

Cache parsed `OsuBeatmap` keyed by file path. Don't re-parse on repeat plays.

### Failure

If no match found, log warning and don't start engine. Show "Could not find beatmap" in debug form.

### osu! path

Default: `%LocalAppData%\osu!\Songs`. Overridable per profile via `OsuSongsPath`.

---

## Section 3: OsuManiaEngine

### Lifecycle

1. **Pre-start:** Detector reads window title, finds and parses `.osu`. Abort if not found or not Mode 3.
2. **Sync phase:** Pixel detection on the column of the beatmap's first note (from `Notes[0].Column`), watching for its color signature. On detection + keypress → `t_anchor` (wall clock). First note's `TimeMs` → `t_beatmap_anchor`. Sync formula: `real_press_time = t_anchor + (note.TimeMs - t_beatmap_anchor)`.
3. **Playback phase:** Tight loop, check `now` against next scheduled note:
   - **Tap:** Press at `TimeMs`, release after `MinPressDurationMs`
   - **Hold:** Press at `TimeMs`, release at `EndTimeMs`
   - Accuracy preset jitter applied to press time only (not release)

### N-key support

Engine maintains its own `ushort[]` scan codes sized to `keyCount`, built from profile's `ManiaKeys`. Uses `NativeApi.PressScan()` / `ReleaseScan()` directly.

### State tracking

- `bool[] held` sized to `keyCount`
- Press → `held[col] = true`, key down
- Release → `held[col] = false`, key up
- No `DetectionLane` needed — exact times from file

### Pause/resume

Same hotkey. Pause releases all held keys. Resume skips past notes and continues from next upcoming.

### Thread model

Dedicated `Thread`, `IsBackground = true`, `AboveNormal` priority. `Thread.SpinWait(100)` between checks.

---

## Section 4: Profile & MacroEngine Integration

### New Profile fields

```csharp
enum DetectionMode { PixelBased, BeatmapFile }

// On Profile:
DetectionMode DetectionMode = PixelBased;   // default preserves existing behavior
string[] ManiaKeys = ["D","F","J","K"];      // keybinds for N keys
string OsuSongsPath = "";                    // empty = default %LocalAppData%\osu!\Songs
```

### Built-in profile

`SeedBuiltInProfiles()` adds `"osu!mania"` profile with `DetectionMode = BeatmapFile`, `IsBuiltIn = true`.

### Key count validation

If beatmap is 7K but profile has only 4 keys configured, engine warns and doesn't start.

### MacroEngine routing

```
MacroEngine.Start():
  if profile.DetectionMode == BeatmapFile → start OsuManiaEngine
  else → existing pixel detection loop (untouched)
```

Both engines expose same public state (`States[]`, `Fps`, `Running`, `Active`) so debug form works without changes.

### Untouched code

- All pixel detection code (DetectionLane, SignatureMatcher, SignatureLearner, ScreenCapture)
- Funky Friday / RoBeats profiles
- Calibration UI (hidden when BeatmapFile mode)
- AccuracyPreset system (used by both engines)

---

## Section 5: UI Changes

### ProfilesTab / GamesTab

- osu!mania profile: hide calibration controls (tap/hold pickers, signature learning)
- Show N keybind slots with note: "Key count auto-detected from beatmap. Configure enough keys."
- Optional Songs path text field, pre-filled with default

### MainTab status

- Active: `"Detected: Artist - Title [Diff] (4K)"`
- Failure: `"No osu!mania beatmap detected — is osu! open with a map selected?"`

### DebugForm

- Dynamic lane count based on `keyCount` instead of hardcoded 4
- Sync status: `"Synced"` / `"Waiting for first note..."` / `"Not synced"`

### KeybindsTab

- osu!mania profile: show `ManiaKeys` array (up to 10 keys) instead of fixed 4 lanes

### No new forms or tabs

Everything fits into existing UI with conditional visibility.

---

## Files to create

| File | Purpose |
|------|---------|
| `OsuBeatmapParser.cs` | Parse `.osu` files into `OsuBeatmap` / `OsuNote` |
| `OsuMapDetector.cs` | Find active beatmap from window title + Songs folder |
| `OsuManiaEngine.cs` | Beatmap-driven engine with sync, scheduling, humanization |

## Files to modify

| File | Change |
|------|--------|
| `Profile.cs` | Add `DetectionMode`, `ManiaKeys`, `OsuSongsPath` |
| `ConfigManager.cs` | Seed osu!mania built-in profile |
| `MacroEngine.cs` | Route to `OsuManiaEngine` when `DetectionMode == BeatmapFile` |
| `NativeApi.cs` | Add `UpdateLaneScans(string[], int count)` overload for N keys |
| `DebugForm.cs` | Dynamic lane count rendering |
| `KeybindsTab.cs` | N-key keybind UI for mania profiles |
| `MainTab.cs` / `MainForm.cs` | Beatmap detection status display |
