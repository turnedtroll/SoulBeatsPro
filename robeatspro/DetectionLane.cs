namespace SoulBeatsPro;

internal enum LaneAction { None, Press, Release }

/// <summary>
/// Pure per-lane state machine. Two states: Released and Pressing. Rising edge
/// fires Press; release occurs after N consecutive clean frames AND minimum
/// press duration has elapsed. No timers, no IO — caller supplies `now`.
/// </summary>
internal sealed class DetectionLane
{
    private readonly double _minPressDurationSec;
    private readonly int _cleanFramesRequired;

    private bool _pressing;
    private bool _prevPresent;
    private double _pressedAt;
    private int _cleanFrames;

    public DetectionLane(double minPressDurationSec, int cleanFrames)
    {
        _minPressDurationSec = minPressDurationSec;
        _cleanFramesRequired = cleanFrames;
    }

    public void Reset()
    {
        _pressing = false;
        _prevPresent = false;
        _pressedAt = 0.0;
        _cleanFrames = 0;
    }

    public LaneAction Update(bool present, double now)
    {
        LaneAction action = LaneAction.None;

        if (!_pressing)
        {
            if (present && !_prevPresent)
            {
                _pressing = true;
                _pressedAt = now;
                _cleanFrames = 0;
                action = LaneAction.Press;
            }
        }
        else
        {
            if (present) _cleanFrames = 0;
            else _cleanFrames++;

            bool enoughClean = _cleanFrames > _cleanFramesRequired;
            bool enoughHeld = (now - _pressedAt) >= _minPressDurationSec;
            if (enoughClean && enoughHeld)
            {
                _pressing = false;
                _cleanFrames = 0;
                action = LaneAction.Release;
            }
        }

        _prevPresent = present;
        return action;
    }
}
