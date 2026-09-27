namespace PomodoroTimer.Core.Sync;

/// <summary>
/// Runs local database file operations (hashing, copying, replacing) where they can't interleave with the app's
/// own database writes. The app passes one that runs on the UI thread, since that's where all its writes happen.
/// </summary>
public interface ILocalFileGate
{
    Task<T> RunAsync<T>(Func<T> action);
}

/// <summary>Runs the action immediately on the calling thread — for startup, shutdown, and tests.</summary>
public sealed class InlineFileGate : ILocalFileGate
{
    public Task<T> RunAsync<T>(Func<T> action) => Task.FromResult(action());
}
