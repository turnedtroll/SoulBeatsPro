# Calibration UX Improvements

**Date:** 2026-04-13
**Scope:** `robeatspro/CalibrationTab.cs` (+ small helper additions if needed in `ScreenCapture.cs`)

## Problem

Users report three overlapping pain points with the current Calibration tab:

1. **Hard to place precisely.** The crosshair shows a position but the actual sample patch (7×7 pixels) is invisible at 0.5× preview scale. Users think a dot is "on" a target when the algorithm sees a different region.
2. **No feedback on whether placement works.** A dot can sit visually on top of a hold body and detection still misses, because the algorithm needs N qualifying pixels in the patch — and the user has no way to know how many it's actually getting.
3. **Workflow is slow.** Recalibrating one point requires selecting it, nudging with arrows or dragging, repeatedly toggling preview, and guessing whether the new position is better.

The downstream symptom is real-game misses traced to bad calibration that *looked* correct.

## Goals

Add four reinforcing features to the existing Calibration tab so the user can see, validate, and quickly fix sample-point placement without leaving the tab.

## Non-goals

- No new "smart calibration" tab or wizard.
- No change to the underlying detection algorithm.
- No change to coords storage format.
- No automatic recalibration triggered without explicit user action.

---

## Feature 1 — Live detection HUD per point

Next to each crosshair on the preview, draw a small badge showing the current qualifying-pixel count and a pass/fail mark, updated every preview tick.

- Format: `12 ✓` (green) when count ≥ `MinPixels`, `2 ✗` (red) when below.
- For tap points: count = white pixels (note detection).
- For hold points: count = gray pixels (hold body detection).
- Position: just to the side of the crosshair, in the lane color so it's clear which point each badge belongs to.
- Refreshes at the existing preview rate (~30 fps via `Timer_Tick`).

This is the primary signal — the user sees *immediately* whether a placement is functional without having to launch the macro and play a song.

## Feature 2 — Make the sample patch visible

The current code draws the sample patch outline with width = `SampleHalf * Scale` ≈ 1.5px. At preview scale that's invisible.

- Replace the thin inner rectangle with a **bold, semi-transparent filled square** (lane color, alpha ~80) covering the actual `(2*SampleHalf+1)²` pixel area scaled to preview.
- Add a 1px solid border in the lane color so the boundary is sharp.

Now the user can see *exactly* what 7×7 area is being measured and can position confidently relative to actual content.

## Feature 3 — Magnified pixel inspector

When a single point is selected, render a small zoomed view in the sidebar showing that sample patch at high magnification (~6×) with per-pixel classification overlay.

- Layout: a `~84×84` panel in the sidebar, below the existing controls.
- For each of the 49 pixels in the sample patch, draw a 12×12 zoomed cell. Tint:
  - **Green** = pixel passes the white threshold
  - **Blue** = pixel passes the gray threshold
  - **Dim** (gray, 30% alpha) = neither
- Below the panel, show a one-line counter: `White: 12 / Gray: 4 / MinPixels: 10`.
- Updates every preview tick alongside the main view.

This explains *why* a placement doesn't pass — the user sees that they only have 6 gray pixels in the patch and that they need to nudge until they have 10+.

## Feature 4 — Auto-snap button

A `Snap to Body` button in the sidebar that, when clicked during preview, scans a small region around the currently-selected point and snaps the point to the location with the highest qualifying-pixel count.

- Search region: ±15 pixels from current position (a 31×31 search window).
- For tap points: score by white-pixel count.
- For hold points: score by gray-pixel count.
- The new position must beat the current count by at least 1 pixel (no-op if already optimal).
- Shows a brief status message in the existing top status bar: `Snapped HX: 4 → 14 pixels` so the user can see whether snap helped.
- Disabled when no point is selected, when more than one is selected, or when preview is off.

This makes recalibration a one-click action when the right type of note is visible on screen.

---

## Interaction notes

- All four features only render/operate during preview. Outside preview, the existing static crosshair drawing remains.
- Per-point HUD and the magnified inspector both require running the same per-pixel analysis the macro uses. To avoid duplicating logic, expose a small helper in `ScreenCapture` that returns the per-pixel classification for an arbitrary patch (or returns the raw counts), and have both the HUD and the inspector use it.
- The magnified inspector should not redraw the bitmap if the selection hasn't changed *and* the underlying counts haven't changed — but for simplicity in v1, just redraw every tick. Preview is already 30 fps and the patch is tiny.

## Testing strategy

1. **HUD correctness:** Place a tap point on a clearly-white area, confirm green checkmark with high count. Move off — count drops to 0, red X.
2. **Sample patch visibility:** With a fresh install, confirm the highlighted square is obviously visible at preview scale.
3. **Magnifier classification:** With a hold body in view, select that point, confirm the magnifier shows mostly blue cells when count > MinPixels.
4. **Auto-snap:** Misplace lane 2 hold by 10px off the body, click Snap, verify it relocates to a higher-count position and the status bar shows the improvement.
5. **No-preview behavior:** Confirm everything still renders cleanly when preview is off (HUD/magnifier hidden, snap button disabled).

## Open questions

- Exact scaling and font sizes for the HUD badge will need tweaking once visible — start with sidebar font (~7.5pt) and adjust.
- Whether to add a "snap all" that auto-snaps every selected point in one go. Out of scope for v1; deferrable.
