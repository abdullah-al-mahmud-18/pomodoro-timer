namespace PomodoroTimer.App.ViewModels;

/// <summary>Rolling "last N days" window for the History filter, or All to skip date filtering entirely.</summary>
public enum HistoryPeriod
{
    Last7Days,
    Last14Days,
    Last30Days,
    Last180Days,
    Last365Days,
    All
}
