using System.Collections.ObjectModel;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;

namespace PomodoroTimer.App.ViewModels;

/// <summary>
/// Backs the History page: the full session list filtered by Type (All/Timer/Stopwatch), Mode (All/Work/Study/Break),
/// Session (Completed/Stopped early/All) and a From/To date range (both days inclusive, local time). Filters default
/// to All / All / All / today - 7 days through today each time the page is opened. A cleared date leaves that end
/// of the range open.
/// </summary>
public class HistoryViewModel : ViewModelBase
{
    private readonly SessionRepository _sessionRepository;
    private readonly DashboardViewModel _dashboardViewModel;

    private FilterOption<TimerMode?> _selectedType;
    private FilterOption<SessionCategory?> _selectedMode;
    private FilterOption<bool?> _selectedStatus;
    private DateTime? _fromDate;
    private DateTime? _toDate;
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
            new("All", null),
            new("Completed", true),
            new("Stopped early", false)
        };

        _selectedType = TypeOptions[0];
        _selectedMode = ModeOptions[0];
        _selectedStatus = StatusOptions[0];
        _fromDate = DefaultFromDate;
        _toDate = DefaultToDate;
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

    private static DateTime DefaultFromDate => DateTime.Today.AddDays(-7);
    private static DateTime DefaultToDate => DateTime.Today;

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

    /// <summary>First day shown (inclusive), or null for no lower bound.</summary>
    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetField(ref _fromDate, value?.Date))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Last day shown (inclusive), or null for no upper bound.</summary>
    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetField(ref _toDate, value?.Date))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Resets all filters to their defaults (All / All / All / today - 7 days through today) and reloads. Call when the page is opened.</summary>
    public void ResetAndReload()
    {
        IsConfirmingDeleteFiltered = false;

        _selectedType = TypeOptions[0];
        _selectedMode = ModeOptions[0];
        _selectedStatus = StatusOptions[0];
        _fromDate = DefaultFromDate;
        _toDate = DefaultToDate;
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));

        ApplyFilters();
    }

    /// <summary>Re-reads the list with the current filters (e.g. after sync downloaded a newer database).</summary>
    public void Reload()
    {
        IsConfirmingDeleteFiltered = false;
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
        // Compare local calendar days, so a session counts on the day it started on this computer's clock.
        var from = FromDate;
        var toExclusive = ToDate?.AddDays(1);

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

            var startedLocal = session.StartedAt.ToLocalTime().DateTime;
            if (from is not null && startedLocal < from)
            {
                continue;
            }

            if (toExclusive is not null && startedLocal >= toExclusive)
            {
                continue;
            }

            Sessions.Add(new SessionItemViewModel(session));
        }
    }
}
