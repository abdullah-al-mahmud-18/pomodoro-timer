using PomodoroTimer.Core.Models;

namespace PomodoroTimer.Core.Services;

/// <summary>
/// UI-agnostic countdown/count-up engine for a single session. Owns no threads or timers itself —
/// callers (e.g. a UI dispatcher tick) call <see cref="Tick"/> periodically, or <see cref="Elapsed"/>/
/// <see cref="Remaining"/> can be read on demand, both driven by an injectable clock for testability.
/// </summary>
public class TimerEngine
{
    private readonly Func<DateTimeOffset> _now;

    private DateTimeOffset _startedAt;
    private TimeSpan _accumulatedBeforePause;
    private DateTimeOffset? _runningSince;

    public string Name { get; private set; } = string.Empty;
    public TimerMode Mode { get; private set; }
    public SessionCategory Category { get; private set; }
    public TimeSpan? PlannedDuration { get; private set; }
    public TimerState State { get; private set; } = TimerState.Idle;

    public event EventHandler? Completed;

    public TimerEngine(Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DateTimeOffset StartedAt => _startedAt;

    public void Start(string name, TimerMode mode, SessionCategory category, TimeSpan? plannedDuration)
    {
        if (mode == TimerMode.Timer && (plannedDuration is null || plannedDuration.Value <= TimeSpan.Zero))
        {
            throw new ArgumentException("Timer mode requires a positive planned duration.", nameof(plannedDuration));
        }

        Name = name;
        Mode = mode;
        Category = category;
        PlannedDuration = mode == TimerMode.Timer ? plannedDuration : null;
        _accumulatedBeforePause = TimeSpan.Zero;
        _startedAt = _now();
        _runningSince = _startedAt;
        State = TimerState.Running;
    }

    public void Pause()
    {
        if (State != TimerState.Running)
        {
            return;
        }

        _accumulatedBeforePause = Elapsed;
        _runningSince = null;
        State = TimerState.Paused;
    }

    public void Resume()
    {
        if (State != TimerState.Paused)
        {
            return;
        }

        _runningSince = _now();
        State = TimerState.Running;
    }

    /// <summary>Stops the session before natural completion (user-initiated stop).</summary>
    public void Stop()
    {
        if (State is not (TimerState.Running or TimerState.Paused))
        {
            return;
        }

        if (State == TimerState.Running)
        {
            _accumulatedBeforePause = Elapsed;
        }

        _runningSince = null;
        State = TimerState.Stopped;
    }

    /// <summary>Re-evaluates elapsed time and raises <see cref="Completed"/> if a countdown has reached zero.</summary>
    public void Tick()
    {
        if (State != TimerState.Running)
        {
            return;
        }

        if (Mode == TimerMode.Timer && Remaining <= TimeSpan.Zero)
        {
            _accumulatedBeforePause = PlannedDuration!.Value;
            _runningSince = null;
            State = TimerState.Completed;
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            var running = _runningSince is null ? TimeSpan.Zero : _now() - _runningSince.Value;
            var total = _accumulatedBeforePause + running;

            if (Mode == TimerMode.Timer && PlannedDuration is not null && total > PlannedDuration.Value)
            {
                return PlannedDuration.Value;
            }

            return total;
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            if (Mode != TimerMode.Timer || PlannedDuration is null)
            {
                return TimeSpan.Zero;
            }

            var remaining = PlannedDuration.Value - Elapsed;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public Session ToSession(DateTimeOffset endedAt)
    {
        return new Session
        {
            Name = Name,
            Mode = Mode,
            Category = Category,
            PlannedDurationSeconds = PlannedDuration is null ? null : (int)PlannedDuration.Value.TotalSeconds,
            ActualDurationSeconds = (int)Elapsed.TotalSeconds,
            StartedAt = _startedAt,
            EndedAt = endedAt,
            Completed = State == TimerState.Completed || Mode == TimerMode.Stopwatch
        };
    }
}
