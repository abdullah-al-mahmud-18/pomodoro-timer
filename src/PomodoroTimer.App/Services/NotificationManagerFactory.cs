using System;
using System.Threading.Tasks;
using DesktopNotifications;
using DesktopNotifications.FreeDesktop;
using Serilog;
#if !LINUX_BUILD
using DesktopNotifications.Windows;
#endif

namespace PomodoroTimer.App.Services;

/// <summary>
/// Constructs the platform-appropriate <see cref="INotificationManager"/> directly against the
/// DesktopNotifications.Windows / DesktopNotifications.FreeDesktop backends (bypassing
/// DesktopNotifications.Avalonia, whose AppBuilder glue targets a pre-1.0 Avalonia API and does not
/// compile against Avalonia 11).
///
/// DesktopNotifications.Windows is only referenced under the net10.0-windows10.0.19041.0 TFM (see the
/// csproj) because its Microsoft.Toolkit.Uwp.Notifications dependency needs a Windows-flavored TFM to
/// resolve to a WinRT-capable build; on the plain net10.0 TFM used for linux-x64 publishing that type
/// isn't usable, hence the LINUX_BUILD guard.
/// </summary>
public static class NotificationManagerFactory
{
    public static INotificationManager? TryCreate()
    {
        try
        {
#if !LINUX_BUILD
            if (OperatingSystem.IsWindows())
            {
                var context = WindowsApplicationContext.FromCurrentProcess();
                var manager = new WindowsNotificationManager(context);
                manager.Initialize().GetAwaiter().GetResult();
                return manager;
            }
#endif

            if (OperatingSystem.IsLinux())
            {
                var context = FreeDesktopApplicationContext.FromCurrentProcess();
                var manager = new FreeDesktopNotificationManager(context);
                // Called on the UI thread: blocking on Initialize() directly deadlocks, because the D-Bus
                // continuation tries to resume on the (blocked) UI thread. Run it on the thread pool instead,
                // and bound it so an unresponsive D-Bus session can't hang startup.
                if (!Task.Run(() => manager.Initialize()).Wait(TimeSpan.FromSeconds(5)))
                {
                    Log.Warning("Notification backend didn't respond; completion notifications are disabled");
                    return null;
                }
                return manager;
            }
        }
        catch (Exception ex)
        {
            // No notification backend available (e.g. no D-Bus session) — app still runs without toasts.
            Log.Warning(ex, "Notification backend unavailable; completion notifications are disabled");
        }

        return null;
    }
}
