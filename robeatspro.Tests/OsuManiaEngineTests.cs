using SoulBeatsPro;

namespace RoBeatsPro.Tests;

public class OsuManiaEngineTests
{
    [Fact]
    public void build_schedule_creates_press_and_release_events_for_taps()
    {
        var notes = new List<OsuNote>
        {
            new(column: 0, timeMs: 1000, endTimeMs: 0, isHold: false),
            new(column: 1, timeMs: 1200, endTimeMs: 0, isHold: false),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30);

        Assert.Equal(4, events.Count);

        Assert.Equal(ScheduledEventKind.Press, events[0].Kind);
        Assert.Equal(0, events[0].Column);
        Assert.Equal(1000, events[0].TimeMs);

        Assert.Equal(ScheduledEventKind.Release, events[1].Kind);
        Assert.Equal(0, events[1].Column);
        Assert.Equal(1030, events[1].TimeMs);

        Assert.Equal(ScheduledEventKind.Press, events[2].Kind);
        Assert.Equal(1, events[2].Column);

        Assert.Equal(ScheduledEventKind.Release, events[3].Kind);
        Assert.Equal(1, events[3].Column);
        Assert.Equal(1230, events[3].TimeMs);
    }

    [Fact]
    public void build_schedule_creates_press_and_release_for_holds()
    {
        var notes = new List<OsuNote>
        {
            new(column: 2, timeMs: 1000, endTimeMs: 2000, isHold: true),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30);

        Assert.Equal(2, events.Count);

        Assert.Equal(ScheduledEventKind.Press, events[0].Kind);
        Assert.Equal(2, events[0].Column);
        Assert.Equal(1000, events[0].TimeMs);

        Assert.Equal(ScheduledEventKind.Release, events[1].Kind);
        Assert.Equal(2, events[1].Column);
        Assert.Equal(2000, events[1].TimeMs);
    }

    [Fact]
    public void build_schedule_sorts_events_by_time()
    {
        var notes = new List<OsuNote>
        {
            new(column: 0, timeMs: 2000, endTimeMs: 0, isHold: false),
            new(column: 1, timeMs: 1000, endTimeMs: 0, isHold: false),
            new(column: 2, timeMs: 1500, endTimeMs: 3000, isHold: true),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30);

        for (int i = 1; i < events.Count; i++)
            Assert.True(events[i].TimeMs >= events[i - 1].TimeMs,
                $"Event {i} at {events[i].TimeMs}ms is before event {i-1} at {events[i-1].TimeMs}ms");
    }

    [Fact]
    public void skip_first_note_returns_schedule_without_first_note_events()
    {
        var notes = new List<OsuNote>
        {
            new(column: 0, timeMs: 1000, endTimeMs: 0, isHold: false),
            new(column: 1, timeMs: 1200, endTimeMs: 0, isHold: false),
        };

        var events = OsuManiaEngine.BuildSchedule(notes, minPressDurationMs: 30, skipFirstNote: true);

        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].Column);
        Assert.Equal(1200, events[0].TimeMs);
    }
}
