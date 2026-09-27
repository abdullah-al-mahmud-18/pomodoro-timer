using System;
using System.IO;

namespace PomodoroTimer.App.Services;

/// <summary>
/// The per-user app directory (the folder holding pomodoro.db) and every file the app keeps there.
/// Windows: %LOCALAPPDATA%\PomodoroTimer. Linux: ~/.local/share/PomodoroTimer.
/// </summary>
public static class AppPaths
{
    public static string AppDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PomodoroTimer");

    public static string DatabasePath => Path.Combine(AppDirectory, "pomodoro.db");

    /// <summary>Per-device sync bookkeeping. Never uploaded.</summary>
    public static string SyncStatePath => Path.Combine(AppDirectory, "sync-state.json");

    /// <summary>OAuth refresh token folder. Never uploaded, never logged.</summary>
    public static string GoogleTokenDirectory => Path.Combine(AppDirectory, "google-token");

    /// <summary>Serilog appends the date, giving pomodoro-yyyyMMdd.log.</summary>
    public static string LogFilePattern => Path.Combine(AppDirectory, "pomodoro-.log");

    /// <summary>OAuth client credentials, next to the executable. Not committed to git.</summary>
    public static string ClientSecretPath => Path.Combine(AppContext.BaseDirectory, "client_secret.json");
}
