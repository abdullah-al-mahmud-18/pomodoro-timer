using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using PomodoroTimer.App.Services;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;
using PomodoroTimer.Core.Names;
using PomodoroTimer.Core.Services;
using Serilog;

namespace PomodoroTimer.App.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly SessionRepository _sessionRepository;
    private readonly NotificationService _notificationService;
    private readonly SoundService _soundService;
    private readonly DispatcherTimer _tickTimer;

    private TimerEngine? _engine;

    private string _sessionName = string.Empty;
    private IReadOnlyList<string> _names = Array.Empty<string>();
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
        HistoryViewModel historyViewModel,
        SyncStatusViewModel syncStatus)
    {
        _sessionRepository = sessionRepository;
        _notificationService = notificationService;
        _soundService = soundService;
        Dashboard = dashboardViewModel;
        History = historyViewModel;
        Sync = syncStatus;

        StartCommand = new RelayCommand(Start, () => !IsRunning && !IsPaused);
        PauseCommand = new RelayCommand(Pause, () => IsRunning);
        ResumeCommand = new RelayCommand(Resume, () => IsPaused);
        StopCommand = new RelayCommand(Stop, () => IsRunning || IsPaused);
        ShowHistoryCommand = new RelayCommand(() =>
        {
            CurrentPage = AppPage.History;
            History.ResetAndReload();
        });
        ShowDashboardCommand = new RelayCommand(() =>
        {
            CurrentPage = AppPage.Dashboard;
            Dashboard.Refresh();
        });
        ShowTimerCommand = new RelayCommand(() => CurrentPage = AppPage.Timer);

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tickTimer.Tick += (_, _) => OnTick();

        UpdateTimeDisplay();
    }

    public DashboardViewModel Dashboard { get; }
    public HistoryViewModel History { get; }
    public SyncStatusViewModel Sync { get; }

    /// <summary>Re-reads History and Dashboard after sync replaced the database, keeping the current filters.</summary>
    public void ReloadData()
    {
        History.Reload();
        Dashboard.Refresh();
    }

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
            }
        }
    }

    public bool IsTimerViewActive => _currentPage == AppPage.Timer;
    public bool IsHistoryViewActive => _currentPage == AppPage.History;
    public bool IsDashboardViewActive => _currentPage == AppPage.Dashboard;

    /// <summary>
    /// What's typed in the Name box. AutoCompleteBox sets null when cleared, so null is stored as empty.
    /// </summary>
    public string? SessionName
    {
        get => _sessionName;
        set => SetField(ref _sessionName, value ?? string.Empty);
    }

    /// <summary>The names from names.txt — the only names a session can be started with.</summary>
    public IReadOnlyList<string> Names
    {
        get => _names;
        private set => SetField(ref _names, value);
    }

    /// <summary>Reads names.txt (creating it empty if missing). On failure keeps the current list and shows why.</summary>
    public void ReloadNames()
    {
        try
        {
            Names = NameList.LoadOrCreate(AppPaths.NamesPath);
            Log.Information("Loaded {Count} names from {Path}", Names.Count, AppPaths.NamesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Couldn't read the names list at {Path}", AppPaths.NamesPath);
            ErrorMessage = $"Couldn't read the names list ({AppPaths.NamesPath}). {ErrorReporter.UserMessageFor(ex)}";
        }
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

        if (Names.Count == 0)
        {
            ErrorMessage = $"No names yet. Add names to {AppPaths.NamesPath}, one per line, then restart the app.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SessionName))
        {
            ErrorMessage = "Choose a name before starting.";
            return;
        }

        if (NameList.Find(Names, SessionName) is not { } name)
        {
            ErrorMessage = $"\"{SessionName.Trim()}\" isn't in your names list. Add it to {AppPaths.NamesPath} and restart the app, or choose a name from the list.";
            return;
        }
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

    /// <summary>
    /// The close prompt's title for a running or paused session, e.g. "A timer is running", or null when no session
    /// is in progress.
    /// </summary>
    public string? SessionInProgressDescription => _engine is null
        ? null
        : $"A {(_engine.Mode == TimerMode.Timer ? "timer" : "stopwatch")} is {(IsPaused ? "paused" : "running")}";

    /// <summary>The close prompt's explanation of what closing does to the session in progress.</summary>
    public string? SessionInProgressCloseMessage => _engine is null
        ? null
        : _engine.Mode == TimerMode.Timer
            ? $"Closing the app stops \"{_engine.Name}\" and saves it to History as stopped early."
            : $"Closing the app stops \"{_engine.Name}\" and saves it to History.";

    /// <summary>
    /// Called when the window is closing: a running or paused timer/stopwatch is stopped and saved exactly as if
    /// Stop had been pressed, so the shutdown sync uploads it. Does nothing when no session is in progress.
    /// </summary>
    public void SaveSessionInProgress()
    {
        if (_engine is null)
        {
            return;
        }

        Log.Information("Window closing with a {Mode} session in progress ({Name}); saving it before shutdown sync",
            _engine.Mode, _engine.Name);
        Stop();
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
            // The session was already saved and reset in the meantime (the window closed just as the timer finished).
            if (!ReferenceEquals(_engine, sender))
            {
                return;
            }

            try
            {
                _tickTimer.Stop();
                UpdateTimeDisplay();
                PersistSession();

                var name = _engine?.Name ?? DefaultName();
                _ = _notificationService.ShowAsync("Timer complete", $"\"{name}\" has finished.");
                _ = _soundService.PlayCompletionSoundAsync();
            }
            catch (Exception ex)
            {
                ErrorReporter.Report(ex, "Timer completion");
            }
            finally
            {
                ResetToConfiguring();
            }
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

        try
        {
            _sessionRepository.Add(session);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Keep the session's details in the log so it can be recovered by hand, and tell the user.
            Log.Error(ex, "Couldn't save session {Name} ({Mode}, {Category}): {ActualSeconds}s of {PlannedSeconds}s, {StartedAt:O} to {EndedAt:O}, completed {Completed}",
                session.Name, session.Mode, session.Category, session.ActualDurationSeconds, session.PlannedDurationSeconds,
                session.StartedAt, session.EndedAt, session.Completed);
            ErrorMessage = $"Couldn't save \"{session.Name}\". {ErrorReporter.UserMessageFor(ex)}";
        }
    }

    private void ResetToConfiguring()
    {
        _engine = null;
        IsRunning = false;
        IsPaused = false;
        Progress = 0;
        SelectedCategory = null;
        SessionName = string.Empty;
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
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
