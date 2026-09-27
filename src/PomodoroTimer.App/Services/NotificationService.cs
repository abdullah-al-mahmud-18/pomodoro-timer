using System;
using System.Threading.Tasks;
using DesktopNotifications;
using Serilog;

namespace PomodoroTimer.App.Services;

/// <summary>Thin wrapper around <see cref="INotificationManager"/> that no-ops when the backend is unavailable.</summary>
public class NotificationService
{
    private readonly INotificationManager? _manager;

    public NotificationService(INotificationManager? manager)
    {
        _manager = manager;
    }

    public async Task ShowAsync(string title, string body)
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            await _manager.ShowNotification(new Notification
            {
                Title = title,
                Body = body
            });
        }
        catch (Exception ex)
        {
            // Never let a notification failure interrupt the timer flow; the session is already saved.
            Log.Warning(ex, "Couldn't show notification");
        }
    }
}
