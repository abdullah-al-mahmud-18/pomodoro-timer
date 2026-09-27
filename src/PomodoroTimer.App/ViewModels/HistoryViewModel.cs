using System.Collections.ObjectModel;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;

namespace PomodoroTimer.App.ViewModels;

/// <summary>
/// Backs the History page: the full session list filtered by Type (Timer/Stopwatch), Mode (Work/Study/Break),
/// and Period (rolling last-N-days window, or All). Filters default to Timer / Work / last 7 days each time
/// the page is opened.
/// </summary>
public class HistoryViewModel : ViewModelBase
{
    private readonly SessionRepository _sessionRepository;

    private FilterOption<TimerMode> _selectedType;
    private FilterOption<SessionCategory> _selectedMode;
    private FilterOption<HistoryPeriod> _selectedPeriod;

    public HistoryViewModel(SessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;

        Sessions = new ObservableCollection<SessionItemViewModel>();
        DeleteSessionCommand = new RelayCommand<SessionItemViewModel>(Delete);

        TypeOptions = new List<FilterOption<TimerMode>>
        {
            new("Timer", TimerMode.Timer),
            new("Stopwatch", TimerMode.Stopwatch)
        };

        ModeOptions = new List<FilterOption<SessionCategory>>
        {
            new("Work", SessionCategory.Work),
            new("Study", SessionCategory.Study),
            new("Break", SessionCategory.Break)
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
        _selectedPeriod = PeriodOptions[0];
    }

    public ObservableCollection<SessionItemViewModel> Sessions { get; }
    public RelayCommand<SessionItemViewModel> DeleteSessionCommand { get; }

    public List<FilterOption<TimerMode>> TypeOptions { get; }
    public List<FilterOption<SessionCategory>> ModeOptions { get; }
    public List<FilterOption<HistoryPeriod>> PeriodOptions { get; }

    public FilterOption<TimerMode> SelectedType
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

    public FilterOption<SessionCategory> SelectedMode
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

    /// <summary>Resets all filters to their defaults (Timer / Work / last 7 days) and reloads. Call when the page is opened.</summary>
    public void ResetAndReload()
    {
        _selectedType = TypeOptions[0];
        _selectedMode = ModeOptions[0];
        _selectedPeriod = PeriodOptions[0];
        OnPropertyChanged(nameof(SelectedType));
        OnPropertyChanged(nameof(SelectedMode));
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
    }

    private void ApplyFilters()
    {
        var cutoff = SelectedPeriod.Value == HistoryPeriod.All
            ? (DateTimeOffset?)null
            : DateTimeOffset.Now.Date.AddDays(-DaysFor(SelectedPeriod.Value) + 1);

        Sessions.Clear();
        foreach (var session in _sessionRepository.GetAll())
        {
            if (session.Mode != SelectedType.Value)
            {
                continue;
            }

            if (session.Category != SelectedMode.Value)
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
