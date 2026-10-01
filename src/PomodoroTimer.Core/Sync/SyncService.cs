using System.Globalization;
using System.Security.Cryptography;
using Serilog;

namespace PomodoroTimer.Core.Sync;

/// <summary>What startup sync found, and the action the decision table picked.</summary>
public sealed record SyncPlan(
    SyncAction Action,
    CloudFileInfo? Cloud,
    SyncState? State,
    string? LocalMd5,
    int CloudMatchCount);

public enum SyncResultKind
{
    /// <summary>Nothing needed to move (in sync, or no local changes).</summary>
    NothingToDo,
    Uploaded,
    Downloaded,

    /// <summary>The downloaded file failed verification; the local db was left untouched.</summary>
    DownloadVerificationFailed,

    /// <summary>Shutdown upload skipped because the cloud copy changed since the last sync.</summary>
    SkippedCloudChanged,

    /// <summary>Shutdown upload skipped because the cloud file this device synced with is gone.</summary>
    SkippedCloudMissing,

    /// <summary>There is no local db to upload.</summary>
    NoLocalDatabase
}

/// <param name="BackupPath">The backup made before the local db was replaced, if any.</param>
public sealed record SyncResult(SyncResultKind Kind, string? BackupPath = null);

/// <summary>
/// Whole-file sync of one local file (the SQLite db, or the names list) against one cloud file, using content
/// hashes (L = local, C = cloud, S = last synced) to tell which side changed. Never merges and never overwrites
/// local data without a backup.
/// </summary>
public sealed class SyncService
{
    public const int BackupsToKeep = 5;
    private const string BackupInfix = ".bak-";

    private readonly ICloudFileStore _store;
    private readonly SyncedFile _file;
    private readonly SyncStateStore _stateStore;
    private readonly ILocalFileGate _gate;
    private readonly Func<DateTimeOffset> _now;

    public SyncService(
        ICloudFileStore store,
        string databasePath,
        string statePath,
        ILocalFileGate? gate = null,
        Func<DateTimeOffset>? clock = null)
        : this(store, new DatabaseSyncFile(databasePath), statePath, gate, clock)
    {
    }

    public SyncService(
        ICloudFileStore store,
        SyncedFile file,
        string statePath,
        ILocalFileGate? gate = null,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _file = file;
        _stateStore = new SyncStateStore(statePath);
        _gate = gate ?? new InlineFileGate();
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private static ILogger Logger => Log.ForContext<SyncService>();

    private string LocalPath => _file.LocalPath;

    private string AppDirectory => Path.GetDirectoryName(Path.GetFullPath(LocalPath))!;

    /// <summary>Gathers L, C, S and the flags, and runs the startup decision table. Changes nothing.</summary>
    public async Task<SyncPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var state = _stateStore.Load();
        var (localMd5, localHasContent) = await _gate.RunAsync(InspectLocalFile);

        CloudFileInfo? cloud;
        var cloudDeleted = false;
        if (state is not null)
        {
            cloud = await _store.GetMetadataAsync(state.FileId, cancellationToken);
            cloudDeleted = cloud is null;
        }
        else
        {
            var matches = await _store.FindAsync(cancellationToken);
            if (matches.Count > 1)
            {
                Logger.Warning("Found {Count} cloud files tagged as the {File}; not guessing which to use", matches.Count, _file.DisplayName);
                return new SyncPlan(SyncAction.AmbiguousCloudFiles, null, null, localMd5, matches.Count);
            }

            cloud = matches.Count == 1 ? matches[0] : null;
        }

        var action = SyncDecision.Decide(new SyncInputs(
            LocalMd5: localMd5,
            CloudMd5: cloud?.Md5,
            SyncedMd5: state?.SyncedMd5,
            CloudExists: cloud is not null,
            StateExists: state is not null,
            LocalHasSessions: localHasContent,
            CloudFileDeleted: cloudDeleted));

        Logger.Information(
            "Sync decision for {File} {Action}: L={LocalMd5} C={CloudMd5} S={SyncedMd5} stateExists={StateExists} localHasContent={LocalHasContent} cloudDeleted={CloudDeleted}",
            _file.DisplayName, action, localMd5, cloud?.Md5, state?.SyncedMd5, state is not null, localHasContent, cloudDeleted);

        return new SyncPlan(action, cloud, state, localMd5, cloud is null ? 0 : 1);
    }

    /// <summary>
    /// Carries out <paramref name="action"/> for a plan — normally <see cref="SyncPlan.Action"/>, or
    /// <see cref="SyncAction.Upload"/>/<see cref="SyncAction.Download"/> once the user has resolved a prompt.
    /// </summary>
    public async Task<SyncResult> ExecuteAsync(SyncPlan plan, SyncAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case SyncAction.None:
                // Identical content but the state file is missing or stale — record it so next time is a clean match.
                if (plan.Cloud is not null && SyncDecision.HashEquals(plan.LocalMd5, plan.Cloud.Md5)
                    && !SyncDecision.HashEquals(plan.State?.SyncedMd5, plan.LocalMd5))
                {
                    SaveState(plan.Cloud.Id, plan.LocalMd5!);
                }
                return new SyncResult(SyncResultKind.NothingToDo);

            case SyncAction.CreateCloudFile:
            case SyncAction.Upload:
                // No cloud file (first sync, or the user confirmed re-uploading a deleted one) → create; otherwise update.
                return await UploadAsync(plan.Cloud?.Id, cancellationToken);

            case SyncAction.Download:
                if (plan.Cloud is null)
                {
                    throw new InvalidOperationException("Cannot download: no cloud file was found.");
                }
                return await DownloadAsync(plan.Cloud, cancellationToken);

            default:
                throw new InvalidOperationException($"{action} must be resolved by the user before syncing.");
        }
    }

    /// <summary>
    /// Shutdown sync: uploads local changes unless nothing changed, or the cloud copy changed since the last
    /// sync (in which case the next startup shows the conflict prompt).
    /// </summary>
    public async Task<SyncResult> UploadOnShutdownAsync(CancellationToken cancellationToken)
    {
        var state = _stateStore.Load();
        var localMd5 = await _gate.RunAsync(() =>
        {
            _file.Release();
            return File.Exists(LocalPath) ? ComputeMd5(LocalPath) : null;
        });

        if (localMd5 is null)
        {
            Logger.Information("Shutdown sync ({File}): no local file, nothing to upload", _file.DisplayName);
            return new SyncResult(SyncResultKind.NoLocalDatabase);
        }

        if (state is not null && SyncDecision.HashEquals(localMd5, state.SyncedMd5))
        {
            Logger.Information("Shutdown sync ({File}): no local changes since last sync (L == S), skipping upload", _file.DisplayName);
            return new SyncResult(SyncResultKind.NothingToDo);
        }

        if (state is null)
        {
            var matches = await _store.FindAsync(cancellationToken);
            if (matches.Count == 0)
            {
                return await UploadAsync(null, cancellationToken);
            }

            if (matches.Count == 1 && SyncDecision.HashEquals(matches[0].Md5, localMd5))
            {
                SaveState(matches[0].Id, localMd5);
                return new SyncResult(SyncResultKind.NothingToDo);
            }

            Logger.Warning("Shutdown sync ({File}): a cloud copy exists but this device has never synced with it; not uploading. The next start will ask what to keep", _file.DisplayName);
            return new SyncResult(SyncResultKind.SkippedCloudChanged);
        }

        var cloud = await _store.GetMetadataAsync(state.FileId, cancellationToken);
        if (cloud is null)
        {
            Logger.Warning("Shutdown sync ({File}): cloud file {FileId} no longer exists; not uploading. The next start will ask before re-uploading", _file.DisplayName, state.FileId);
            return new SyncResult(SyncResultKind.SkippedCloudMissing);
        }

        if (SyncDecision.HashEquals(cloud.Md5, localMd5))
        {
            SaveState(cloud.Id, localMd5);
            return new SyncResult(SyncResultKind.NothingToDo);
        }

        if (!SyncDecision.HashEquals(cloud.Md5, state.SyncedMd5))
        {
            Logger.Warning("Shutdown sync ({File}): cloud copy changed since last sync (C={CloudMd5}, S={SyncedMd5}); not uploading. The next start will show the conflict prompt",
                _file.DisplayName, cloud.Md5, state.SyncedMd5);
            return new SyncResult(SyncResultKind.SkippedCloudChanged);
        }

        return await UploadAsync(cloud.Id, cancellationToken);
    }

    private async Task<SyncResult> UploadAsync(string? fileId, CancellationToken cancellationToken)
    {
        var snapshotPath = TempPath("upload");

        // Upload a byte-for-byte snapshot taken inside the gate, so a write during the upload can't tear it.
        // S is the hash of exactly what was uploaded; later local writes make L != S and are uploaded next time.
        var snapshotMd5 = await _gate.RunAsync(() =>
        {
            if (!File.Exists(LocalPath))
            {
                return null;
            }

            _file.Release();
            File.Copy(LocalPath, snapshotPath, overwrite: true);
            return ComputeMd5(snapshotPath);
        });

        if (snapshotMd5 is null)
        {
            Logger.Information("Upload skipped: no local {File}", _file.DisplayName);
            return new SyncResult(SyncResultKind.NoLocalDatabase);
        }

        try
        {
            var uploaded = fileId is null
                ? await _store.CreateAsync(snapshotPath, cancellationToken)
                : await _store.UpdateAsync(fileId, snapshotPath, cancellationToken);

            if (uploaded.Md5 is not null && !SyncDecision.HashEquals(uploaded.Md5, snapshotMd5))
            {
                throw new IOException($"Uploaded file checksum {uploaded.Md5} doesn't match the local copy {snapshotMd5}.");
            }

            SaveState(uploaded.Id, snapshotMd5);
            Logger.Information("Uploaded local {File} to cloud file {FileId} ({Action}), MD5 {Md5}",
                _file.DisplayName, uploaded.Id, fileId is null ? "created" : "updated", snapshotMd5);
            return new SyncResult(SyncResultKind.Uploaded);
        }
        finally
        {
            TryDelete(snapshotPath);
        }
    }

    private async Task<SyncResult> DownloadAsync(CloudFileInfo cloud, CancellationToken cancellationToken)
    {
        // Never download over the live file: temp file first, verify, then swap in atomically.
        var tempPath = TempPath("download");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await _store.DownloadAsync(cloud.Id, stream, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var downloadedMd5 = ComputeMd5(tempPath);
            if (cloud.Md5 is not null && !SyncDecision.HashEquals(downloadedMd5, cloud.Md5))
            {
                Logger.Error("Downloaded {File} MD5 {Downloaded} doesn't match cloud MD5 {Cloud}; local file left untouched",
                    _file.DisplayName, downloadedMd5, cloud.Md5);
                return new SyncResult(SyncResultKind.DownloadVerificationFailed);
            }

            if (_file.Verify(tempPath) is { } problem)
            {
                Logger.Error("Downloaded {File} failed verification ({Problem}); local file left untouched", _file.DisplayName, problem);
                return new SyncResult(SyncResultKind.DownloadVerificationFailed);
            }

            var backupPath = await _gate.RunAsync(() => ReplaceLocalFile(tempPath));
            SaveState(cloud.Id, downloadedMd5);

            Logger.Information("Downloaded cloud file {FileId} over the local {File}, MD5 {Md5}", cloud.Id, _file.DisplayName, downloadedMd5);
            return new SyncResult(SyncResultKind.Downloaded, backupPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Backs up the current local file (if any), then atomically moves the verified download over it.</summary>
    private string? ReplaceLocalFile(string verifiedPath)
    {
        _file.Release();
        _file.EnsureReplaceable();

        string? backupPath = null;
        if (File.Exists(LocalPath))
        {
            backupPath = CreateBackup();
        }

        File.Move(verifiedPath, LocalPath, overwrite: true);
        RotateBackups();
        return backupPath;
    }

    /// <summary>Copies the local file to e.g. pomodoro.db.bak-&lt;yyyyMMdd-HHmmss&gt; (UTC). Keeps the newest <see cref="BackupsToKeep"/>.</summary>
    public string CreateBackup()
    {
        var stamp = _now().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupPath = LocalPath + BackupInfix + stamp;
        for (var suffix = 1; File.Exists(backupPath); suffix++)
        {
            backupPath = $"{LocalPath}{BackupInfix}{stamp}-{suffix}";
        }

        File.Copy(LocalPath, backupPath);
        Logger.Information("Backed up local {File} to {BackupPath}", _file.DisplayName, backupPath);
        return backupPath;
    }

    private void RotateBackups()
    {
        var pattern = Path.GetFileName(LocalPath) + BackupInfix + "*";
        var stale = Directory.GetFiles(AppDirectory, pattern)
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(BackupsToKeep);

        foreach (var path in stale)
        {
            try
            {
                File.Delete(path);
                Logger.Information("Deleted old backup {BackupPath}", path);
            }
            catch (IOException ex)
            {
                Logger.Warning(ex, "Couldn't delete old backup {BackupPath}", path);
            }
        }
    }

    /// <summary>L plus whether the local file holds any data.</summary>
    private (string? Md5, bool HasContent) InspectLocalFile()
    {
        if (!File.Exists(LocalPath))
        {
            return (null, false);
        }

        var hasContent = _file.HasContent();
        _file.Release();
        return (ComputeMd5(LocalPath), hasContent);
    }

    private void SaveState(string fileId, string md5) => _stateStore.Save(new SyncState(fileId, md5, _now()));

    private string TempPath(string purpose) => Path.Combine(AppDirectory, $"{Path.GetFileName(LocalPath)}.{purpose}-tmp");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "Couldn't delete temporary file {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning(ex, "Couldn't delete temporary file {Path}", path);
        }
    }

    /// <summary>Lowercase hex MD5 — used only for change detection, not security. Matches Drive's md5Checksum format.</summary>
    public static string ComputeMd5(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }
}
