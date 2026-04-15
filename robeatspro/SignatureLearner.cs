namespace SoulBeatsPro;

/// <summary>
/// Background sampler used by Quick Calibrate (1s burst) and Learning (long session).
/// Grabs frames at ~30 Hz and appends one (r,g,b) sample per lane per frame to per-lane
/// buffers. UI reads sample counts via Snapshot() while running; full buffers are read
/// only after Stop(). Owns its own ScreenCapture so it does not collide with the
/// CalibrationTab preview's capture.
/// </summary>
internal sealed class SignatureLearner : IDisposable
{
    private readonly object _lock = new();
    private readonly List<(byte r, byte g, byte b)>[] _buffers =
    {
        new(), new(), new(), new()
    };
    private CancellationTokenSource? _cts;
    private Task? _task;
    private ScreenCapture? _capture;
    private DateTime _startedAt;

    public bool IsRunning => _task is { IsCompleted: false };
    public TimeSpan Elapsed => IsRunning ? DateTime.UtcNow - _startedAt : TimeSpan.Zero;

    public void Start(Rectangle monitorBounds, Point[] tapPoints, int sampleHz = 30)
    {
        if (IsRunning) return;
        if (tapPoints.Length < 4) throw new ArgumentException("expected 4 tap points", nameof(tapPoints));

        lock (_lock)
        {
            foreach (var b in _buffers) b.Clear();
        }
        _startedAt = DateTime.UtcNow;
        _capture = new ScreenCapture(monitorBounds.Left, monitorBounds.Top,
            monitorBounds.Width, monitorBounds.Height);

        var localCapture = _capture;
        var localBounds = monitorBounds;
        var pts = (Point[])tapPoints.Clone();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int delayMs = Math.Max(10, 1000 / sampleHz);

        _task = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    localCapture.Grab();
                    for (int lane = 0; lane < 4; lane++)
                    {
                        int cx = pts[lane].X - localBounds.Left;
                        int cy = pts[lane].Y - localBounds.Top;
                        if (cx < 0 || cy < 0 || cx >= localCapture.Width || cy >= localCapture.Height)
                            continue;
                        var px = localCapture.ReadPixel(cx, cy);
                        lock (_lock) _buffers[lane].Add(px);
                    }
                }
                catch
                {
                    // Transient capture failure — keep going.
                }

                try { await Task.Delay(delayMs, token); }
                catch { break; }
            }
        }, token);
    }

    /// Per-lane sample counts. Safe to call while the learner is running.
    public int[] Snapshot()
    {
        lock (_lock)
        {
            return new[]
            {
                _buffers[0].Count, _buffers[1].Count,
                _buffers[2].Count, _buffers[3].Count
            };
        }
    }

    /// Stops the background task and returns the final per-lane sample buffers.
    /// Safe to call multiple times. Returned arrays are independent copies.
    public List<(byte r, byte g, byte b)>[] Stop()
    {
        var cts = _cts;
        var task = _task;
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
        }
        if (task != null)
        {
            try { task.Wait(2000); } catch { }
        }
        cts?.Dispose();
        _cts = null;
        _task = null;
        _capture?.Dispose();
        _capture = null;

        lock (_lock)
        {
            return new[]
            {
                _buffers[0].ToList(), _buffers[1].ToList(),
                _buffers[2].ToList(), _buffers[3].ToList()
            };
        }
    }

    public void Dispose()
    {
        if (IsRunning) Stop();
        _capture?.Dispose();
    }
}
