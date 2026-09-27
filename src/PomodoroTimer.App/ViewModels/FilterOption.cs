namespace PomodoroTimer.App.ViewModels;

/// <summary>A single dropdown option: a display label paired with the underlying filter value.</summary>
public class FilterOption<T>
{
    public FilterOption(string label, T value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public T Value { get; }

    public override string ToString() => Label;
}
