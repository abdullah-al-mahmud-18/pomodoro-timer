using System.Collections.ObjectModel;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;

namespace PomodoroTimer.App.ViewModels;

/// <summary>
/// Backs the History page: the full session list filtered by Type (All/Timer/Stopwatch), Mode (All/Work/Study/Break),
/// Session (Completed/Stopped early/All) and Period (rolling last-N-days window, or All). Filters default to
/// All / All / All / last 7 days each time the page is opened.
/// </summary>
public class HistoryViewModel : ViewModelBase
{
    private readonly SessionRepository _sessionRepository;
    private readonly DashboardViewModel _dashboardViewModel;

    private FilterOption<TimerMode?> _selectedType;
    private FilterOption<SessionCategory?> _selectedMode;
    private FilterOption<bool?> _selectedStatus;
    private FilterOption<HistoryPeriod> _selectedPeriod;
    private bool _isConfirmingDeleteFiltered;

    public HistoryViewModel(SessionRepository sessionRepository, DashboardViewModel dashboardViewModel)
    {
        _sessionRepository = sessionRepository;
        _dashboardViewModel = dashboardViewModel;

        Sessions = new ObservableCollection<SessionItemViewModel>();
        Sessions.CollectionChanged += (_, _) => DeleteFilteredCommand?.RaiseCanExecuteChanged();
        DeleteSessionCommand = new RelayCommand<SessionItemViewModel>(Delete);
        DeleteFilteredCommand = new RelayCommand(() => IsConfirmingDeleteFiltered = true, () => Sessions.Count > 0);
        ConfirmDeleteFilteredCommand = new RelayCommand(DeleteFiltered);
        CancelDeleteFilteredCommand = new RelayCommand(() => IsConfirmingDeleteFiltered = false);

        TypeOptions = new List<FilterOption<TimerMode?>>
        {
            new("All", null),
            new("Timer", TimerMode.Timer),
            new("Stopwatch", TimerMode.Stopwatch)
        };

        ModeOptions = new List<FilterOption<SessionCategory?>>
        {
            new("All", null),
            new("Work", SessionCategory.Work),
            new("Study", SessionCategory.Study),
            new("Break", SessionCategory.Break)
        };

        // Value is the session's Completed flag; null means All.
        StatusOptions = new List<FilterOption<bool?>>
        {
            new("Completed", true),
            new("Stopped early", false),
            new("All", null)
        };

        PeriodOptions = new List<FilterOption<HistoryPeriod>>
        {
            new("Last 7 days", HistoryPeriod.Last7Days),
            new("Last 14 days", HistoryPeriod.Last14Days),
            new("Last 30 days", HistoryPeriod.Last30Days),
            new("Last 180 days", HistoryPeriod.Last180Days),
            new("Last 365 days", HistoryPeriod.Last365Days),
            new("All", HistoryPeriod.All)
        };

        _selectedType = TypeOptions[0];
        _selectedMode = ModeOptions[0];
        _selectedStatus = DefaultStatus;
        _selectedPeriod = PeriodOptions[0];
    }

    public ObservableCollection<SessionItemViewModel> Sessions { get; }
    public RelayCommand<SessionItemViewModel> DeleteSessionCommand { get; }
    public RelayCommand DeleteFilteredCommand { get; }
    public RelayCommand ConfirmDeleteFilteredCommand { get; }
    public RelayCommand CancelDeleteFilteredCommand { get; }

    /// <summary>True while the "Are you sure?" prompt for Delete Filtered Data is showing.</summary>
    public bool IsConfirmingDeleteFiltered
    {
        get => _isConfirmingDeleteFiltered;
        private set
        {
            if (SetField(ref _isConfirmingDeleteFiltered, value))
            {
                OnPropertyChanged(nameof(DeleteFilteredPrompt));
            }
        }
    }

    public string DeleteFilteredPrompt => Sessions.Count == 1
        ? "This will permanently delete 1 session shown in the list. This cannot be undone."
        : $"This will permanently delete {Sessions.Count} sessions shown in the list. This cannot be undone.";

    public List<FilterOption<TimerMode?>> TypeOptions { get; }
    public List<FilterOption<SessionCategory?>> ModeOptions { get; }
    public List<FilterOption<bool?>> StatusOptions { get; }
    public List<FilterOption<HistoryPeriod>> PeriodOptions { get; }

    private FilterOption<bool?> DefaultStatus => StatusOptions[^1];

    public FilterOption<bool?> SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetField(ref _selectedStatus, value))
            {
                ApplyFilters();
            }
        }
    }

    public FilterOption<TimerMode?> SelectedType
    {
        get => _selectedType;
        set
        {
            if (SetField(ref _selectedType, value))
            {
                ApplyFilters();
            }
        }
    }

    public FilterOption<SessionCategory?> SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetField(ref _selectedMode, value))
            {
                ApplyFilters();
            }
        }
    }

    public FilterOption<HistoryPeriod> SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (SetField(ref _selectedPeriod, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Resets all filters to their defaults (All / All / All / last 7 days) and reloads. Call when the page is opened.</summary>
    public void ResetAndReload()
    {
        IsConfirmingDeleteFiltered = false;

        _selectedType = TypeOptions[0];
        _selectedMode = ModeOptions[0];
        _selectedStatus = DefaultStatus;
        _selectedPeriod = PeriodOptions[0];
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(SelectedPeriod));

        ApplyFilters();
    }

    private void Delete(SessionItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _sessionRepository.Delete(item.Id);
        ApplyFilters();
        _dashboardViewModel.Refresh();
    }

    /// <summary>Deletes every session currently shown by the active filters.</summary>
    private void DeleteFiltered()
    {
        IsConfirmingDeleteFiltered = false;
        _sessionRepository.DeleteMany(Sessions.Select(s => s.Id).ToList());
        ApplyFilters();
        _dashboardViewModel.Refresh();
    }

    private void ApplyFilters()
    {
        var cutoff = SelectedPeriod.Value == HistoryPeriod.All
            ? (DateTimeOffset?)null
            : DateTimeOffset.Now.Date.AddDays(-DaysFor(SelectedPeriod.Value) + 1);

        Sessions.Clear();
        foreach (var session in _sessionRepository.GetAll())
        {
            if (SelectedType.Value is { } type && session.Mode != type)
            {
                continue;
            }

            if (SelectedMode.Value is { } category && session.Category != category)
            {
                continue;
            }

            if (SelectedStatus.Value is { } completed && session.Completed != completed)
            {
                continue;
            }

            if (cutoff is not null && session.StartedAt.ToLocalTime() < cutoff)
            {
                continue;
            }

            Sessions.Add(new SessionItemViewModel(session));
        }
    }

    private static int DaysFor(HistoryPeriod period) => period switch
    {
        HistoryPeriod.Last7Days => 7,
        HistoryPeriod.Last14Days => 14,
        HistoryPeriod.Last30Days => 30,
        HistoryPeriod.Last180Days => 180,
        HistoryPeriod.Last365Days => 365,
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };
}
