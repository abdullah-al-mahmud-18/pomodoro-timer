using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using PomodoroTimer.Core.Sync;

namespace PomodoroTimer.App.Services;

/// <summary>
/// Runs sync's local file operations on the UI thread. All database writes (saving a session, deleting history)
/// also happen there, so a snapshot or file swap can never interleave with a write.
/// </summary>
public sealed class DispatcherFileGate : ILocalFileGate
{
    public Task<T> RunAsync<T>(Func<T> action) =>
        Dispatcher.UIThread.CheckAccess()
            ? Task.FromResult(action())
            : Dispatcher.UIThread.InvokeAsync(action).GetTask();
}
