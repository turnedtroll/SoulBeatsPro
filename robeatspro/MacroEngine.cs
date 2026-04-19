using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace SoulBeatsPro;

/// Core macro logic — press-while-present detection.
/// A key stays held as long as the lane's color signature is present at
/// the tap pixel, released once it has been absent for `CleanFrames` frames.
internal sealed class MacroEngine
{
    public static MacroEngine? CurrentInstance { get; private set; }

    // Tuning — read from config at start
    private int _sampleHalf;
    private int _minPixels;
    private double _toggleDelay;

    public static readonly string[] LaneNames = ["1", "2", "3", "4"];
    public static readonly Color[] LaneColors =
    [
        Color.FromArgb(255, 80, 80),
        Color.FromArgb(80, 255, 80),
        Color.FromArgb(0, 180, 255),
        Color.FromArgb(200, 80, 255)
    ];

    public enum LaneState { Released, Pressing }

    // Public state (read by debug form / UI)
    public LaneState[] States { get; private set; } = new LaneState[4];
    public int[] MatchCounts => _matchCountsDebug;
    public bool Active { get; set; } = true;
    public int Fps { get; internal set; }
    public bool Running { get; private set; }

    public Point[] TapPixels { get; private set; } = null!;
    public Point[] HoldPixels { get; private set; } = null!;

    /// <summary>True for any lane whose press is currently delayed by an accuracy preset.</summary>
    public bool[] PendingScheduled => _pendingScheduledDebug;

    /// <summary>
    /// Human-readable status for osu!mania mode (detection + sync). Empty in pixel-based mode.
    /// </summary>
    public string StatusText
    {
        get
        {
            var e = _osuEngine;
            if (e == null) return "";
            var sync = e.SyncStatus;
            var det = e.DetectionStatus;
            if (string.IsNullOrEmpty(det)) return sync ?? "";
            return string.IsNullOrEmpty(sync) ? det : $"{det} — {sync}";
        }
    }

    // Private state
    private readonly DetectionLane[] _lanes = new DetectionLane[4];
    private readonly double[] _scheduledPressAt = new double[4];
    private readonly double[] _tapReleaseAt = new double[4]; // used only for preset-delayed taps' minimum held duration
    private readonly int[] _matchCountsDebug = new int[4];
    private readonly bool[] _pendingScheduledDebug = new bool[4];
    private readonly Random _rng = new();
    private AccuracyPreset _accuracyPreset;
    private double _accuracyMaxDelay;
    private double _maxJudgmentMs;
    private double _lastToggle;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private OsuManiaEngine? _osuEngine;
    private ColorSignature[] _signatures = new ColorSignature[4];
    private double _minPressDurationSec;
    private int _cleanFrames;
    private volatile string _lastStopReason = "";

    /// <summary>Last user-facing reason the engine stopped (e.g. osu! window not found).</summary>
    public string LastStopReason => _lastStopReason;

    public event Action? OnStopped;

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
    }

    public void Stop() { _stopRequested = true; _osuEngine?.Stop(); }

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
                _pendingScheduledDebug[i] = _scheduledPressAt[i] > 0;
                bool present = matchCount >= _minPixels;

                var action = _lanes[i].Update(present, now);
                switch (action)
                {
                    case LaneAction.Press:
                        if (_accuracyMaxDelay <= 0.0 || _scheduledPressAt[i] > 0)
                        {
                            NativeApi.PressKey(i);
                            States[i] = LaneState.Pressing;
                            _tapReleaseAt[i] = now + _minPressDurationSec;
                            _scheduledPressAt[i] = 0.0;
                        }
                        else
                        {
                            double delay = AccuracyPresetTable.SampleDelaySeconds(
                                _accuracyPreset, _maxJudgmentMs, _rng);
                            _scheduledPressAt[i] = now + delay;
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
            _lastStopReason = status;
            Running = false;
            OnStopped?.Invoke();
            return;
        }

        // Validate key count
        if (beatmap.KeyCount > profile.ManiaKeys.Length)
        {
            var msg = $"Beatmap is {beatmap.KeyCount}K but only {profile.ManiaKeys.Length} mania keys configured";
            Log.Warn($"[OsuMania] {msg}");
            _lastStopReason = msg;
            Running = false;
            OnStopped?.Invoke();
            return;
        }

        // Reject keys padded with duplicates (e.g. "BACKSPACE" x6) — would spam unintended keys
        var keys = profile.ManiaKeys;
        for (int i = 0; i < beatmap.KeyCount; i++)
        {
            for (int j = i + 1; j < beatmap.KeyCount; j++)
            {
                if (string.Equals(keys[i], keys[j], StringComparison.OrdinalIgnoreCase))
                {
                    var msg = $"Beatmap is {beatmap.KeyCount}K but keys {i + 1} and {j + 1} are both '{keys[i]}' — configure unique keys in Keybinds";
                    Log.Warn($"[OsuMania] {msg}");
                    _lastStopReason = msg;
                    Running = false;
                    OnStopped?.Invoke();
                    return;
                }
            }
        }

        // Resize state arrays for variable key count
        if (beatmap.KeyCount > States.Length)
        {
            States = new LaneState[beatmap.KeyCount];
        }

        var scanCodes = NativeApi.BuildScanCodes(profile.ManiaKeys, beatmap.KeyCount);

        _osuEngine = new OsuManiaEngine(this, beatmap, scanCodes);
        _osuEngine.DetectionStatus = status;
        _osuEngine.Run();

        // If the engine stopped before sync (e.g. missing calibration), preserve that message
        var syncMsg = _osuEngine.SyncStatus;
        if (!string.IsNullOrEmpty(syncMsg) && syncMsg != "Synced — playing")
            _lastStopReason = syncMsg;

        _osuEngine = null;
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
}
