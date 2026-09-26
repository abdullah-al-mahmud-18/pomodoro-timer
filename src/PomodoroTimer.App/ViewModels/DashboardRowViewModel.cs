using PomodoroTimer.Core.Models;

namespace PomodoroTimer.App.ViewModels;

/// <summary>One labeled row of the Dashboard: a period label plus Work/Study/Break durations formatted for display.</summary>
public class DashboardRowViewModel
{
    public DashboardRowViewModel(string label, CategoryTotals totals)
    {
        Label = label;
        WorkDisplay = FormatDuration(totals.WorkSeconds);
        StudyDisplay = FormatDuration(totals.StudySeconds);
        BreakDisplay = FormatDuration(totals.BreakSeconds);
    }

    public string Label { get; }
    public string WorkDisplay { get; }
    public string StudyDisplay { get; }
    public string BreakDisplay { get; }

    private static string FormatDuration(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : span.Minutes > 0
                ? $"{span.Minutes}m {span.Seconds}s"
                : $"{span.Seconds}s";
    }
}
