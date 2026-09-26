using PomodoroTimer.Core.Models;
using PomodoroTimer.Core.Services;
using Xunit;

namespace PomodoroTimer.Core.Tests;

public class TimerEngineTests
{
    private class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Get() => Now;
        public void Advance(TimeSpan span) => Now += span;
    }

    [Fact]
    public void Timer_CountsDown_AndCompletes()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock.Get);
        var completedRaised = false;
        engine.Completed += (_, _) => completedRaised = true;

        engine.Start("Deep work", TimerMode.Timer, TimeSpan.FromMinutes(25));

        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Tick();
        Assert.Equal(TimerState.Running, engine.State);
        Assert.Equal(TimeSpan.FromMinutes(15), engine.Remaining);

        clock.Advance(TimeSpan.FromMinutes(15));
        engine.Tick();

        Assert.Equal(TimerState.Completed, engine.State);
        Assert.Equal(TimeSpan.Zero, engine.Remaining);
        Assert.True(completedRaised);
    }

    [Fact]
    public void Timer_PauseAndResume_ExcludesPausedDuration()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock.Get);
        engine.Start("Focus", TimerMode.Timer, TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(2));
        engine.Pause();
        Assert.Equal(TimerState.Paused, engine.State);

        clock.Advance(TimeSpan.FromMinutes(30));
        engine.Resume();

        clock.Advance(TimeSpan.FromMinutes(1));
        engine.Tick();

        Assert.Equal(TimerState.Running, engine.State);
        Assert.Equal(TimeSpan.FromMinutes(3), engine.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(7), engine.Remaining);
    }

    [Fact]
    public void Stopwatch_CountsUp_WithNoLimit()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock.Get);
        engine.Start("Reading", TimerMode.Stopwatch, null);

        clock.Advance(TimeSpan.FromHours(2));
        engine.Tick();

        Assert.Equal(TimerState.Running, engine.State);
        Assert.Equal(TimeSpan.FromHours(2), engine.Elapsed);
        Assert.Equal(TimeSpan.Zero, engine.Remaining);
    }

    [Fact]
    public void Stop_EndsSessionEarly_AsNotCompleted()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock.Get);
        engine.Start("Focus", TimerMode.Timer, TimeSpan.FromMinutes(25));

        clock.Advance(TimeSpan.FromMinutes(5));
        engine.Stop();

        Assert.Equal(TimerState.Stopped, engine.State);
        var session = engine.ToSession(clock.Now);
        Assert.False(session.Completed);
        Assert.Equal(300, session.ActualDurationSeconds);
    }

    [Fact]
    public void Stopwatch_ToSession_IsAlwaysCompleted()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock.Get);
        engine.Start("Reading", TimerMode.Stopwatch, null);

        clock.Advance(TimeSpan.FromMinutes(45));
        engine.Stop();

        var session = engine.ToSession(clock.Now);
        Assert.True(session.Completed);
        Assert.Null(session.PlannedDurationSeconds);
        Assert.Equal(2700, session.ActualDurationSeconds);
    }

    [Fact]
    public void Timer_WithoutDuration_Throws()
    {
        var engine = new TimerEngine();
        Assert.Throws<ArgumentException>(() => engine.Start("Bad", TimerMode.Timer, null));
    }

    [Fact]
    public void Elapsed_NeverExceedsPlannedDuration()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock.Get);
        engine.Start("Focus", TimerMode.Timer, TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(50));

        Assert.Equal(TimeSpan.FromMinutes(5), engine.Elapsed);
        Assert.Equal(TimeSpan.Zero, engine.Remaining);
    }
}
