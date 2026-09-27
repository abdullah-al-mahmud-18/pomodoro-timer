using System;
using System.Threading.Tasks;
using PomodoroTimer.App.Services;

namespace PomodoroTimer.App.ViewModels;

/// <summary>
/// The small sync status shown next to the app title (e.g. "Synced 10:42", "Offline — will sync later"),
/// with an optional action button (Retry / Sign in).
/// </summary>
public class SyncStatusViewModel : ViewModelBase
{
    private string _statusText = "Syncing…";
    private string? _detail;
    private string? _actionText;
    private bool _isBusy = true;

    public SyncStatusViewModel()
    {
        ActionCommand = new RelayCommand(() => _ = RunActionAsync(), () => IsActionVisible);
    }

    /// <summary>What the action button does. Set by <see cref="SyncCoordinator"/>.</summary>
    public Func<Task>? ActionHandler { get; set; }

    public RelayCommand ActionCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Longer explanation, shown as a tooltip.</summary>
    public string? Detail
    {
        get => _detail;
        private set => SetField(ref _detail, value);
    }

    public string? ActionText
    {
        get => _actionText;
        private set
        {
            if (SetField(ref _actionText, value))
            {
                RaiseActionState();
            }
        }
    }

    public bool IsActionVisible => _actionText is not null && !_isBusy;

    public void SetBusy(string statusText)
    {
        _isBusy = true;
        StatusText = statusText;
        Detail = null;
        ActionText = null;
        RaiseActionState();
    }

    public void Set(string statusText, string? detail = null, string? actionText = null)
    {
        _isBusy = false;
        StatusText = statusText;
        Detail = detail;
        ActionText = actionText;
        RaiseActionState();
    }

    private void RaiseActionState()
    {
        OnPropertyChanged(nameof(IsActionVisible));
        ActionCommand.RaiseCanExecuteChanged();
    }

    private async Task RunActionAsync()
    {
        try
        {
            if (ActionHandler is not null)
            {
                await ActionHandler();
            }
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Sync retry");
        }
    }
}
