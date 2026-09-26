using PomodoroTimer.Core.Models;

namespace PomodoroTimer.App.ViewModels;

public class SessionItemViewModel
{
    public Session Session { get; }

    public SessionItemViewModel(Session session)
    {
        Session = session;
    }

    public int Id => Session.Id;
    public string Name => Session.Name;
    public string ModeDisplay => Session.Mode == TimerMode.Timer ? "Timer" : "Stopwatch";
    public string CategoryDisplay => Session.Category.ToString();
    public string DurationDisplay => FormatDuration(Session.ActualDurationSeconds);
    public string StatusDisplay => Session.Completed ? "Completed" : "Stopped early";
    public string StartedAtDisplay => Session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string EndedAtDisplay => Session.EndedAt.ToLocalTime().ToString("HH:mm");

    private static string FormatDuration(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.Hours > 0
            ? $"{span.Hours}h {span.Minutes}m {span.Seconds}s"
            : span.Minutes > 0
                ? $"{span.Minutes}m {span.Seconds}s"
                : $"{span.Seconds}s";
    }
}
