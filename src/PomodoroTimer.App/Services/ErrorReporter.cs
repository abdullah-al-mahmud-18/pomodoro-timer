using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using PomodoroTimer.App.Views;
using Serilog;

namespace PomodoroTimer.App.Services;

/// <summary>Logs an exception caught at a UI boundary and tells the user in plain words. Never throws.</summary>
public static class ErrorReporter
{
    private static bool _isShowing;

    public static void Report(Exception exception, string context)
    {
        try
        {
            Log.Error(exception, "{Context} failed", context);
        }
        catch
        {
            // Logging must never take the app down.
        }

        Dispatcher.UIThread.Post(() => Show(UserMessageFor(exception)));
    }

    public static string UserMessageFor(Exception exception) => exception switch
    {
        SqliteException => "Couldn't read or save your data. Make sure the disk isn't full and no other program is using pomodoro.db. Nothing was deleted.",
        IOException or UnauthorizedAccessException => "Couldn't access a file the app needs. Make sure the disk isn't full and you have permission to use the app's folder. Nothing was deleted.",
        _ => "Something went wrong. The app will keep running; the details were saved to the log file."
    };

    private static async void Show(string message)
    {
        // One dialog at a time, so a repeating failure can't stack up windows.
        if (_isShowing)
        {
            return;
        }

        try
        {
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is null || !owner.IsVisible)
            {
                return;
            }

            _isShowing = true;
            await MessageDialog.ShowAsync(owner, "Something went wrong", $"{message}\n\nLog files: {AppPaths.AppDirectory}", "OK");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't show error dialog");
        }
        finally
        {
            _isShowing = false;
        }
    }
}
