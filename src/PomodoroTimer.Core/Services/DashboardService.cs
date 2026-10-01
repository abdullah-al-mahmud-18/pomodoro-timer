using PomodoroTimer.Core.Models;

namespace PomodoroTimer.Core.Services;

/// <summary>
/// Computes Dashboard totals and daily averages per <see cref="SessionCategory"/> from a session history.
/// Windows are measured in local calendar days ending today, driven by an injectable clock for testability.
/// </summary>
public class DashboardService
{
    private readonly Func<DateTimeOffset> _now;

    public DashboardService(Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.Now);
    }

    public DashboardReport BuildReport(IReadOnlyCollection<Session> sessions)
    {
        var today = _now().ToLocalTime().Date;

        return new DashboardReport
        {
            Today = TotalsSince(sessions, today),

            Last7Days = TotalsSince(sessions, today.AddDays(-6)),
            Last14Days = TotalsSince(sessions, today.AddDays(-13)),
            Last30Days = TotalsSince(sessions, today.AddDays(-29)),
            Last90Days = TotalsSince(sessions, today.AddDays(-89)),
            Last180Days = TotalsSince(sessions, today.AddDays(-179)),
            Last365Days = TotalsSince(sessions, today.AddDays(-364)),

            AveragePerDayLast7Days = Average(TotalsSince(sessions, today.AddDays(-6)), 7),
            AveragePerDayLast14Days = Average(TotalsSince(sessions, today.AddDays(-13)), 14),
            AveragePerDayLast30Days = Average(TotalsSince(sessions, today.AddDays(-29)), 30),
            AveragePerDayLast90Days = Average(TotalsSince(sessions, today.AddDays(-89)), 90),
            AveragePerDayLast180Days = Average(TotalsSince(sessions, today.AddDays(-179)), 180),
            AveragePerDayLast365Days = Average(TotalsSince(sessions, today.AddDays(-364)), 365)
        };
    }

    /// <summary>Sums ActualDurationSeconds per category for sessions whose local start date falls on or after <paramref name="startDateInclusive"/>.</summary>
    private static CategoryTotals TotalsSince(IReadOnlyCollection<Session> sessions, DateTime startDateInclusive)
    {
        var work = 0;
        var study = 0;
        var brk = 0;

        foreach (var session in sessions)
        {
            var startedLocalDate = session.StartedAt.ToLocalTime().Date;
            if (startedLocalDate < startDateInclusive)
            {
                continue;
            }

            switch (session.Category)
            {
                case SessionCategory.Work:
                    work += session.ActualDurationSeconds;
                    break;
                case SessionCategory.Study:
                    study += session.ActualDurationSeconds;
                    break;
                case SessionCategory.Break:
                    brk += session.ActualDurationSeconds;
                    break;
            }
        }

        return new CategoryTotals { WorkSeconds = work, StudySeconds = study, BreakSeconds = brk };
    }

    private static CategoryTotals Average(CategoryTotals totals, int dayCount)
    {
        return new CategoryTotals
        {
            WorkSeconds = totals.WorkSeconds / dayCount,
            StudySeconds = totals.StudySeconds / dayCount,
            BreakSeconds = totals.BreakSeconds / dayCount
        };
    }
}
