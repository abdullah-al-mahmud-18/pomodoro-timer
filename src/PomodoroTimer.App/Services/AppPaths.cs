using System;
using System.IO;

namespace PomodoroTimer.App.Services;

/// <summary>
/// The per-user app directory (the folder holding pomodoro.db) and every file the app keeps there.
/// Windows: %LOCALAPPDATA%\PomodoroTimer. Linux: ~/.local/share/PomodoroTimer.
/// Debug builds (dotnet run, scripts/run.*) use a separate PomodoroTimer-Dev folder instead, so development never
/// touches the real history, sync state, token, or backups of a published build on the same machine.
/// </summary>
public static class AppPaths
{
#if DEBUG
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif

    public static string AppDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        IsDevelopment ? "PomodoroTimer-Dev" : "PomodoroTimer");

    public static string DatabasePath => Path.Combine(AppDirectory, "pomodoro.db");

    /// <summary>Session names the user can pick, one per line. Edited by the user; synced like the db.</summary>
    public static string NamesPath => Path.Combine(AppDirectory, "names.txt");

    /// <summary>Per-device sync bookkeeping for the db. Never uploaded.</summary>
    public static string SyncStatePath => Path.Combine(AppDirectory, "sync-state.json");

    /// <summary>Per-device sync bookkeeping for names.txt. Never uploaded.</summary>
    public static string NamesSyncStatePath => Path.Combine(AppDirectory, "sync-state-names.json");

    /// <summary>OAuth refresh token folder. Never uploaded, never logged.</summary>
    public static string GoogleTokenDirectory => Path.Combine(AppDirectory, "google-token");

    /// <summary>Serilog appends the date, giving pomodoro-yyyyMMdd.log.</summary>
    public static string LogFilePattern => Path.Combine(AppDirectory, "pomodoro-.log");

    /// <summary>OAuth client credentials, next to the executable. Not committed to git.</summary>
    public static string ClientSecretPath => Path.Combine(AppContext.BaseDirectory, "client_secret.json");
}
