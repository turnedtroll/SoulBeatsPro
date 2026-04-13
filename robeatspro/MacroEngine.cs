using System.Diagnostics;

namespace SoulBeatsPro;

/// Core macro logic — exact same state machine as larpLOLv4.
/// Supports two detection modes:
///   - WhiteGray (Funky Friday): white pixels = note, gray pixels = hold body
///   - ColorBased (RoBeats): configurable color thresholds
internal sealed class MacroEngine
{
    public static MacroEngine? CurrentInstance { get; private set; }

    // Tuning — read from config at start
    private int _sampleHalf;
    private int _minPixels;
    private double _tapKeyDuration;
    private double _holdReleaseCooldown;
    private double _toggleDelay;
    private double _holdArmGrace;
    private double _holdReleaseGrace;
    private bool _whiteGrayMode;

    public static readonly string[] LaneNames = ["1", "2", "3", "4"];
    public static readonly Color[] LaneColors =
    [
        Color.FromArgb(255, 80, 80),
        Color.FromArgb(80, 255, 80),
        Color.FromArgb(0, 180, 255),
        Color.FromArgb(200, 80, 255)
    ];

    public enum LaneState { Idle, Tapped, Holding }

    // Public state (read by debug form)
    public LaneState[] States { get; } = new LaneState[4];
    public int[] NoteCounts { get; } = new int[4];
    public int[] TapHoldCounts { get; } = new int[4];
    public int[] HoldZoneCounts { get; } = new int[4];
    public bool[] HoldIncoming { get; } = new bool[4];
    public bool[] HoldSawTail { get; } = new bool[4];
    public bool Active { get; set; } = true;
    public int Fps { get; private set; }
    public bool Running { get; private set; }
    public bool WhiteGrayMode => _whiteGrayMode;

    public Point[] TapPixels { get; private set; } = null!;
    public Point[] HoldPixels { get; private set; } = null!;

    // Private state
    private readonly double[] _tapReleaseAt = new double[4];
    private readonly double[] _holdReleasedAt = new double[4];
    private readonly double[] _holdArmedAt = new double[4];
    private readonly double[] _holdReleaseStartedAt = new double[4];
    private readonly int[] _lastNoteCount = new int[4];
    private double _lastToggle;
    private Thread? _thread;
    private volatile bool _stopRequested;

    public event Action? OnStopped;

    public void Start()
    {
        if (Running) return;
        CurrentInstance = this;
        (TapPixels, HoldPixels) = ConfigManager.Instance.LoadCoords();
        NativeApi.UpdateLaneScans(ConfigManager.Instance.Keybinds.LaneKeys);

        // Load tuning from config
        var t = ConfigManager.Instance.Tuning;
        _sampleHalf = t.SampleHalf;
        _minPixels = t.MinPixels;
        _tapKeyDuration = t.TapKeyDuration;
        _holdReleaseCooldown = t.HoldReleaseCooldown;
        _toggleDelay = t.ToggleDelay;
        _holdArmGrace = t.HoldArmGrace;
        _holdReleaseGrace = t.HoldReleaseGrace;
        _whiteGrayMode = ConfigManager.Instance.IsWhiteGrayMode;

        _stopRequested = false;
        Running = true;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop() { _stopRequested = true; }

    private void Loop()
    {
        // Bounding box for all 8 sample points
        var allPts = TapPixels.Concat(HoldPixels).ToArray();
        int capLeft = allPts.Min(p => p.X) - _sampleHalf - 1;
        int capTop = allPts.Min(p => p.Y) - _sampleHalf - 1;
        int capRight = allPts.Max(p => p.X) + _sampleHalf + 1;
        int capBottom = allPts.Max(p => p.Y) + _sampleHalf + 1;
        int capW = capRight - capLeft;
        int capH = capBottom - capTop;

        var tapRel = TapPixels.Select(p => new Point(p.X - capLeft, p.Y - capTop)).ToArray();
        var holdRel = HoldPixels.Select(p => new Point(p.X - capLeft, p.Y - capTop)).ToArray();

        using var capture = new ScreenCapture(capLeft, capTop, capW, capH);

        // Reset state
        Array.Fill(States, LaneState.Idle);
        Array.Fill(HoldIncoming, false);
        Array.Fill(HoldSawTail, false);
        Array.Fill(_tapReleaseAt, 0.0);
        Array.Fill(_holdReleasedAt, 0.0);
        Array.Fill(_holdArmedAt, 0.0);
        Array.Fill(_holdReleaseStartedAt, 0.0);
        Array.Fill(_lastNoteCount, 0);
        Active = true;
        _lastToggle = 0;

        var sw = Stopwatch.StartNew();
        int frameCount = 0;
        double fpsTimer = sw.Elapsed.TotalSeconds;

        while (!_stopRequested)
        {
            double now = sw.Elapsed.TotalSeconds;

            // FPS counter
            frameCount++;
            if (now - fpsTimer >= 1.0)
            {
                Fps = frameCount;
                frameCount = 0;
                fpsTimer = now;
            }

            // Pause/resume
            if (NativeApi.IsKeyDown(ConfigManager.Instance.Keybinds.Pause) && now - _lastToggle > _toggleDelay)
            {
                Active = !Active;
                _lastToggle = now;
                if (!Active)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (States[i] == LaneState.Holding) NativeApi.ReleaseKey(i);
                        if (_tapReleaseAt[i] > 0) NativeApi.ReleaseKey(i);
                    }
                    Array.Fill(States, LaneState.Idle);
                    Array.Fill(HoldIncoming, false);
                    Array.Fill(HoldSawTail, false);
                    Array.Fill(_tapReleaseAt, 0.0);
                    Array.Fill(_holdArmedAt, 0.0);
                    Array.Fill(_lastNoteCount, 0);
                }
            }

            if (!Active)
            {
                Thread.Sleep(10);
                continue;
            }

            // Non-blocking tap releases
            for (int i = 0; i < 4; i++)
            {
                if (_tapReleaseAt[i] > 0 && now >= _tapReleaseAt[i])
                {
                    NativeApi.ReleaseKey(i);
                    _tapReleaseAt[i] = 0.0;
                }
            }

            // Capture screen
            try { capture.Grab(); }
            catch { Thread.Sleep(1); continue; }

            // Per-lane detection
            for (int i = 0; i < 4; i++)
            {
                int noteCount, tapHoldCount, holdZoneCount;
                bool holdHasNote;

                if (_whiteGrayMode)
                {
                    // Funky Friday / larpLOLv4: white = note, gray = hold body
                    capture.AnalyzePatchWhiteGray(tapRel[i].X, tapRel[i].Y, _sampleHalf,
                        out noteCount, out tapHoldCount);
                    capture.AnalyzePatchWhiteGray(holdRel[i].X, holdRel[i].Y, _sampleHalf,
                        out _, out holdZoneCount);
                    holdHasNote = capture.PatchHasWhite(holdRel[i].X, holdRel[i].Y, _sampleHalf);
                }
                else
                {
                    // RoBeats: configurable color thresholds
                    capture.AnalyzePatchColor(tapRel[i].X, tapRel[i].Y, _sampleHalf,
                        out noteCount, out tapHoldCount);
                    capture.AnalyzePatchColor(holdRel[i].X, holdRel[i].Y, _sampleHalf,
                        out _, out holdZoneCount);
                    holdHasNote = capture.PatchHasNoteColor(holdRel[i].X, holdRel[i].Y, _sampleHalf);
                }

                NoteCounts[i] = noteCount;
                TapHoldCounts[i] = tapHoldCount;
                HoldZoneCounts[i] = holdZoneCount;

                var state = States[i];

                // Hold zone: arm flag with grace timer
                if (holdZoneCount >= _minPixels && !holdHasNote)
                {
                    HoldIncoming[i] = true;
                    _holdArmedAt[i] = now;
                }
                else if (state == LaneState.Idle && holdZoneCount < _minPixels)
                {
                    if (HoldIncoming[i] && now - _holdArmedAt[i] >= _holdArmGrace)
                        HoldIncoming[i] = false;
                }

                // ── HOLDING ──
                if (state == LaneState.Holding)
                {
                    if (tapHoldCount >= _minPixels)
                    {
                        HoldSawTail[i] = true;
                        _holdReleaseStartedAt[i] = 0; // still seeing hold body, reset grace timer
                    }
                    else if (HoldSawTail[i])
                    {
                        // Tail disappeared — grace timer prevents flicker drops
                        if (_holdReleaseStartedAt[i] == 0)
                            _holdReleaseStartedAt[i] = now;

                        if (now - _holdReleaseStartedAt[i] >= _holdReleaseGrace)
                        {
                            NativeApi.ReleaseKey(i);
                            States[i] = LaneState.Idle;
                            HoldSawTail[i] = false;
                            HoldIncoming[i] = false;
                            _holdReleasedAt[i] = now;
                            _holdReleaseStartedAt[i] = 0;
                        }
                    }
                    else if (noteCount == 0 && tapHoldCount == 0 && holdZoneCount == 0)
                    {
                        // Everything gone — instant release (matches larpLOLv4)
                        NativeApi.ReleaseKey(i);
                        States[i] = LaneState.Idle;
                        HoldSawTail[i] = false;
                        HoldIncoming[i] = false;
                        _holdReleasedAt[i] = now;
                        _holdReleaseStartedAt[i] = 0;
                    }
                }

                // ── IDLE ──
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
                // ── TAPPED ──
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

                _lastNoteCount[i] = noteCount;
            }

            Thread.SpinWait(100);
        }

        // Release all keys on stop
        for (int i = 0; i < 4; i++)
        {
            if (States[i] == LaneState.Holding || _tapReleaseAt[i] > 0)
                NativeApi.ReleaseKey(i);
        }
        Array.Fill(States, LaneState.Idle);

        Running = false;
        OnStopped?.Invoke();
    }
}
