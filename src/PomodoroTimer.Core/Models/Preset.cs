namespace PomodoroTimer.Core.Models;

public class Preset
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required TimerMode Mode { get; set; }

    /// <summary>Null when Mode is Stopwatch (no fixed duration).</summary>
    public int? DurationSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
