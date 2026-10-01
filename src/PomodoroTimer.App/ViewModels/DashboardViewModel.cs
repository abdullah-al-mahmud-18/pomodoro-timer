using System.Collections.ObjectModel;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;
using PomodoroTimer.Core.Services;

namespace PomodoroTimer.App.ViewModels;

/// <summary>Backs the Dashboard page: totals for today and rolling windows, plus per-day averages and Work/Study/Break ratios.</summary>
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
        RatioRows = new ObservableCollection<DashboardRowViewModel>();
    }

    public ObservableCollection<DashboardRowViewModel> TodayRows { get; }
    public ObservableCollection<DashboardRowViewModel> TotalRows { get; }
    public ObservableCollection<DashboardRowViewModel> AverageRows { get; }
    public ObservableCollection<DashboardRowViewModel> RatioRows { get; }

    public void Refresh()
    {
        var sessions = _sessionRepository.GetAll();
        var report = _dashboardService.BuildReport(sessions);

        TodayRows.Clear();
        TodayRows.Add(new DashboardRowViewModel("Today", report.Today));

        TotalRows.Clear();
        TotalRows.Add(new DashboardRowViewModel("Last 7 days", report.Last7Days));
        TotalRows.Add(new DashboardRowViewModel("Last 14 days", report.Last14Days));
        TotalRows.Add(new DashboardRowViewModel("Last 1 month", report.Last30Days));
        TotalRows.Add(new DashboardRowViewModel("Last 3 months", report.Last90Days));
        TotalRows.Add(new DashboardRowViewModel("Last 6 months", report.Last180Days));
        TotalRows.Add(new DashboardRowViewModel("Last 1 year", report.Last365Days));

        AverageRows.Clear();
        AverageRows.Add(new DashboardRowViewModel("Last 7 days", report.AveragePerDayLast7Days));
        AverageRows.Add(new DashboardRowViewModel("Last 14 days", report.AveragePerDayLast14Days));
        AverageRows.Add(new DashboardRowViewModel("Last 1 month", report.AveragePerDayLast30Days));
        AverageRows.Add(new DashboardRowViewModel("Last 3 months", report.AveragePerDayLast90Days));
        AverageRows.Add(new DashboardRowViewModel("Last 6 months", report.AveragePerDayLast180Days));
        AverageRows.Add(new DashboardRowViewModel("Last 1 year", report.AveragePerDayLast365Days));

        RatioRows.Clear();
        RatioRows.Add(new DashboardRowViewModel("Last 7 days", CategoryRatio.From(report.Last7Days)));
        RatioRows.Add(new DashboardRowViewModel("Last 14 days", CategoryRatio.From(report.Last14Days)));
        RatioRows.Add(new DashboardRowViewModel("Last 1 month", CategoryRatio.From(report.Last30Days)));
        RatioRows.Add(new DashboardRowViewModel("Last 3 months", CategoryRatio.From(report.Last90Days)));
        RatioRows.Add(new DashboardRowViewModel("Last 6 months", CategoryRatio.From(report.Last180Days)));
        RatioRows.Add(new DashboardRowViewModel("Last 1 year", CategoryRatio.From(report.Last365Days)));
    }
}
