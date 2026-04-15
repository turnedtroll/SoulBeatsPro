# Signature Learning + Manual Adjustment Design

**Date:** 2026-04-14
**Branch:** `feat/universal-detection`
**Context:** Follow-up to `2026-04-13-universal-mania-detection-design.md`. During end-to-end testing (Task 12 of that plan) the user reported that calibration produces non-deterministic results — different lanes fire correctly after each calibration run, never all four at once. This spec fixes the underlying capture bug and adds two user-facing calibration modes (Learning and Manual) plus editable signature controls.

---

## Problem

The current calibration flow in `CalibrationTab.RunCaptureWalkAsync` samples a single pixel at each lane's tap crosshair across 10 consecutive frames (~200 ms). `SignatureCapture.BuildEntry` then averages those 10 samples and sets `tolerance = max channel deviation from mean`.

This produces garbage in practice because a note is under the judgment-line crosshair for only 1–2 frames of 10 — the rest of the window sees background. Two failure modes:

1. **Mean ≈ background, tolerance explodes.** One brief on-note sample pushes `maxDev` to 100+. The resulting signature matches essentially every pixel → engine sees "always present" → key is held down permanently → missed notes because the macro never re-taps.
2. **All 10 samples were background.** Tolerance is tight (≈8), signature is pure background → signature never matches a real note → that lane stays silent.

Which failure each lane lands in is pure timing lottery based on where notes happen to be during its 200 ms window. This is why the user observed calibration changing *which* lanes work, never fixing all four.

A secondary bug: `ColorsTab.RenderSignatures` runs only inside the constructor, so after calibration the tab keeps showing the startup snapshot. The tab appears to report "(not calibrated)" even when signatures are populated in memory.

## Goals

1. Replace the averaging-everything capture algorithm with one that distinguishes note samples from background samples.
2. Support long-running, multi-song learning sessions so the user can improve signatures passively.
3. Support manual per-pixel signature entry via ScreenPicker for lanes Learning fails on.
4. Make ColorsTab editable: delete individual entries, tweak per-entry tolerance, see changes live.
5. Fix ColorsTab stale-render bug.

## Non-goals

- Changing the engine's detection loop. `MacroEngine` already matches against all entries in `Signatures[i].Entries` equally; that stays untouched.
- Using the hold crosshair in detection. It is already unused (verified: `MacroEngine.cs:152` only samples `tapRel[i]`). Hold crosshair remains as a calibration-only visual aid.
- Multi-frame sampling during runtime detection — the engine still samples a single patch per tick. The learning changes are capture-time only.

---

## Architecture

### Capture algorithm (replaces `SignatureCapture.BuildEntry`)

**Input:** `N` samples `(r, g, b)` collected across the capture session (1 sample per frame at the tap crosshair, ~30 Hz).

**Steps:**

1. **Compute the median sample** (channel-wise median of R, G, B across all samples). Any sample whose max channel distance from the median is ≤ `Δ_bg` (default 12) is labeled **background**. This cluster is discarded.
2. Of the remaining samples (distance > `Δ_bg` from median):
   - Pick the sample farthest from the median as the **seed**.
   - Grow the **note cluster** by including every sample within `Δ_note` (default 18) of the seed, then recomputing the cluster's mean and repeating once (one iteration of k-means-style refinement).
3. **Validate:** if the note cluster contains fewer than `minNoteSamples` (default 20) samples, return a failed `CaptureResult`. Do not overwrite existing signature.
4. **Build entry:**
   - `r, g, b` = integer mean of the note cluster.
   - `tolerance = max(floorTolerance=8, 2 × stddev)` computed over the note cluster only.
   - `learned = true`.
5. Return a successful `CaptureResult` carrying the entry.

**`CaptureResult` shape:**

```csharp
internal readonly struct CaptureResult
{
    public bool Ok { get; }
    public ColorSignatureEntry? Entry { get; }
    public string? FailureReason { get; }   // null when Ok

    public static CaptureResult Success(ColorSignatureEntry e) => new(true, e, null);
    public static CaptureResult Failed(string reason)         => new(false, null, reason);

    private CaptureResult(bool ok, ColorSignatureEntry? e, string? reason) { Ok = ok; Entry = e; FailureReason = reason; }
}
```

**Quick Calibrate** uses this same algorithm over a 1 s burst (~30 samples). **Learning** uses it over an arbitrary-length session (bounded by auto-stop at 5 minutes, or user-stopped).

### Capture orchestration (new: `SignatureLearner`)

A small class that owns the running-sample buffer for a learning session:

- `Start(screenCapture, tapPoints, monitorBounds)` — begins a background task that grabs a frame ~30 times/sec and appends one sample per lane to a per-lane `List<(byte r, byte g, byte b)>`.
- `Snapshot()` — returns current per-lane sample counts for the live status label.
- `Stop()` — signals the background task to stop, waits for it to drain, returns the final buffer.
- Thread-safe against UI reads (lock around sample-count snapshots; the buffers are only read from the UI thread *after* Stop).

Quick Calibrate reuses `SignatureLearner` with a 1 s auto-stop.

### Data model

`ColorSignatureEntry` gains one field:

```csharp
[JsonPropertyName("learned")] public bool Learned { get; set; } = false;
```

- `true` — produced by Quick Calibrate or Learning.
- `false` — added manually (ScreenPicker or ColorsTab edit).

**Commit behavior (Learning / Quick Calibrate):**
- For each lane with a successful capture:
  - If a `Learned == true` entry already exists → overwrite it.
  - Otherwise → append the new entry (so manual entries are always preserved).
- For each lane with a failed capture: existing signature is untouched. UI shows a per-lane warning.

**Commit behavior (Manual Add):** always appends a new entry with `Learned = false`. Default tolerance is 12.

**Engine impact:** zero. The `Learned` flag is metadata; `SignatureMatcher.Matches` is unchanged.

**Backward compatibility:** old settings.json deserializes `learned` as default `false`; existing entries are treated as manual. On first Quick Calibrate / Learning the user recalibrates anyway, so the new flag propagates naturally.

### UI

**Calibration tab — three buttons next to each other:**

| Button | Behavior |
|---|---|
| Quick Calibrate | 1 s parallel session sampling all 4 lanes simultaneously. Same `SignatureLearner` infrastructure as Learning, just bounded to 1 s. Current "Capture Signatures" button renamed. |
| Start Learning / Stop Learning | Long observational session. All 4 lanes sampled in parallel. Status label: `L1: 142 | L2: 89 | L3: 40 | L4: 201` plus elapsed time. Auto-stop at 5 min. |
| Manual Add | Opens a small modal: lane selector (1–4) + Pick Pixel button → launches `ScreenPicker` → captured RGB appended as `Learned = false` entry with tolerance = 12. |

The existing Skip/Cancel buttons are removed (they only made sense for the sequential per-lane walk; Learning uses Stop instead).

**Colors tab — editable:**

Per entry:
- Swatch (existing) — color fill, tooltip `R={r} G={g} B={b}  ±{tol}`.
- Small `[×]` button overlay, top-right corner of the swatch → deletes the entry, SaveSettings, re-renders.
- Tolerance slider (range 0–80) below the swatch → on ValueChanged updates `entry.Tolerance` immediately (so detection responds live); SaveSettings runs through a 200 ms debounce timer to avoid disk thrash while dragging.
- `[L]` text badge on the swatch when `Learned = true`.

Per lane row:
- `[ + Add color ]` button at the end of the row → same ScreenPicker flow as "Manual Add" on Calibration, pre-filled to that lane.

Bottom of tab:
- `[ Reset learned ]` — clears only `Learned == true` entries across all 4 lanes.
- `[ Clear all signatures ]` (existing) — wipes everything.

### Refresh behavior (fixes the stale-render bug)

`ColorsTab`:
- Subscribe to `VisibleChanged` → call `RenderSignatures()` when becoming visible.
- Subscribe to new `ConfigManager.ProfileSignaturesChanged` event → call `RenderSignatures()` whenever signatures change.

`ConfigManager`:
- New `public event Action? ProfileSignaturesChanged;` event.
- New `public void NotifySignaturesChanged()` helper that raises the event. Callers (Quick/Learning commit, Manual Add, ColorsTab edits, Reset learned, Clear all) explicitly invoke it after their `SaveSettings()` call. `SaveSettings()` itself does not raise the event — it has many callers (theme, keybinds, tuning, etc.) and only signature mutators should trigger ColorsTab refresh.

### File map

**New files:**
- `robeatspro/SignatureLearner.cs` — background sampler managing the learning/quick capture buffer.
- `robeatspro.Tests/SignatureCaptureClusterTests.cs` — 5 unit tests for the new clustering algorithm.

**Modified files:**
- `robeatspro/Profile.cs` — `ColorSignatureEntry.Learned` field + `[JsonPropertyName]`.
- `robeatspro/SignatureMatcher.cs` — rewrite `SignatureCapture.BuildEntry` → cluster-based; new return type `CaptureResult` (struct: `Success(entry)` or `Failed(reason)`).
- `robeatspro/CalibrationTab.cs` — replace current walk with Quick/Learn/Manual buttons backed by `SignatureLearner`.
- `robeatspro/ColorsTab.cs` — editable swatches, tolerance sliders, `[×]` / `[+ Add]` / `[Reset learned]` controls, live refresh hooks.
- `robeatspro/ConfigManager.cs` — `ProfileSignaturesChanged` event + `NotifySignaturesChanged()` helper.

**No changes to:** `MacroEngine.cs`, `ScreenCapture.cs`, `DetectionLane.cs`, `ProfilesTab.cs`. The engine side stays exactly as-is.

---

## Testing

**Unit tests (xUnit, `robeatspro.Tests/SignatureCaptureClusterTests.cs`):**

1. `Cluster_RejectsAllBackground` — 900 identical-ish background samples → `CaptureResult.Failed`.
2. `Cluster_FindsNoteAgainstBackground` — 850 gray + 50 white → returns `(r=~255, g=~255, b=~255)`, tolerance ≤ 30.
3. `Cluster_TightToleranceFromStdDev` — note cluster stddev = 5 → tolerance ≤ 14.
4. `Cluster_IgnoresSparseOutliers` — 800 bg + 30 note + 5 random RGB outliers → picks the note cluster, not outliers.
5. `Cluster_MinSampleCountEnforced` — only 15 note samples → `CaptureResult.Failed`.

**Manual smoke tests (user-run, after Task 8 of implementation):**

1. **FF — Learning:** Start Learning, play 2 Bold hands-off for 30 s, Stop. Colors tab shows 4 lanes × 1 swatch each, all bearing `[L]`. Run macro; all 4 lanes press on notes.
2. **FF — Manual tweak:** Delete lane 3's learned swatch. Manual Add → pick a white note pixel. Run macro; lane 3 fires correctly using the manual entry.
3. **Tolerance slider:** Drag lane 1's slider to 0 → lane 1 stops firing. Drag to 80 → lane 1 fires constantly. Live-responsive.
4. **Reset learned:** Add a manual entry to lane 2, then run Learning again (lane 2 now has manual + learned). Click Reset learned; lane 2 retains only the manual entry.
5. **Colors refresh:** Run Quick Calibrate on Calibration tab, switch to Colors tab. Swatches appear without restart. Closes the stale-render bug.
6. **Quick-vs-Learn fallback:** Run Quick Calibrate during a quiet section → at least one lane reports "insufficient note samples". Run Learning for 30 s → all 4 lanes populated.

---

## Rollout (task breakdown for the implementation plan)

1. `ColorSignatureEntry.Learned` field + 5 clustering unit tests (red → green via new algorithm).
2. Rewrite `SignatureCapture.BuildEntry` → cluster-based with `CaptureResult` return.
3. `SignatureLearner` class — background sampler + tests where practical (pure-data tests on the aggregation, not on the threading).
4. Quick Calibrate rewired: Calibration tab uses `SignatureLearner` with 1 s duration, applies `CaptureResult` per lane, shows per-lane warnings on failure.
5. Learning mode: Start/Stop button + live status label + auto-stop.
6. Manual Add (Calibration tab modal + ScreenPicker reuse).
7. ColorsTab editable: delete, tolerance slider, `[+ Add]` per lane, `[Reset learned]`, refresh hooks + `ProfileSignaturesChanged` event.
8. Manual end-to-end smoke test + final commit.

Each task ends with `dotnet build` clean, `dotnet test` green (for tasks touching pure logic), and a single scoped commit.
