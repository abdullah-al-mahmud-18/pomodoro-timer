namespace PomodoroTimer.Core.Sync;

public enum SyncAction
{
    /// <summary>Already in sync (or nothing to sync).</summary>
    None,

    /// <summary>No cloud file exists yet — upload the local db as a new file.</summary>
    CreateCloudFile,

    /// <summary>Local changes never reached the cloud — upload them.</summary>
    Upload,

    /// <summary>Cloud is newer (or the local db is empty/missing) — back up local, then download.</summary>
    Download,

    /// <summary>Both sides changed, or both have data with no sync history — the user must choose.</summary>
    Conflict,

    /// <summary>The cloud file this device synced with was deleted — ask before uploading a new one.</summary>
    CloudFileDeleted,

    /// <summary>More than one tagged cloud file was found — don't guess which is right.</summary>
    AmbiguousCloudFiles
}
