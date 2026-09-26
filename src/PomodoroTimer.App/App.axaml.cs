using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PomodoroTimer.App.Services;
using PomodoroTimer.App.ViewModels;
using PomodoroTimer.App.Views;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Services;

namespace PomodoroTimer.App;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PomodoroTimer",
                "pomodoro.db");

            var database = new Database(dbPath);
            var sessionRepository = new SessionRepository(database);
            var dashboardService = new DashboardService();
            var dashboardViewModel = new DashboardViewModel(sessionRepository, dashboardService);

            var notificationManager = NotificationManagerFactory.TryCreate();
            var notificationService = new NotificationService(notificationManager);
            var soundService = new SoundService();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(sessionRepository, notificationService, soundService, dashboardViewModel)
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                soundService.Dispose();
                (notificationManager as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
