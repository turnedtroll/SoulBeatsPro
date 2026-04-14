using SoulBeatsPro;

namespace RoBeatsPro.Tests;

/// <summary>
/// DetectionLane is a pure state machine. It returns actions (Press / Release /
/// None) in response to per-frame "present" booleans and a timestamp. No IO.
/// </summary>
public class DetectionStateMachineTests
{
    private DetectionLane NewLane() => new(minPressDurationSec: 0.020, cleanFrames: 3);

    [Fact]
    public void rising_edge_returns_press()
    {
        var lane = NewLane();
        Assert.Equal(LaneAction.None, lane.Update(present: false, now: 0.000));
        Assert.Equal(LaneAction.Press, lane.Update(present: true, now: 0.001));
    }

    [Fact]
    public void continued_presence_returns_none()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);
        Assert.Equal(LaneAction.None, lane.Update(true, 0.050));
        Assert.Equal(LaneAction.None, lane.Update(true, 0.100));
    }

    [Fact]
    public void release_requires_N_clean_frames_and_min_press_duration()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);

        Assert.Equal(LaneAction.None, lane.Update(false, 0.005));
        Assert.Equal(LaneAction.None, lane.Update(false, 0.010));
        Assert.Equal(LaneAction.None, lane.Update(false, 0.015));

        Assert.Equal(LaneAction.Release, lane.Update(false, 0.030));
    }

    [Fact]
    public void single_clean_frame_then_present_resets_clean_counter()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);

        lane.Update(false, 0.100);
        lane.Update(false, 0.105);
        lane.Update(true,  0.110);
        lane.Update(false, 0.115);
        lane.Update(false, 0.120);
        Assert.Equal(LaneAction.None, lane.Update(false, 0.125));
        Assert.Equal(LaneAction.Release, lane.Update(false, 0.130));
    }

    [Fact]
    public void second_rising_edge_after_release_presses_again()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);
        lane.Update(false, 0.030);
        lane.Update(false, 0.035);
        lane.Update(false, 0.040);
        lane.Update(false, 0.045);
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.100));
    }

    [Fact]
    public void dense_tap_stream_fires_new_press_on_each_rising_edge()
    {
        var lane = NewLane();

        lane.Update(false, 0.000);
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.001));
        lane.Update(true, 0.003);
        lane.Update(true, 0.005);
        lane.Update(false, 0.007);
        lane.Update(false, 0.008);
        lane.Update(false, 0.009);
        Assert.Equal(LaneAction.Release, lane.Update(false, 0.025));
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.030));
    }

    [Fact]
    public void reset_returns_lane_to_released_without_emitting_release()
    {
        var lane = NewLane();
        lane.Update(false, 0.000);
        lane.Update(true,  0.001);
        lane.Reset();
        Assert.Equal(LaneAction.Press, lane.Update(true, 0.100));
    }
}
