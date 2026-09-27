using System;
using Avalonia.Threading;
using PomodoroTimer.App.Services;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;
using PomodoroTimer.Core.Services;

namespace PomodoroTimer.App.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly SessionRepository _sessionRepository;
    private readonly NotificationService _notificationService;
    private readonly SoundService _soundService;
    private readonly DispatcherTimer _tickTimer;

    private TimerEngine? _engine;

    private string _sessionName = string.Empty;
    private bool _isTimerMode = true;
    private int _hours;
    private int _minutes = 25;
    private int _seconds;
    private string _timeDisplay = "00:00:00";
    private double _progress;
    private bool _isRunning;
    private bool _isPaused;
    private string? _errorMessage;
    private AppPage _currentPage = AppPage.Timer;
    private SessionCategory? _selectedCategory;

    public MainWindowViewModel(
        SessionRepository sessionRepository,
        NotificationService notificationService,
        SoundService soundService,
        DashboardViewModel dashboardViewModel,
        HistoryViewModel historyViewModel)
    {
        _sessionRepository = sessionRepository;
        _notificationService = notificationService;
        _soundService = soundService;
        Dashboard = dashboardViewModel;
        History = historyViewModel;

        StartCommand = new RelayCommand(Start, () => !IsRunning && !IsPaused);
        PauseCommand = new RelayCommand(Pause, () => IsRunning && IsTimerMode);
        ResumeCommand = new RelayCommand(Resume, () => IsPaused);
        StopCommand = new RelayCommand(Stop, () => IsRunning || IsPaused);
        ShowHistoryCommand = new RelayCommand(() => CurrentPage = AppPage.History);
        ShowDashboardCommand = new RelayCommand(() => CurrentPage = AppPage.Dashboard);
        ShowTimerCommand = new RelayCommand(() => CurrentPage = AppPage.Timer);

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tickTimer.Tick += (_, _) => OnTick();

        UpdateTimeDisplay();
    }

    public DashboardViewModel Dashboard { get; }
    public HistoryViewModel History { get; }

    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ShowHistoryCommand { get; }
    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowTimerCommand { get; }

    public AppPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetField(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsTimerViewActive));
                OnPropertyChanged(nameof(IsHistoryViewActive));
                OnPropertyChanged(nameof(IsDashboardViewActive));

                if (value == AppPage.History)
                {
                    History.ResetAndReload();
                }
                else if (value == AppPage.Dashboard)
                {
                    Dashboard.Refresh();
                }
            }
        }
    }

    public bool IsTimerViewActive => _currentPage == AppPage.Timer;
    public bool IsHistoryViewActive => _currentPage == AppPage.History;
    public bool IsDashboardViewActive => _currentPage == AppPage.Dashboard;

    public string SessionName
    {
        get => _sessionName;
        set => SetField(ref _sessionName, value);
    }

    public bool IsTimerMode
    {
        get => _isTimerMode;
        set
        {
            if (SetField(ref _isTimerMode, value))
            {
                OnPropertyChanged(nameof(IsStopwatchMode));
                PauseCommand.RaiseCanExecuteChanged();
                UpdateTimeDisplay();
            }
        }
    }

    public bool IsStopwatchMode
    {
        get => !_isTimerMode;
        set => IsTimerMode = !value;
    }

    public SessionCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                OnPropertyChanged(nameof(IsWorkCategory));
                OnPropertyChanged(nameof(IsStudyCategory));
                OnPropertyChanged(nameof(IsBreakCategory));
            }
        }
    }

    public bool IsWorkCategory
    {
        get => _selectedCategory == SessionCategory.Work;
        set { if (value) SelectedCategory = SessionCategory.Work; }
    }

    public bool IsStudyCategory
    {
        get => _selectedCategory == SessionCategory.Study;
        set { if (value) SelectedCategory = SessionCategory.Study; }
    }

    public bool IsBreakCategory
    {
        get => _selectedCategory == SessionCategory.Break;
        set { if (value) SelectedCategory = SessionCategory.Break; }
    }

    public int Hours
    {
        get => _hours;
        private set { if (SetField(ref _hours, value)) OnPropertyChanged(nameof(HoursText)); }
    }

    public int Minutes
    {
        get => _minutes;
        private set { if (SetField(ref _minutes, value)) OnPropertyChanged(nameof(MinutesText)); }
    }

    public int Seconds
    {
        get => _seconds;
        private set { if (SetField(ref _seconds, value)) OnPropertyChanged(nameof(SecondsText)); }
    }

    public string HoursText
    {
        get => _hours.ToString();
        set { Hours = Math.Max(0, ParseOrZero(value)); UpdateTimeDisplay(); }
    }

    public string MinutesText
    {
        get => _minutes.ToString();
        set { Minutes = Math.Clamp(ParseOrZero(value), 0, 59); UpdateTimeDisplay(); }
    }

    public string SecondsText
    {
        get => _seconds.ToString();
        set { Seconds = Math.Clamp(ParseOrZero(value), 0, 59); UpdateTimeDisplay(); }
    }

    private static int ParseOrZero(string? text) => int.TryParse(text, out var value) ? value : 0;

    public string TimeDisplay
    {
        get => _timeDisplay;
        private set => SetField(ref _timeDisplay, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                RaiseAllCommandStates();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetField(ref _isPaused, value))
            {
                RaiseAllCommandStates();
            }
        }
    }

    public bool IsConfiguring => !IsRunning && !IsPaused;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    private void RaiseAllCommandStates()
    {
        OnPropertyChanged(nameof(IsConfiguring));
        StartCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    private TimeSpan CustomDuration => new(_hours, _minutes, _seconds);

    private void Start()
    {
        ErrorMessage = null;

        if (SelectedCategory is not { } category)
        {
            ErrorMessage = "Select a mode (Work, Study, or Break) before starting.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(SessionName) ? DefaultName() : SessionName.Trim();
        var mode = IsTimerMode ? TimerMode.Timer : TimerMode.Stopwatch;

        if (mode == TimerMode.Timer && CustomDuration <= TimeSpan.Zero)
        {
            ErrorMessage = "Set a duration greater than zero to start a timer.";
            return;
        }

        _engine = new TimerEngine();
        _engine.Completed += OnEngineCompleted;
        _engine.Start(name, mode, category, mode == TimerMode.Timer ? CustomDuration : null);

        IsRunning = true;
        IsPaused = false;
        _tickTimer.Start();
        UpdateTimeDisplay();
    }

    private static string DefaultName() => "Untitled session";

    private void Pause()
    {
        _engine?.Pause();
        IsRunning = false;
        IsPaused = true;
        _tickTimer.Stop();
        UpdateTimeDisplay();
    }

    private void Resume()
    {
        _engine?.Resume();
        IsRunning = true;
        IsPaused = false;
        _tickTimer.Start();
    }

    private void Stop()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.Stop();
        _tickTimer.Stop();
        PersistSession();
        ResetToConfiguring();
    }

    private void OnTick()
    {
        _engine?.Tick();
        UpdateTimeDisplay();
    }

    private void OnEngineCompleted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _tickTimer.Stop();
            UpdateTimeDisplay();
            PersistSession();

            var name = _engine?.Name ?? DefaultName();
            _ = _notificationService.ShowAsync("Timer complete", $"\"{name}\" has finished.");
            _ = _soundService.PlayCompletionSoundAsync();

            ResetToConfiguring();
        });
    }

    private void PersistSession()
    {
        if (_engine is null)
        {
            return;
        }

        var session = _engine.ToSession(DateTimeOffset.UtcNow);
        if (session.ActualDurationSeconds <= 0)
        {
            return;
        }

        _sessionRepository.Add(session);
    }

    private void ResetToConfiguring()
    {
        _engine = null;
        IsRunning = false;
        IsPaused = false;
        Progress = 0;
        SelectedCategory = null;
        UpdateTimeDisplay();
    }

    private void UpdateTimeDisplay()
    {
        if (_engine is not null && (IsRunning || IsPaused))
        {
            var elapsed = _engine.Elapsed;
            TimeDisplay = FormatTimeSpan(_engine.Mode == TimerMode.Timer ? _engine.Remaining : elapsed);

            if (_engine.Mode == TimerMode.Timer && _engine.PlannedDuration is { } planned && planned > TimeSpan.Zero)
            {
                Progress = Math.Clamp(elapsed.TotalSeconds / planned.TotalSeconds, 0, 1) * 100;
            }
            else
            {
                Progress = 0;
            }
        }
        else
        {
            TimeDisplay = IsTimerMode ? FormatTimeSpan(CustomDuration) : "00:00:00";
            Progress = 0;
        }
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
