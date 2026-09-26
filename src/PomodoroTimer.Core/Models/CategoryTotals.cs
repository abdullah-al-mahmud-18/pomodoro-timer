namespace PomodoroTimer.Core.Models;

/// <summary>Total seconds spent per <see cref="SessionCategory"/> over some time window.</summary>
public class CategoryTotals
{
    public int WorkSeconds { get; init; }
    public int StudySeconds { get; init; }
    public int BreakSeconds { get; init; }
}
