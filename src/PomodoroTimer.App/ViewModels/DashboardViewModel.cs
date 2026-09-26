using System.Collections.ObjectModel;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Services;

namespace PomodoroTimer.App.ViewModels;

/// <summary>Backs the Dashboard page: totals for today and rolling windows, plus per-day averages.</summary>
public class DashboardViewModel : ViewModelBase
{
    private readonly SessionRepository _sessionRepository;
    private readonly DashboardService _dashboardService;

    public DashboardViewModel(SessionRepository sessionRepository, DashboardService dashboardService)
    {
        _sessionRepository = sessionRepository;
        _dashboardService = dashboardService;

        TodayRows = new ObservableCollection<DashboardRowViewModel>();
        TotalRows = new ObservableCollection<DashboardRowViewModel>();
        AverageRows = new ObservableCollection<DashboardRowViewModel>();
    }

    public ObservableCollection<DashboardRowViewModel> TodayRows { get; }
    public ObservableCollection<DashboardRowViewModel> TotalRows { get; }
    public ObservableCollection<DashboardRowViewModel> AverageRows { get; }

    public void Refresh()
    {
        var sessions = _sessionRepository.GetAll();
        var report = _dashboardService.BuildReport(sessions);

        TodayRows.Clear();
        TodayRows.Add(new DashboardRowViewModel("Today", report.Today));

        TotalRows.Clear();
        TotalRows.Add(new DashboardRowViewModel("Last 7 days", report.Last7Days));
        TotalRows.Add(new DashboardRowViewModel("Last 14 days", report.Last14Days));
        TotalRows.Add(new DashboardRowViewModel("Last 30 days", report.Last30Days));
        TotalRows.Add(new DashboardRowViewModel("Last 90 days", report.Last90Days));
        TotalRows.Add(new DashboardRowViewModel("Last 365 days", report.Last365Days));

        AverageRows.Clear();
        AverageRows.Add(new DashboardRowViewModel("Last 7 days", report.AveragePerDayLast7Days));
        AverageRows.Add(new DashboardRowViewModel("Last 14 days", report.AveragePerDayLast14Days));
        AverageRows.Add(new DashboardRowViewModel("Last 30 days", report.AveragePerDayLast30Days));
        AverageRows.Add(new DashboardRowViewModel("Last 90 days", report.AveragePerDayLast90Days));
    }
}
