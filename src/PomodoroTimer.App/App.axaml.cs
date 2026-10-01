using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DesktopNotifications;
using PomodoroTimer.App.Services;
using PomodoroTimer.App.ViewModels;
using PomodoroTimer.App.Views;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Services;
using Serilog;

namespace PomodoroTimer.App;

public class App : Application
{
    private SyncCoordinator? _sync;
    private MainWindow? _window;
    private SoundService? _soundService;
    private INotificationManager? _notificationManager;
    private bool _shutdownSyncStarted;
    private bool _closePromptOpen;
    private bool _readyToClose;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _sync = new SyncCoordinator(
                new GoogleAuthService(AppPaths.ClientSecretPath, AppPaths.GoogleTokenDirectory),
                new DispatcherFileGate(),
                () => _window);

            // The window opens in a "Syncing…" state; pages load only after startup sync, which must run
            // before the database is opened or migrated.
            _window = new MainWindow();
            _window.ShowBusyOverlay("Syncing…");
            _window.Opened += OnMainWindowOpened;
            _window.Closing += OnMainWindowClosing;
            desktop.MainWindow = _window;

            desktop.ShutdownRequested += (_, _) =>
            {
                _soundService?.Dispose();
                (_notificationManager as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        _window!.Opened -= OnMainWindowOpened;

        try
        {
            await _sync!.StartupAsync();
        }
        catch (Exception ex)
        {
            // StartupAsync handles its own failures; this is a last line of defence. Local use continues regardless.
            Log.Error(ex, "Startup sync failed");
        }

        try
        {
            LoadPages();
        }
        catch (Exception ex)
        {
            // Never delete or overwrite the db in response — just explain and leave it alone.
            Log.Error(ex, "Couldn't open the database at {Path}", AppPaths.DatabasePath);
            _window.ShowBusyOverlay($"{ErrorReporter.UserMessageFor(ex)}\n\nClose the app and try again. Log files: {AppPaths.AppDirectory}");
        }
    }

    private void LoadPages()
    {
        var database = new Database(AppPaths.DatabasePath);
        var sessionRepository = new SessionRepository(database);
        var dashboardService = new DashboardService();
        var dashboardViewModel = new DashboardViewModel(sessionRepository, dashboardService);
        var historyViewModel = new HistoryViewModel(sessionRepository, dashboardViewModel);

        _notificationManager = NotificationManagerFactory.TryCreate();
        var notificationService = new NotificationService(_notificationManager);
        _soundService = new SoundService();

        var mainViewModel = new MainWindowViewModel(
            sessionRepository, notificationService, _soundService, dashboardViewModel, historyViewModel, _sync!.Status);

        // A Retry/Sign in can download a newer db mid-session: migrate it and refresh what's on screen.
        _sync.LocalDatabaseReplaced += () =>
        {
            database.Initialize();
            mainViewModel.ReloadData();
        };

        // names.txt is read once here, after startup sync; a Retry/Sign in can download a newer one mid-session.
        mainViewModel.ReloadNames();
        _sync.NamesReplaced += mainViewModel.ReloadNames;

        _window!.DataContext = mainViewModel;
        _window.HideBusyOverlay();
    }

    /// <summary>
    /// Cancels the first close. If a timer/stopwatch is in progress, asks before stopping it; then saves it, uploads
    /// local changes (bounded by a timeout), and closes for real.
    /// </summary>
    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_readyToClose)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownSyncStarted || _closePromptOpen)
        {
            return;
        }

        try
        {
            // The OS is shutting down or logging off and won't wait for an answer: just save and sync.
            if (e.CloseReason != WindowCloseReason.OSShutdown && !await ConfirmCloseWithSessionInProgressAsync())
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // If the prompt itself fails, fall through to saving and closing rather than leaving the window stuck.
            Log.Error(ex, "Couldn't show the close prompt");
        }

        _shutdownSyncStarted = true;
        try
        {
            // Save a running/paused session first (as if Stop was pressed) so the shutdown sync uploads it.
            (_window!.DataContext as MainWindowViewModel)?.SaveSessionInProgress();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't save the session in progress at close");
        }

        try
        {
            _window!.ShowBusyOverlay("Syncing…");
            await _sync!.ShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown sync failed");
        }
        finally
        {
            _readyToClose = true;
            _window?.Close();
        }
    }

    /// <summary>
    /// "A timer is running — stop and close?" Returns true to go ahead (also when no session is in progress), false
    /// to cancel the close and keep the session going.
    /// </summary>
    private async Task<bool> ConfirmCloseWithSessionInProgressAsync()
    {
        if (_window!.DataContext is not MainWindowViewModel { SessionInProgressDescription: { } description } viewModel)
        {
            return true;
        }

        _closePromptOpen = true;
        try
        {
            var choice = await MessageDialog.ShowAsync(_window, $"{description} — stop and close?",
                viewModel.SessionInProgressCloseMessage!, "Stop and close", "Keep running");

            // Only an explicit "Stop and close" closes; "Keep running" or closing the dialog keeps the session going.
            var close = choice == 0;
            Log.Information("Close requested with a session in progress; user chose {Choice}", close ? "stop and close" : "keep running");
            return close;
        }
        finally
        {
            _closePromptOpen = false;
        }
    }

    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ErrorReporter.Report(e.Exception, "UI operation");
    }
}
