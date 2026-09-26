namespace PomodoroTimer.Core.Models;

/// <summary>
/// Full set of Dashboard figures: today's totals, rolling-window totals, and rolling-window daily averages,
/// each broken down by <see cref="SessionCategory"/>.
/// </summary>
public class DashboardReport
{
    public required CategoryTotals Today { get; init; }

    public required CategoryTotals Last7Days { get; init; }
    public required CategoryTotals Last14Days { get; init; }
    public required CategoryTotals Last30Days { get; init; }
    public required CategoryTotals Last90Days { get; init; }
    public required CategoryTotals Last365Days { get; init; }

    public required CategoryTotals AveragePerDayLast7Days { get; init; }
    public required CategoryTotals AveragePerDayLast14Days { get; init; }
    public required CategoryTotals AveragePerDayLast30Days { get; init; }
    public required CategoryTotals AveragePerDayLast90Days { get; init; }
}
