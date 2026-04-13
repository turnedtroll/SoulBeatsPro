using System.Diagnostics;

namespace larpLOLv4;

/// Core macro logic — screen capture + pixel detection + key press state machine.
/// Runs on a dedicated thread, reports state via callbacks.
internal sealed class MacroEngine
{
    // ── Detection tuning ──
    private const int SampleHalf = 3;
    private const int MinPixels = 3;
    private const double TapKeyDuration = 0.03;
    private const double HoldReleaseCooldown = 0.06;
    private const double ToggleDelay = 0.3;
    private const double HoldArmGrace = 0.20; // keep HoldIncoming armed for 200ms after gray disappears

    // ── Lane labels ──
    public static readonly string[] LaneNames = ["Z", "X", ",", "."];
    public static readonly Color[] LaneColors =
    [
        Color.FromArgb(255, 80, 80),
        Color.FromArgb(80, 255, 80),
        Color.FromArgb(255, 180, 0),
        Color.FromArgb(255, 80, 200)
    ];

    // ── Public state (read by debug form) ──
    public enum LaneState { Idle, Tapped, Holding }

    public LaneState[] States { get; } = new LaneState[4];
    public int[] WhiteCounts { get; } = new int[4];
    public int[] TapGrayCounts { get; } = new int[4];
    public int[] HoldGrayCounts { get; } = new int[4];
    public bool[] HoldIncoming { get; } = new bool[4];
    public bool[] HoldSawTail { get; } = new bool[4];
    public bool Active { get; private set; } = true;
    public int Fps { get; private set; }
    public bool Running { get; private set; }

    public Point[] TapPixels { get; private set; } = null!;
    public Point[] HoldPixels { get; private set; } = null!;

    // ── Private state ──
    private readonly double[] _tapReleaseAt = new double[4];
    private readonly double[] _holdReleasedAt = new double[4];
    private readonly double[] _holdArmedAt = new double[4]; // timestamp when HoldIncoming was last armed
    private double _lastToggle;
    private Thread? _thread;
    private volatile bool _stopRequested;

    public event Action? OnStopped;

    public void Start()
    {
        if (Running) return;

        (TapPixels, HoldPixels) = CoordsManager.Load();
        _stopRequested = false;
        Running = true;

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
    }

    private void Loop()
    {
        // Compute bounding box for all 8 sample points
        var allPts = TapPixels.Concat(HoldPixels).ToArray();
        int capLeft = allPts.Min(p => p.X) - SampleHalf - 1;
        int capTop = allPts.Min(p => p.Y) - SampleHalf - 1;
        int capRight = allPts.Max(p => p.X) + SampleHalf + 1;
        int capBottom = allPts.Max(p => p.Y) + SampleHalf + 1;
        int capW = capRight - capLeft;
        int capH = capBottom - capTop;

        // Relative coordinates within the capture region
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

            // ── L: pause/resume ──
            if (NativeApi.IsKeyDown(NativeApi.VK_L) && now - _lastToggle > ToggleDelay)
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
                }
            }

            if (!Active)
            {
                Thread.Sleep(10);
                continue;
            }

            // ── Non-blocking tap releases ──
            for (int i = 0; i < 4; i++)
            {
                if (_tapReleaseAt[i] > 0 && now >= _tapReleaseAt[i])
                {
                    NativeApi.ReleaseKey(i);
                    _tapReleaseAt[i] = 0.0;
                }
            }

            // ── Capture screen ──
            try
            {
                capture.Grab();
            }
            catch
            {
                Thread.Sleep(1);
                continue;
            }

            // ── Per-lane detection ──
            for (int i = 0; i < 4; i++)
            {
                // Tap zone
                capture.AnalyzePatch(tapRel[i].X, tapRel[i].Y, SampleHalf,
                    out int whiteCount, out int tapGrayCount);

                // Hold zone
                capture.AnalyzePatch(holdRel[i].X, holdRel[i].Y, SampleHalf,
                    out int holdWhiteDummy, out int holdGrayCount);
                bool holdHasWhite = capture.PatchHasWhite(holdRel[i].X, holdRel[i].Y, SampleHalf);

                WhiteCounts[i] = whiteCount;
                TapGrayCounts[i] = tapGrayCount;
                HoldGrayCounts[i] = holdGrayCount;

                var state = States[i];

                // Hold zone: arm flag with grace timer
                if (holdGrayCount >= MinPixels && !holdHasWhite)
                {
                    HoldIncoming[i] = true;
                    _holdArmedAt[i] = now; // refresh the timer every frame gray is seen
                }
                else if (state == LaneState.Idle && holdGrayCount < MinPixels)
                {
                    // Only disarm after grace period expires (gray may have passed through)
                    if (HoldIncoming[i] && now - _holdArmedAt[i] >= HoldArmGrace)
                        HoldIncoming[i] = false;
                }

                // ── HOLDING ──
                if (state == LaneState.Holding)
                {
                    if (tapGrayCount >= MinPixels)
                    {
                        HoldSawTail[i] = true;
                    }
                    else if (HoldSawTail[i])
                    {
                        NativeApi.ReleaseKey(i);
                        States[i] = LaneState.Idle;
                        HoldSawTail[i] = false;
                        HoldIncoming[i] = false;
                        _holdReleasedAt[i] = now;
                    }
                    else if (whiteCount == 0 && tapGrayCount == 0 && holdGrayCount == 0)
                    {
                        NativeApi.ReleaseKey(i);
                        States[i] = LaneState.Idle;
                        HoldSawTail[i] = false;
                        HoldIncoming[i] = false;
                        _holdReleasedAt[i] = now;
                    }
                }

                // ── IDLE ──
                if (state == LaneState.Idle && now - _holdReleasedAt[i] >= HoldReleaseCooldown)
                {
                    if (whiteCount >= MinPixels)
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
                            _tapReleaseAt[i] = now + TapKeyDuration;
                            States[i] = LaneState.Tapped;
                        }
                    }
                }
                // ── TAPPED ──
                else if (state == LaneState.Tapped)
                {
                    if (whiteCount < MinPixels && _tapReleaseAt[i] == 0.0)
                    {
                        States[i] = LaneState.Idle;
                        HoldIncoming[i] = false;
                    }
                }
            }

            // Tiny yield to avoid 100% CPU
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
