using PomodoroTimer.Core.Models;

namespace PomodoroTimer.App.ViewModels;

public class PresetItemViewModel
{
    public Preset Preset { get; }

    public PresetItemViewModel(Preset preset)
    {
        Preset = preset;
    }

    public int Id => Preset.Id;
    public string Name => Preset.Name;
    public TimerMode Mode => Preset.Mode;

    public string DurationDisplay => Preset.Mode == TimerMode.Stopwatch
        ? "Stopwatch"
        : FormatDuration(Preset.DurationSeconds ?? 0);

    public string ModeDisplay => Preset.Mode == TimerMode.Timer ? "Timer" : "Stopwatch";

    private static string FormatDuration(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(totalSeconds);
        return span.Hours > 0
            ? $"{span.Hours}h {span.Minutes}m"
            : span.Minutes > 0
                ? $"{span.Minutes}m {span.Seconds}s"
                : $"{span.Seconds}s";
    }
}
