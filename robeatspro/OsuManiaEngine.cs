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

internal sealed class OsuManiaEngine
{
    private readonly MacroEngine _parent;
    private readonly OsuBeatmap _beatmap;
    private readonly ushort[] _scanCodes;
    private readonly int _keyCount;
    private readonly bool[] _held;
    private volatile bool _stopRequested;
    private readonly Random _rng = new();
    private volatile string _syncStatus = "Not synced";

    public string SyncStatus => _syncStatus;
    public string DetectionStatus { get; set; } = "";

    public OsuManiaEngine(MacroEngine parent, OsuBeatmap beatmap, ushort[] scanCodes)
    {
        _parent = parent;
        _beatmap = beatmap;
        _scanCodes = scanCodes;
        _keyCount = beatmap.KeyCount;
        _held = new bool[beatmap.KeyCount];
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
            // Release (1) before Press (0) at same time — lets hold→tap on same column work
            return b.Kind.CompareTo(a.Kind);
        });

        return events;
    }

    /// <summary>
    /// Collect every note that shares the same TimeMs as the first note — the "first tick".
    /// These are all pressed simultaneously on sync; returns their columns in note order.
    /// Pure helper — exposed for tests.
    /// </summary>
    public static List<int> CollectFirstTickColumns(List<OsuNote> notes)
    {
        var cols = new List<int>();
        if (notes.Count == 0) return cols;
        int firstTickTime = notes[0].TimeMs;
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].TimeMs != firstTickTime) break;
            cols.Add(notes[i].Column);
        }
        return cols;
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
        double inputOffsetMs = tuning.InputOffsetMs;

        if (_beatmap.Notes.Count == 0)
        {
            _syncStatus = "Beatmap has no notes to play";
            return;
        }

        var firstNote = _beatmap.Notes[0];
        int firstTickTime = firstNote.TimeMs;
        var firstTickColumns = CollectFirstTickColumns(_beatmap.Notes);

        // OD drives the non-Marvelous judgment windows. Marvelous (300g) is fixed at 16.5ms.
        double od = _beatmap.OverallDifficulty;
        double perfectMs = OsuManiaTimingWindows.GetWindowMs(OsuManiaJudgment.Perfect, od);
        string odSummary = $"OD {od:0.#} — Marvelous ±{OsuManiaTimingWindows.MarvelousMs:0.#}ms, Perfect ±{perfectMs:0.#}ms";
        DetectionStatus = string.IsNullOrEmpty(DetectionStatus)
            ? odSummary
            : $"{DetectionStatus} · {odSummary}";

        // Build full schedule — first-tick PRESS events are skipped post-sync
        // (we press those columns directly when sync triggers). Their RELEASE events
        // stay in the schedule and fire naturally as playback advances.
        var schedule = BuildSchedule(_beatmap.Notes, tuning.MinPressDurationMs, skipFirstNote: false);

        // Pre-compute bidirectional jitter per press event. Each chord-member gets an
        // independent sample so simultaneous notes scatter naturally like human fingers.
        var jitterMs = new double[schedule.Count];
        for (int i = 0; i < schedule.Count; i++)
        {
            if (schedule[i].Kind == ScheduledEventKind.Press)
                jitterMs[i] = AccuracyPresetTable.SampleJitterMsBidirectional(preset, maxJudgmentMs, _rng);
        }

        // === SYNC PHASE ===
        _syncStatus = "Waiting for first note...";

        int syncColumn = firstNote.Column;

        (var tapPixels, _) = ConfigManager.Instance.LoadCoords();
        var signatures = profile.Signatures;
        int sampleHalf = tuning.SampleHalf;
        int minPixels = tuning.MinPixels;

        if (tapPixels.Length == 0 || syncColumn >= tapPixels.Length)
        {
            _syncStatus = $"No tap pixel for sync column {syncColumn + 1} — calibrate first";
            return;
        }

        var syncSig = syncColumn < signatures.Length ? signatures[syncColumn] : new ColorSignature();
        if (syncSig.Entries.Count == 0)
        {
            _syncStatus = $"No color signature for sync column {syncColumn + 1} — calibrate Colors tab";
            return;
        }

        var syncPoint = tapPixels[Math.Min(syncColumn, tapPixels.Length - 1)];

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
        // anchorBeatmapMs is shifted forward by inputOffsetMs so the engine's
        // idea of "current beatmap time" runs slightly ahead of real time —
        // presses fire inputOffsetMs earlier in wall time, compensating for
        // capture + SendInput latency so hits land on Marvelous (300g).
        int anchorBeatmapMs = firstTickTime;
        int eventIdx = 0;

        int syncFrameCount = 0;
        double syncFpsTimer = sw.Elapsed.TotalSeconds;

        while (!_stopRequested && !synced)
        {
            double now = sw.Elapsed.TotalSeconds;

            syncFrameCount++;
            if (now - syncFpsTimer >= 1.0)
            {
                _parent.Fps = syncFrameCount;
                syncFrameCount = 0;
                syncFpsTimer = now;
            }

            if (NativeApi.IsKeyDown(ConfigManager.Instance.Keybinds.Pause) && now - lastToggle > toggleDelay)
            {
                _parent.Active = !_parent.Active;
                lastToggle = now;
                if (!_parent.Active) ReleaseAll();
            }
            if (!_parent.Active) { Thread.Sleep(10); continue; }

            try { capture.Grab(); } catch { Thread.Sleep(1); continue; }

            int matchCount = capture.CountSignatureMatches(relX, relY, sampleHalf, syncSig);
            _syncStatus = $"Waiting for first note at col {syncColumn + 1} ({matchCount}/{minPixels} px)";
            if (matchCount >= minPixels)
            {
                // Press every column in the first tick simultaneously — chord members
                // arrive at the receptor together, so they must be pressed together.
                foreach (int col in firstTickColumns)
                {
                    if (col < _scanCodes.Length && !_held[col])
                    {
                        NativeApi.PressScan(_scanCodes[col]);
                        _held[col] = true;
                    }
                    if (col < _parent.States.Length)
                        _parent.States[col] = MacroEngine.LaneState.Pressing;
                }

                anchorWallTime = sw.Elapsed.TotalSeconds;
                anchorBeatmapMs = firstTickTime + (int)Math.Round(inputOffsetMs);
                synced = true;
                _syncStatus = "Synced — playing";

                // Advance past the first-tick PRESS events we just handled.
                // Their RELEASE events remain in the schedule and fire in playback.
                while (eventIdx < schedule.Count)
                {
                    var evt = schedule[eventIdx];
                    if (evt.Kind == ScheduledEventKind.Press && evt.TimeMs == firstTickTime)
                        eventIdx++;
                    else
                        break;
                }
            }

            Thread.SpinWait(100);
        }

        if (_stopRequested) { ReleaseAll(); return; }

        // === PLAYBACK PHASE ===
        // Pace the loop to a steady 2000 Hz (500μs per tick). Without this, the spin-wait
        // loop runs at a few million iterations/sec, inflating the FPS counter into the
        // millions and burning CPU for no extra timing precision. 2000 Hz is ~30× finer
        // than the 16.5ms Marvelous window — plenty of headroom — and shows as a clean,
        // readable number in the UI.
        int frameCount = 0;
        double fpsTimer = sw.Elapsed.TotalSeconds;
        double pauseStartTime = 0;
        long playbackTicksPerFrame = Stopwatch.Frequency / 2000;
        long nextFrameTicks = sw.ElapsedTicks + playbackTicksPerFrame;

        while (!_stopRequested && eventIdx < schedule.Count)
        {
            double now = sw.Elapsed.TotalSeconds;

            frameCount++;
            if (now - fpsTimer >= 1.0)
            {
                _parent.Fps = frameCount;
                frameCount = 0;
                fpsTimer = now;
            }

            if (NativeApi.IsKeyDown(ConfigManager.Instance.Keybinds.Pause) && now - lastToggle > toggleDelay)
            {
                _parent.Active = !_parent.Active;
                lastToggle = now;
                if (!_parent.Active)
                {
                    ReleaseAll();
                    pauseStartTime = now;
                }
                else if (pauseStartTime > 0)
                {
                    // Shift anchor forward by pause duration so we skip past notes
                    anchorWallTime += now - pauseStartTime;
                    pauseStartTime = 0;
                }
            }
            if (!_parent.Active) { Thread.Sleep(10); continue; }

            double elapsedSinceAnchor = now - anchorWallTime;
            double currentBeatmapMs = anchorBeatmapMs + elapsedSinceAnchor * 1000.0;

            while (eventIdx < schedule.Count)
            {
                var evt = schedule[eventIdx];
                double targetMs = evt.TimeMs;

                if (evt.Kind == ScheduledEventKind.Press)
                    targetMs += jitterMs[eventIdx];

                if (currentBeatmapMs < targetMs) break;

                if (evt.Column < _scanCodes.Length)
                {
                    if (evt.Kind == ScheduledEventKind.Press)
                    {
                        if (!_held[evt.Column])
                        {
                            NativeApi.PressScan(_scanCodes[evt.Column]);
                            _held[evt.Column] = true;
                        }
                        if (evt.Column < _parent.States.Length)
                            _parent.States[evt.Column] = MacroEngine.LaneState.Pressing;
                    }
                    else
                    {
                        if (_held[evt.Column])
                        {
                            NativeApi.ReleaseScan(_scanCodes[evt.Column]);
                            _held[evt.Column] = false;
                        }
                        if (evt.Column < _parent.States.Length)
                            _parent.States[evt.Column] = MacroEngine.LaneState.Released;
                    }
                }

                eventIdx++;
            }

            // Pace to target tick rate — sleep the remainder of the 500μs budget.
            while (sw.ElapsedTicks < nextFrameTicks)
                Thread.SpinWait(100);
            nextFrameTicks += playbackTicksPerFrame;
            // If we fell behind (e.g. pause resume), snap forward to avoid a burst.
            if (nextFrameTicks < sw.ElapsedTicks)
                nextFrameTicks = sw.ElapsedTicks + playbackTicksPerFrame;
        }

        ReleaseAll();
    }

    private void ReleaseAll()
    {
        for (int i = 0; i < _scanCodes.Length; i++)
        {
            if (i < _held.Length && _held[i])
            {
                NativeApi.ReleaseScan(_scanCodes[i]);
                _held[i] = false;
            }
            if (i < _parent.States.Length)
                _parent.States[i] = MacroEngine.LaneState.Released;
        }
    }
}
