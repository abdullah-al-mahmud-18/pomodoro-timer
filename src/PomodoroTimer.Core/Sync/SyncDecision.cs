namespace PomodoroTimer.Core.Sync;

/// <summary>Everything the startup decision depends on.</summary>
/// <param name="LocalMd5">L — MD5 of the local db, or null if it doesn't exist.</param>
/// <param name="CloudMd5">C — the cloud file's MD5 (ignored when <paramref name="CloudExists"/> is false).</param>
/// <param name="SyncedMd5">S — the MD5 recorded at the last successful sync (ignored when <paramref name="StateExists"/> is false).</param>
/// <param name="CloudExists">A cloud file was found.</param>
/// <param name="StateExists">This device has a sync-state.json from an earlier sync.</param>
/// <param name="LocalHasSessions">The local db contains at least one session.</param>
/// <param name="CloudFileDeleted">sync-state.json names a cloud file that no longer exists.</param>
public sealed record SyncInputs(
    string? LocalMd5,
    string? CloudMd5,
    string? SyncedMd5,
    bool CloudExists,
    bool StateExists,
    bool LocalHasSessions,
    bool CloudFileDeleted = false);

/// <summary>The startup decision table, as a pure function.</summary>
public static class SyncDecision
{
    public static SyncAction Decide(SyncInputs inputs)
    {
        if (!inputs.CloudExists)
        {
            if (inputs.LocalMd5 is null)
            {
                return SyncAction.None;
            }

            return inputs.CloudFileDeleted ? SyncAction.CloudFileDeleted : SyncAction.CreateCloudFile;
        }

        // Byte-identical copies can't conflict, whatever the recorded state says (e.g. a previous upload
        // succeeded but the state file wasn't written before the app closed).
        if (inputs.LocalMd5 is not null && HashEquals(inputs.LocalMd5, inputs.CloudMd5))
        {
            return SyncAction.None;
        }

        if (!inputs.StateExists)
        {
            return inputs.LocalMd5 is null || !inputs.LocalHasSessions ? SyncAction.Download : SyncAction.Conflict;
        }

        // Nothing local to lose.
        if (inputs.LocalMd5 is null)
        {
            return SyncAction.Download;
        }

        var localChanged = !HashEquals(inputs.LocalMd5, inputs.SyncedMd5);
        var cloudChanged = !HashEquals(inputs.CloudMd5, inputs.SyncedMd5);

        return (localChanged, cloudChanged) switch
        {
            (false, false) => SyncAction.None,
            (false, true) => SyncAction.Download,
            (true, false) => SyncAction.Upload,
            (true, true) => SyncAction.Conflict
        };
    }

    public static bool HashEquals(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
