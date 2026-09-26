namespace PomodoroTimer.Core.Models;

public class Session
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required TimerMode Mode { get; set; }

    /// <summary>Null for stopwatch sessions (no planned duration).</summary>
    public int? PlannedDurationSeconds { get; set; }

    public int ActualDurationSeconds { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }

    /// <summary>True if a timer ran to zero, or a stopwatch was stopped intentionally (always true for stopwatch).</summary>
    public bool Completed { get; set; }
}
