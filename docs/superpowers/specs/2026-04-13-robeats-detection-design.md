# RoBeats Detection Overhaul + Accuracy Presets + Per-Game Profiles

**Date:** 2026-04-13
**Scope:** `robeatspro/` (C# WinForms app)

## Goals

1. Make RoBeats detection **never miss**, specifically on fast/dense streams.
2. Add a per-game **accuracy preset dropdown** (Perfect/Sick-only, Human-like, etc.) that varies hit timing but never produces a Miss.
3. Add **per-game profiles** so Funky Friday and RoBeats each keep their own detection thresholds, tuning, coordinates, and accuracy preset.
4. Add an **FPS warning indicator** and document the minimum FPS the macro needs to guarantee no misses.

## Non-goals

- No change to the shared state machine structure (Idle/Tapped/Holding).
- No switch to DXGI / Desktop Duplication capture — GDI+ is already delivering ~500 FPS, capture is not the bottleneck.
- No visual overhaul of the GUI (planned separately; this design uses existing controls).
- No changes to admin/telemetry or feature-gate systems.

---

## Section 1 — Rising-edge detection

### Problem

In `MacroEngine.Loop()`, a lane exits `Tapped` only when `noteCount < _minPixels`. In a fast stream where notes are back-to-back, the sample point sees continuous yellow pixels (one note exits as the next enters). `noteCount` never drops below threshold, the lane stays stuck in `Tapped`, and the next note is silently skipped.

### Fix

Fire the tap on the **rising edge** of `noteCount` (low → high) rather than on mere presence.

#### Changes to `MacroEngine.cs`

- Add `private readonly int[] _lastNoteCount = new int[4];` — previous frame's count per lane.
- **Idle branch:** change
  ```csharp
  if (noteCount >= _minPixels)
  ```
  to
  ```csharp
  if (noteCount >= _minPixels && _lastNoteCount[i] < _minPixels)
  ```
- **Tapped branch:** relax the exit condition so the lane can re-arm between same-lane notes:
  ```csharp
  else if (state == LaneState.Tapped)
  {
      bool released = _tapReleaseAt[i] == 0.0;
      bool risingEdge = noteCount >= _minPixels && _lastNoteCount[i] < _minPixels;
      bool cleared = noteCount < _minPixels;

      if (released && (cleared || risingEdge))
      {
          States[i] = LaneState.Idle;
          HoldIncoming[i] = false;
      }
  }
  ```
- At the end of each per-lane iteration, store `_lastNoteCount[i] = noteCount;`.
- Reset `_lastNoteCount` on Start() / pause-toggle.

### Edge case — anti-aliasing jitter

Roblox MSAA can make edge pixels flicker (e.g. 4 → 2 → 4 across frames on a single note). The refractory guard `_tapReleaseAt[i] == 0` prevents double-tapping from jitter — once a tap fires, the lane cannot retrigger until `TapKeyDuration` (default 30 ms) elapses, which is longer than any realistic AA flicker.

Recommended mitigation for users: run Roblox at **Graphics Quality 1** — eliminates MSAA blending entirely.

### Compatibility with Funky Friday (WhiteGray mode)

Behavior is functionally identical: in white/gray mode notes always have visible gaps (gray hold body or black background) between them, so the rising edge condition fires exactly once per note — same as the current `noteCount >= _minPixels` check.

---

## Section 2 — Per-game accuracy presets

### Concept

The detection fires at the instant a note crosses the judgment line — effectively the early side of the Perfect/Sick window. Introducing a scheduled delay between detection and key-press pushes the hit *later*, into Great/Good then Okay/Bad, never producing a Miss (as long as the max delay stays inside the Miss boundary).

### UI

Each game's settings tab gets a `ComboBox` labeled **Accuracy**. Options are per-game because the judgment names differ.

### Judgment names (researched)

- **RoBeats** — Perfect / Great / Okay / Miss
  - Sources: [RoBeats Wiki — Rank](https://robeats.fandom.com/wiki/Rank), [RoBeats Wiki — Gear](https://robeats.fandom.com/wiki/Gear), [TV Tropes — RoBeats](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/RoBeats).
  - Default Perfect window: 60 ms (20 ms early, 40 ms late). Great/Okay aren't officially published; community estimates put Okay's late edge ~150–180 ms from center.
- **Funky Friday** — Sick / Good / Bad / Shit
  - Sources: [Psych Engine Lua Variables](https://shadowmario.github.io/psychengine.lua/pages/variables.html), [GameBanana — FNF rating mod](https://gamebanana.com/mods/30690).
  - Psych Engine defaults (base FNF): Sick ±45 ms, Good ±90 ms, Bad ±135 ms, Shit beyond ±166 ms total.

### Preset tables

**RoBeats dropdown:**

| Preset | Delay (ms) | Expected judgment |
|---|---|---|
| Perfect-only (default) | 0 | 100% Perfect |
| Mostly Perfects | 0–30 (uniform random) | Mostly Perfect, occasional Great |
| Human-like | 0–70 | Mix of Perfect / Great |
| Sloppy | 0–120 | Perfect / Great / Okay — never Miss |

**Funky Friday dropdown:**

| Preset | Delay (ms) | Expected judgment |
|---|---|---|
| Sick-only (default) | 0 | 100% Sick |
| Mostly Sicks | 0–30 | Mostly Sick, occasional Good |
| Human-like | 0–75 | Mix of Sick / Good |
| Sloppy | 0–125 | Sick / Good / Bad — never Shit |

All maxima leave a **20–35 ms safety margin** inside the Miss boundary to absorb detection jitter (~2 ms per frame) and scroll-speed variance.

### Implementation

- Add enum `AccuracyPreset { PerfectOnly, MostlyPerfects, HumanLike, Sloppy }` (shared enum names; labels are game-specific in the UI).
- Store `AccuracyPreset` per profile (see Section 4).
- Map preset → max delay via a small lookup table per active game.
- Add `private readonly double[] _scheduledPressAt = new double[4];` in `MacroEngine`.
- Single `Random _rng = new();` engine-scoped.
**Scheduled-press state flow:**

- On rising-edge detection in Idle:
  - If preset delay is 0 (Perfect-only / Sick-only), press immediately and transition to `Tapped` (current behavior).
  - Else, set `_scheduledPressAt[i] = now + _rng.NextDouble() * maxDelay;`. Lane stays in `Idle` — the key is not pressed yet.
- Each loop iteration, for any lane with `_scheduledPressAt[i] > 0`:
  - If `now >= _scheduledPressAt[i]`: call `NativeApi.PressKey(i)`, set `_tapReleaseAt[i] = now + _tapKeyDuration`, set `States[i] = LaneState.Tapped`, clear `_scheduledPressAt[i]`.
- **Holds** (rising edge while `HoldIncoming[i]` is true) always press instantly regardless of preset — delayed holds risk missing the hold-arm window. Presets only vary tap timing.

### Safety rule for dense streams

If a new rising edge fires on lane `i` while `_scheduledPressAt[i] > 0` (a press is still pending):

1. Fire the pending press immediately: PressKey, set `_tapReleaseAt`, transition to `Tapped`, clear `_scheduledPressAt[i]`.
2. Return this iteration — the lane is now `Tapped`. The rising-edge exit path in Tapped (Section 1) handles the new note on the next frame.

This guarantees a 16th-note stream (where note spacing may be smaller than Sloppy's max delay) falls back to near-instant presses automatically — still never misses.

---

## Section 3 — FPS warning indicator + minimum spec

### Why a minimum exists

Rising-edge detection requires at least one capture frame during the note's visibility window in the 7×7 sample patch. Note visibility depends on scroll speed:

| Scroll multiplier | Note visibility | FPS for 1 catch | FPS for safe (2–3 frames) |
|---|---|---|---|
| 1× (default) | ~60 ms | 17 | 50 |
| 2× | ~30 ms | 34 | 100 |
| 4× (max fast songs) | ~15 ms | 67 | 200 |

### Spec

- **Minimum:** 120 FPS detection rate (covers most songs up to ~2× scroll)
- **Recommended:** 200+ FPS (covers all songs including max-scroll RoBeats charts)
- **Roblox graphics quality:** 1 (lowest) — also reduces MSAA anti-aliasing

### UI addition (MainTab.cs)

- Live FPS label: `FPS: ###` (reads `MacroEngine.CurrentInstance.Fps`, updates once per second).
- Color coding:
  - **Green** — ≥ 200 FPS
  - **Yellow** — 120–199 FPS
  - **Red + warning text** — < 120 FPS. Warning reads: *"FPS below 120 — macro may miss notes. Close background apps or lower Roblox graphics to Quality 1."*
- Small info icon next to the label with a tooltip explaining the thresholds.

Kept simple and self-contained so it survives the planned GUI overhaul easily.

---

## Section 4 — Per-game profiles

### Current state

`settings.json` is flat: one shared `detection`, `tuning`, plus `coords.json` alongside it. Switching games requires re-tuning/re-calibrating because every setting is overwritten.

### New structure

```json
{
  "gameMode": { "activeGame": "robeats" },
  "profiles": {
    "funkyFriday": {
      "detection": { "whiteGray": { "whiteMin": 240, "grayMin": 130, "grayMax": 170 } },
      "tuning": { "sampleHalf": 3, "minPixels": 3, "tapKeyDuration": 0.03, ... },
      "coords": {
        "tap":  [[x,y], [x,y], [x,y], [x,y]],
        "hold": [[x,y], [x,y], [x,y], [x,y]]
      },
      "accuracyPreset": "sickOnly"
    },
    "robeats": {
      "detection": {
        "noteColor": { "minR": 200, "minG": 180, "maxB": 80, ... },
        "holdColor": { "minR": 120, "maxR": 200, ... }
      },
      "tuning": { ... },
      "coords": { "tap": [...], "hold": [...] },
      "accuracyPreset": "perfectOnly"
    }
  },
  "keybinds": { "lane1": "Z", ... },
  "theme": { ... }
}
```

### Per-profile vs shared

- **Per-profile:** `detection`, `tuning`, `coords`, `accuracyPreset`
- **Shared across games:** `keybinds`, `theme`, admin/telemetry state

### ConfigManager API changes

- New class `GameProfile` holding `DetectionSettings`, `TuningSettings`, `Point[] tap/hold`, `AccuracyPreset`.
- `ConfigManager.ActiveProfile` returns the `GameProfile` for `GameMode.ActiveGame`.
- Existing getters (`Detection`, `Tuning`) become shortcuts: `Detection => ActiveProfile.Detection`.
- `LoadCoords()` / `SaveCoords()` read/write from/to `ActiveProfile.Coords` instead of `coords.json`.
- On game switch in `GamesTab`: save current profile, swap `ActiveGame`, stop + restart `MacroEngine` so it re-reads the new profile.

### Migration (one-time, on first launch after update)

1. If `profiles` key exists in `settings.json`, nothing to migrate — skip.
2. Else, read legacy flat `detection`, `tuning`, and `coords.json` (if present).
3. Create both profiles with defaults.
4. Apply the loaded legacy values to the profile matching `gameMode.activeGame` (the side the user was actually using).
5. Write new structure to `settings.json`.
6. Delete `coords.json` once migration succeeds.
7. Log a single line: `Migrated legacy settings to profile: <activeGame>`.

### Tabs behavior

`TuningTab`, `ColorsTab`, `CalibrationTab` stay the same visually — they always reflect the *active* profile. Switching the active game causes them to reload.

---

## Testing strategy

1. **Unit-ish** — Run against a screen recording of a fast RoBeats chart. Count detections vs actual notes in the recording. Target: 100% detection rate.
2. **Live** — Play a hard chart in Perfect-only mode, verify no misses over 10 full songs.
3. **Preset validation** — For each non-Perfect preset, play 3 songs and confirm:
   - Never a Miss / Shit.
   - Judgment distribution roughly matches the table (within ±10%).
4. **FPS warning** — Artificially cap the loop (`Thread.Sleep(10)`) so FPS drops below 120, verify the red warning appears.
5. **Profile migration** — Save a legacy `settings.json` + `coords.json`, launch, confirm the profile under `activeGame` has the legacy values and the other profile has defaults.
6. **Profile switching** — Tune RoBeats with one set of values, switch to Funky Friday, tune with different values, switch back — RoBeats values preserved.

## Open questions / to decide during implementation

- Exact maximum-delay numbers in the preset tables are conservative estimates. May need empirical tuning once tested in live gameplay. First-cut values are the starting point, not the final.
- Whether to expose advanced users a "custom" preset with raw min/max delay fields. Out of scope for v1 of this change; can add later.
