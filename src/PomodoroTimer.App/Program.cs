using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using PomodoroTimer.App.Services;
using Serilog;

namespace PomodoroTimer.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLogging();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            Log.Information("Pomodoro Timer starting (version {Version}, {OS})",
                typeof(Program).Assembly.GetName().Version, Environment.OSVersion);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Log.Information("Pomodoro Timer exited");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Pomodoro Timer crashed");
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();

    /// <summary>Daily rolling log files next to pomodoro.db. If logging can't be set up, the app runs without it.</summary>
    private static void ConfigureLogging()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    AppPaths.LogFilePattern,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch (Exception ex)
        {
            // A logging failure must never stop the app. Serilog's default (silent) logger stays in place.
            System.Diagnostics.Trace.WriteLine($"Logging setup failed: {ex}");
        }
    }
}
