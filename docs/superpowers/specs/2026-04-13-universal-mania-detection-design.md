# Universal Mania Detection

**Date:** 2026-04-13
**Scope:** `robeatspro/` (C# WinForms app)

## Goals

1. Replace the two hardcoded detection modes (WhiteGray for Funky Friday, Color for RoBeats) with a single **universal detection algorithm** that works for any 4-lane vertical-scroll mania game.
2. Fix two known bugs in the current system:
   - **Sticky holds in Funky Friday** — the key sometimes stays held after a hold note ends.
   - **Occasional missed taps in Funky Friday** — dense streams silently skip notes.
3. Fix the osu!mania hold problem: currently, WhiteGray mode misses every hold note in osu!mania because the hold body is the same color as the tap head, not gray.
4. Replace the two hardcoded game tabs (Funky Friday, RoBeats) with a **generic profile manager**: users add, calibrate, and manage profiles for any game. FF and RoBeats ship as built-in profiles.

## Non-goals

- No support for non-4-lane games in v1 (osu!mania 5K/6K/7K, Etterna higher keymodes). The engine stays hardcoded to 4 lanes; N-lane support is a future project.
- No change to capture backend (GDI+ stays).
- No change to admin/telemetry/feature-gate systems.
- No changes to keybinds, theme, or FPS warning logic beyond what's needed to read them from the new profile shape.

---

## Section 1 — Core detection algorithm: press-while-present

### Concept

Every mania game shares one visual invariant: **while a note (tap or hold) is visually at the judgment line, the player should have the key pressed.** Taps create a brief visual presence; holds create a sustained one. A single detector that "presses the key while the note color is present at the judgment line and releases when the color clears" handles both cases naturally.

This replaces both `WhiteGray` and color-based modes with one algorithm.

### Sample point

**One sample point per lane, on the judgment line.** No vertical offset above or below. Keeping the sample point at the line means zero-delay accuracy preset fires exactly at the moment the note is centered on the line — important for preset delay math to remain predictable.

Sample patch is the existing 7×7 (default `_sampleHalf = 3`).

### Color signature

Each lane stores a **color signature**: an array of entries, each `{ r, g, b, tolerance }`. A pixel matches the signature if any entry's channels are all within tolerance of the pixel's channels.

Signatures let one detector handle:
- **Same-color holds (osu!mania):** signature has 1 entry (tap = hold body).
- **Funky Friday current skin:** signature has 2 entries (white tap + gray hold body). Both count as "note present."
- **RoBeats:** signature has 2 entries (tap color + transparent trail color). Trail entry uses a wider tolerance captured from calibration variance.

### State machine

Only two states per lane: **Released** and **Pressing**.

Per frame, per lane:
1. Count pixels in the 7×7 patch that match any signature entry.
2. `present = matchCount >= _minPixels`.
3. Transitions:
   - **Released → Pressing:** rising edge (`present == true` AND previous frame `present == false`). Call `PressKey(i)`. Set `_pressedAt[i] = now`. Set `_clearFrames[i] = 0`.
   - **Pressing → Released:** `present == false` for ≥ `N` consecutive frames (default `N = 3`) AND `now - _pressedAt[i] >= _minPressDuration` (default 20 ms). Call `ReleaseKey(i)`.
4. End of iteration: update `_clearFrames[i]` — increment if `!present`, reset to 0 if `present`.

### How this fixes the known bugs

- **Sticky holds (FF):** today, hit-flash / lingering pixels near the judgment line keep `noteCount >= _minPixels` true, so the lane never releases. Under the new model, the in-game hit effects must be turned off (documented requirement). Once effects are off, the moment a hold body scrolls past the sample point, `present` drops to false for ≥ N frames and the key releases cleanly.
- **Dense tap misses (FF):** today, back-to-back notes keep `noteCount` above threshold continuously, so the lane never exits `Tapped` and the next rising edge is never observed. Under the new model, a rising edge is all that's needed to fire a press. As long as there's a visible gap between two tap sprites (true for every mania game's default note spacing at any non-degenerate scroll speed), the rising edge fires and the press happens.

### Tuning knobs (per profile)

- `sampleHalf` (default 3)
- `minPixels` (default 3)
- `cleanFrames` — `N` above. Default 3.
- `minPressDuration` — minimum ms between press and earliest possible release. Default 20.
- `tapKeyDuration` — deprecated in favor of `minPressDuration`; migration keeps the old value.

### Roblox / in-game requirements

Documented in-app and in the profile wizard:
- Turn off hit-effect / splash animations in the game.
- For Roblox games specifically: Graphics Quality 1 (also reduces MSAA).

Both are already expected for the existing detection overhaul; this design continues to require them.

---

## Section 2 — Calibration flow

### What calibration captures

The existing two-sample-point calibration UX (tap point + hold point per lane) stays visually identical. Internals change:

- **Tap head color:** captured at the tap point over ~10 frames while a tap note sits at the line. Mean color + observed variance → one signature entry.
- **Hold body color:** captured at the hold point over ~10 frames while a hold body flows through. Mean color + variance → second signature entry. **Optional** — users can skip this step if the game has same-color holds (osu!mania, Quaver default).

The tolerance on each signature entry is derived from the observed variance during capture, with a minimum floor (e.g. ±8 per channel) to tolerate AA jitter. Transparent trails naturally end up with wider tolerance because their variance over a changing background is higher.

### What calibration does *not* do at runtime

The **hold sample point is used only during calibration** to teach the system what hold-body color looks like. At gameplay time, only the tap point is sampled. This keeps the engine single-sample-per-lane (fast and simple) while still supporting games with multi-color note sprites.

### Calibration UX

Unchanged from the just-shipped calibration tab (`2026-04-13-calibration-ux-design.md`). The spec for that feature already covers wizard steps, snap-to-body, magnifier, and live detection HUD. This design reuses all of it — only the data structure the calibration writes changes:

- **Before:** separate `noteColor` and `holdColor` fields per profile.
- **After:** a `colorSignature` array per lane per profile, each entry an `{ r, g, b, tolerance }`.

---

## Section 3 — Generic profile manager

### Replaces the hardcoded FF + RoBeats tabs

A single **Profiles tab** presents:

- A scrollable list of profiles. Each row shows: name, an "active" radio button, a "duplicate" button, and a "delete" button.
- A "**+ Add Profile**" button at the bottom → opens a small dialog for the name → creates a blank profile with default tuning → user is directed to the Calibration tab to finish setup.
- One profile is always the **active profile**. The macro reads all detection/tuning/coords/accuracy settings from the active profile only.

### Built-in vs user profiles

- **Built-in:** "Funky Friday" and "RoBeats" are pre-seeded on first install. They can be renamed and reset-to-defaults, but cannot be deleted — this guarantees users always have a known-good starting point for the two most common games.
- **User-created:** added via "+ Add Profile". Full rename, duplicate, delete.

### Profile shape

```
Profile {
  name:              string
  isBuiltIn:         bool
  colorSignature:    array[4] of array of { r, g, b, tolerance }
  sampleCoords:      { tap: Point[4], hold: Point[4] }   // hold points used by calibration only
  tuning:            { sampleHalf, minPixels, cleanFrames, minPressDuration }
  accuracyPreset:    enum { MaxAccuracy, HighAccuracy, HumanLike, Sloppy }
  maxJudgmentMs:     double                               // see Section 4
}
```

### What's per-profile vs shared

- **Per-profile:** `colorSignature`, `sampleCoords`, `tuning`, `accuracyPreset`, `maxJudgmentMs`.
- **Shared across all profiles:** keybinds, theme, FPS warning thresholds, admin/telemetry state.

### Active-profile swap

When the user changes the active profile:
1. Persist the current profile's settings.
2. Set `ActiveProfileName` in config.
3. Stop and restart `MacroEngine` so it re-reads the new profile.
4. All tabs that read "the active profile" (Tuning, Colors, Calibration) refresh.

### ConfigManager API changes

- New class `Profile` (as above).
- `ConfigManager.Profiles` is a `List<Profile>`.
- `ConfigManager.ActiveProfile` returns the profile whose name matches `ActiveProfileName`.
- Existing shortcut getters (`Detection`, `Tuning`, etc.) become delegates to `ActiveProfile.*`.
- `LoadCoords()` / `SaveCoords()` now read/write `ActiveProfile.SampleCoords`.

---

## Section 4 — Accuracy presets (generalized)

### Concept unchanged

Same as the existing RoBeats detection spec: the detector fires at the early edge of the first non-miss judgment window. A random delay between detection and key-press pushes the hit later in the window, producing later judgments but never a miss (as long as the delay stays inside the game's safety boundary).

### Generic preset names

Presets are now profile-agnostic. Display strings per profile can still be customized (e.g. "Sick-only" vs "Perfect-only"), but the underlying enum is generic:

- `MaxAccuracy` — 0 ms delay. Always fires at the earliest possible judgment.
- `HighAccuracy` — `maxJudgmentMs × 0.20`. Mostly top-tier judgment, occasional one-tier-down.
- `HumanLike` — `maxJudgmentMs × 0.50`. Mix of top-tier and mid-tier.
- `Sloppy` — `maxJudgmentMs × 0.85`. Uses most of the non-miss window. Never misses.

### `maxJudgmentMs` per profile

A per-profile number: the late edge of the last non-miss judgment, minus a ~25 ms safety margin. Defaults:

- **Funky Friday** built-in: 140 ms (Bad late edge ~166 ms minus margin).
- **RoBeats** built-in: 150 ms (Okay late edge ~175 ms minus margin).
- **User-created profile** default: 100 ms — conservative; virtually every mania game has an "okay"-tier late edge ≥ 125 ms.

Exposed as a single numeric field in each profile's advanced settings. Most users never touch it.

### Holds are unaffected

Holds always press on rising edge with zero delay, regardless of preset, to avoid shortening the held-key window and risking a hold-arm miss. Presets only vary the initial-press timing — release is still governed by "clean frames" from Section 1.

### Implementation

- `private readonly double[] _scheduledPressAt = new double[4];` on `MacroEngine`.
- `Random _rng = new();` engine-scoped.
- On Released → Pressing rising edge:
  - If preset maxDelay == 0: press immediately, enter Pressing.
  - Else: set `_scheduledPressAt[i] = now + _rng.NextDouble() * maxDelay`. Lane stays in Released.
- Each iteration, for any lane with `_scheduledPressAt[i] > 0`: if `now >= _scheduledPressAt[i]`, fire the press and transition to Pressing.
- If a *new* rising edge fires while `_scheduledPressAt[i] > 0`: fire the pending press immediately (safety fallback — prevents misses in streams denser than the preset delay).

---

## Section 5 — Migration

Three possible starting states for a user's `settings.json`:

1. **Already new schema** (this spec): has `profiles[]` each with `colorSignature`. No migration.
2. **Post-detection-overhaul schema**: has `profiles` for `funkyFriday` and `robeats`, each with separate `noteColor` / `holdColor` fields. Migrate each profile's colors into a `colorSignature` array (entry 1 = tap color, entry 2 = hold body color if present). Default tolerance = ±12 per channel. Preserve sample coords, tuning, accuracy preset. Convert the two-profile object into the new `profiles[]` array with `isBuiltIn = true` for both.
3. **Pre-profiles legacy schema** (flat `detection` + `tuning` + `coords.json`): do the legacy migration from the detection-overhaul spec first, then apply migration #2.

Active profile is preserved across all migrations. After migration, the user's profile list starts with exactly FF and RoBeats, both built-in. No user profiles exist until the user adds one.

Log a single line on migration: `Migrated settings to universal-detection schema (<source>)`.

---

## Section 6 — Testing

1. **Funky Friday regression.** 10 songs, MaxAccuracy. Expected: zero misses, zero sticky holds. Uses the migrated built-in profile.
2. **RoBeats regression.** Same — 10 songs, verify detection rate matches or beats the current system.
3. **osu!mania 4K (new profile).** Create a fresh profile, calibrate against a default skin, play 5 songs across difficulty range. Verify: taps register, holds both press and release correctly.
4. **Quaver 4K (new profile).** Same process, to confirm universality beyond osu!mania.
5. **Sticky-hold stress test (FF).** Play a chart with back-to-back holds on the same lane. Verify each hold releases cleanly before the next engages.
6. **Dense-tap stress test.** Any 16th-note stream in any profile. Verify no misses.
7. **Preset validation per profile.** For each of HighAccuracy / HumanLike / Sloppy, play 3 songs and confirm:
   - Zero misses.
   - Judgment distribution roughly matches preset intent.
8. **Migration tests.** Simulate each of the three starting schemas, launch, confirm profile list contains FF + RoBeats with all original settings preserved, active profile preserved, no data lost.
9. **Profile swap.** Tune FF to custom values, swap to a user profile, tune it differently, swap back — FF values preserved exactly.

## Open questions / to defer

- **Per-lane per-color calibration (osu!mania rainbow skins):** skins that color each lane differently already work because each lane has its own signature. No additional work.
- **N-lane support (5K+):** out of scope for v1. Tracked as a future project.
- **"Advanced signature editor"** (manually add/remove signature entries without re-calibrating): out of scope; rely on re-calibration for v1.
- **Empirical preset tuning:** the `× 0.20 / × 0.50 / × 0.85` multipliers are educated first-cut values. May need adjustment after live testing.
