using System;
using System.Windows.Input;
using PomodoroTimer.App.Services;

namespace PomodoroTimer.App.ViewModels;

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    /// <summary>UI boundary: a failing command is logged and reported, never allowed to crash the app.</summary>
    public void Execute(object? parameter)
    {
        try
        {
            _execute((T?)parameter);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Command");
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
