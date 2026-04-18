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

    public string SyncStatus { get; private set; } = "Not synced";
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
    /// Main engine loop. Call from a background thread.
    /// </summary>
    public void Run()
    {
        var profile = ConfigManager.Instance.ActiveProfile;
        var tuning = profile.Tuning;
        var preset = profile.AccuracyPreset;
        var maxJudgmentMs = profile.MaxJudgmentMs;
        double toggleDelay = tuning.ToggleDelay;

        var schedule = BuildSchedule(_beatmap.Notes, tuning.MinPressDurationMs, skipFirstNote: true);
        if (schedule.Count == 0 && _beatmap.Notes.Count <= 1)
        {
            SyncStatus = "Beatmap has no notes to play";
            return;
        }

        // Pre-compute accuracy jitter for each press event (sample once, not every spin)
        var jitterMs = new double[schedule.Count];
        for (int i = 0; i < schedule.Count; i++)
        {
            if (schedule[i].Kind == ScheduledEventKind.Press)
                jitterMs[i] = AccuracyPresetTable.SampleDelaySeconds(preset, maxJudgmentMs, _rng) * 1000.0;
        }

        // === SYNC PHASE ===
        SyncStatus = "Waiting for first note...";

        var firstNote = _beatmap.Notes[0];
        int syncColumn = firstNote.Column;

        (var tapPixels, _) = ConfigManager.Instance.LoadCoords();
        var signatures = profile.Signatures;
        int sampleHalf = tuning.SampleHalf;
        int minPixels = tuning.MinPixels;

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

        while (!_stopRequested && !synced)
        {
            double now = sw.Elapsed.TotalSeconds;

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
                if (syncColumn < _scanCodes.Length)
                {
                    NativeApi.PressScan(_scanCodes[syncColumn]);
                    _held[syncColumn] = true;
                }

                if (syncColumn < _parent.States.Length)
                    _parent.States[syncColumn] = MacroEngine.LaneState.Pressing;
                anchorWallTime = sw.Elapsed.TotalSeconds;
                synced = true;
                SyncStatus = "Synced";

                double firstReleaseDelaySec;
                if (firstNote.IsHold && firstNote.EndTimeMs > firstNote.TimeMs)
                    firstReleaseDelaySec = (firstNote.EndTimeMs - firstNote.TimeMs) / 1000.0;
                else
                    firstReleaseDelaySec = tuning.MinPressDurationMs / 1000.0;

                while (!_stopRequested && (sw.Elapsed.TotalSeconds - anchorWallTime) < firstReleaseDelaySec)
                    Thread.SpinWait(100);

                if (syncColumn < _scanCodes.Length)
                {
                    NativeApi.ReleaseScan(_scanCodes[syncColumn]);
                    _held[syncColumn] = false;
                }
                if (syncColumn < _parent.States.Length)
                    _parent.States[syncColumn] = MacroEngine.LaneState.Released;
            }

            Thread.SpinWait(100);
        }

        if (_stopRequested) { ReleaseAll(); return; }

        // === PLAYBACK PHASE ===
        int eventIdx = 0;
        int frameCount = 0;
        double fpsTimer = sw.Elapsed.TotalSeconds;
        double pauseStartTime = 0;

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

            Thread.SpinWait(100);
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
