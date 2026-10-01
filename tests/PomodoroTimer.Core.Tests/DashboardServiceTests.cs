using PomodoroTimer.Core.Models;
using PomodoroTimer.Core.Services;
using Xunit;

namespace PomodoroTimer.Core.Tests;

public class DashboardServiceTests
{
    // Fixed "now": 2026-03-15 noon, expressed in the local machine's own UTC offset so ToLocalTime()
    // lands on the same calendar day regardless of which timezone the test happens to run in.
    private static readonly DateTimeOffset Now = new(
        new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Unspecified),
        TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Unspecified)));

    private static Session MakeSession(SessionCategory category, int secondsAgoFromNow, int durationSeconds)
    {
        var startedAt = Now.AddSeconds(-secondsAgoFromNow);
        return new Session
        {
            Name = "Test",
            Mode = TimerMode.Timer,
            Category = category,
            PlannedDurationSeconds = durationSeconds,
            ActualDurationSeconds = durationSeconds,
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(durationSeconds),
            Completed = true
        };
    }

    [Fact]
    public void Today_OnlyIncludesSessionsStartedToday()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Work, secondsAgoFromNow: 60, durationSeconds: 1000), // today
            MakeSession(SessionCategory.Work, secondsAgoFromNow: (int)TimeSpan.FromDays(2).TotalSeconds, durationSeconds: 5000) // 2 days ago
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(1000, report.Today.WorkSeconds);
    }

    [Fact]
    public void CategoriesAreSummedIndependently()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Work, 60, 300),
            MakeSession(SessionCategory.Study, 60, 200),
            MakeSession(SessionCategory.Break, 60, 100)
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(300, report.Today.WorkSeconds);
        Assert.Equal(200, report.Today.StudySeconds);
        Assert.Equal(100, report.Today.BreakSeconds);
    }

    [Fact]
    public void Last7Days_ExcludesSessionsOlderThanWindow()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Work, secondsAgoFromNow: (int)TimeSpan.FromDays(6).TotalSeconds, durationSeconds: 1000), // within window
            MakeSession(SessionCategory.Work, secondsAgoFromNow: (int)TimeSpan.FromDays(8).TotalSeconds, durationSeconds: 9999) // outside window
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(1000, report.Last7Days.WorkSeconds);
    }

    [Fact]
    public void Last30Days_IncludesLast7DaysSessions()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Study, secondsAgoFromNow: 60, durationSeconds: 500)
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(500, report.Last7Days.StudySeconds);
        Assert.Equal(500, report.Last30Days.StudySeconds);
        Assert.Equal(500, report.Last365Days.StudySeconds);
    }

    [Fact]
    public void AveragePerDay_DividesTotalByWindowLength()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Work, 60, 700) // 700 seconds total in the last 7 days
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(100, report.AveragePerDayLast7Days.WorkSeconds);
    }

    [Fact]
    public void Last180Days_IncludesSessionsInsideWindowOnly()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Break, secondsAgoFromNow: (int)TimeSpan.FromDays(100).TotalSeconds, durationSeconds: 1800), // within window
            MakeSession(SessionCategory.Break, secondsAgoFromNow: (int)TimeSpan.FromDays(200).TotalSeconds, durationSeconds: 9999) // outside window
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(0, report.Last90Days.BreakSeconds);
        Assert.Equal(1800, report.Last180Days.BreakSeconds);
        Assert.Equal(10, report.AveragePerDayLast180Days.BreakSeconds);
    }

    [Fact]
    public void AveragePerDayLast365Days_DividesBy365()
    {
        var sessions = new[]
        {
            MakeSession(SessionCategory.Study, secondsAgoFromNow: (int)TimeSpan.FromDays(300).TotalSeconds, durationSeconds: 3650)
        };

        var service = new DashboardService(() => Now);
        var report = service.BuildReport(sessions);

        Assert.Equal(10, report.AveragePerDayLast365Days.StudySeconds);
    }

    [Fact]
    public void NoSessions_ProducesAllZeroes()
    {
        var service = new DashboardService(() => Now);
        var report = service.BuildReport(Array.Empty<Session>());

        Assert.Equal(0, report.Today.WorkSeconds);
        Assert.Equal(0, report.Last365Days.StudySeconds);
        Assert.Equal(0, report.AveragePerDayLast90Days.BreakSeconds);
    }
}
