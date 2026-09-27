using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
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
/// Whole-file sync of the local SQLite db against one cloud file, using content hashes (L = local, C = cloud,
/// S = last synced) to tell which side changed. Never merges and never overwrites local data without a backup.
/// </summary>
public sealed class SyncService
{
    public const int BackupsToKeep = 5;
    private const string BackupInfix = ".bak-";

    private readonly ICloudFileStore _store;
    private readonly string _databasePath;
    private readonly SyncStateStore _stateStore;
    private readonly ILocalFileGate _gate;
    private readonly Func<DateTimeOffset> _now;

    public SyncService(
        ICloudFileStore store,
        string databasePath,
        string statePath,
        ILocalFileGate? gate = null,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _databasePath = databasePath;
        _stateStore = new SyncStateStore(statePath);
        _gate = gate ?? new InlineFileGate();
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private static ILogger Logger => Log.ForContext<SyncService>();

    private string AppDirectory => Path.GetDirectoryName(Path.GetFullPath(_databasePath))!;

    /// <summary>Gathers L, C, S and the flags, and runs the startup decision table. Changes nothing.</summary>
    public async Task<SyncPlan> PlanAsync(CancellationToken cancellationToken)
    {
        var state = _stateStore.Load();
        var (localMd5, localHasSessions) = await _gate.RunAsync(InspectLocalDatabase);

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
                Logger.Warning("Found {Count} cloud files tagged as the primary database; not guessing which to use", matches.Count);
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
            LocalHasSessions: localHasSessions,
            CloudFileDeleted: cloudDeleted));

        Logger.Information(
            "Sync decision {Action}: L={LocalMd5} C={CloudMd5} S={SyncedMd5} stateExists={StateExists} localHasSessions={LocalHasSessions} cloudDeleted={CloudDeleted}",
            action, localMd5, cloud?.Md5, state?.SyncedMd5, state is not null, localHasSessions, cloudDeleted);

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
            SqliteConnection.ClearAllPools();
            return File.Exists(_databasePath) ? ComputeMd5(_databasePath) : null;
        });

        if (localMd5 is null)
        {
            Logger.Information("Shutdown sync: no local database, nothing to upload");
            return new SyncResult(SyncResultKind.NoLocalDatabase);
        }

        if (state is not null && SyncDecision.HashEquals(localMd5, state.SyncedMd5))
        {
            Logger.Information("Shutdown sync: no local changes since last sync (L == S), skipping upload");
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

            Logger.Warning("Shutdown sync: a cloud database exists but this device has never synced with it; not uploading. The next start will ask what to keep");
            return new SyncResult(SyncResultKind.SkippedCloudChanged);
        }

        var cloud = await _store.GetMetadataAsync(state.FileId, cancellationToken);
        if (cloud is null)
        {
            Logger.Warning("Shutdown sync: cloud file {FileId} no longer exists; not uploading. The next start will ask before re-uploading", state.FileId);
            return new SyncResult(SyncResultKind.SkippedCloudMissing);
        }

        if (SyncDecision.HashEquals(cloud.Md5, localMd5))
        {
            SaveState(cloud.Id, localMd5);
            return new SyncResult(SyncResultKind.NothingToDo);
        }

        if (!SyncDecision.HashEquals(cloud.Md5, state.SyncedMd5))
        {
            Logger.Warning("Shutdown sync: cloud copy changed since last sync (C={CloudMd5}, S={SyncedMd5}); not uploading. The next start will show the conflict prompt",
                cloud.Md5, state.SyncedMd5);
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
            if (!File.Exists(_databasePath))
            {
                return null;
            }

            SqliteConnection.ClearAllPools();
            File.Copy(_databasePath, snapshotPath, overwrite: true);
            return ComputeMd5(snapshotPath);
        });

        if (snapshotMd5 is null)
        {
            Logger.Information("Upload skipped: no local database");
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
            Logger.Information("Uploaded local database to cloud file {FileId} ({Action}), MD5 {Md5}",
                uploaded.Id, fileId is null ? "created" : "updated", snapshotMd5);
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
                Logger.Error("Downloaded file MD5 {Downloaded} doesn't match cloud MD5 {Cloud}; local database left untouched",
                    downloadedMd5, cloud.Md5);
                return new SyncResult(SyncResultKind.DownloadVerificationFailed);
            }

            if (VerifyDatabase(tempPath) is { } problem)
            {
                Logger.Error("Downloaded database failed verification ({Problem}); local database left untouched", problem);
                return new SyncResult(SyncResultKind.DownloadVerificationFailed);
            }

            var backupPath = await _gate.RunAsync(() => ReplaceLocalDatabase(tempPath));
            SaveState(cloud.Id, downloadedMd5);

            Logger.Information("Downloaded cloud file {FileId} over the local database, MD5 {Md5}", cloud.Id, downloadedMd5);
            return new SyncResult(SyncResultKind.Downloaded, backupPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Backs up the current local db (if any), then atomically moves the verified download over it.</summary>
    private string? ReplaceLocalDatabase(string verifiedPath)
    {
        SqliteConnection.ClearAllPools();

        // A leftover rollback journal would be applied to the *new* file on next open and corrupt it.
        foreach (var sidecar in new[] { "-journal", "-wal" })
        {
            if (File.Exists(_databasePath + sidecar))
            {
                throw new IOException($"Not replacing the local database: {Path.GetFileName(_databasePath + sidecar)} exists, so it may be in use or need recovery.");
            }
        }

        string? backupPath = null;
        if (File.Exists(_databasePath))
        {
            backupPath = CreateBackup();
        }

        File.Move(verifiedPath, _databasePath, overwrite: true);
        RotateBackups();
        return backupPath;
    }

    /// <summary>Copies the local db to pomodoro.db.bak-&lt;yyyyMMdd-HHmmss&gt; (UTC). Keeps the newest <see cref="BackupsToKeep"/>.</summary>
    public string CreateBackup()
    {
        var stamp = _now().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupPath = _databasePath + BackupInfix + stamp;
        for (var suffix = 1; File.Exists(backupPath); suffix++)
        {
            backupPath = $"{_databasePath}{BackupInfix}{stamp}-{suffix}";
        }

        File.Copy(_databasePath, backupPath);
        Logger.Information("Backed up local database to {BackupPath}", backupPath);
        return backupPath;
    }

    private void RotateBackups()
    {
        var pattern = Path.GetFileName(_databasePath) + BackupInfix + "*";
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

    /// <summary>L plus whether the local db has any sessions. Opening it also rolls back any hot journal from a crash.</summary>
    private (string? Md5, bool HasSessions) InspectLocalDatabase()
    {
        if (!File.Exists(_databasePath))
        {
            return (null, false);
        }

        bool hasSessions;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE
                    WHEN NOT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'Sessions') THEN 0
                    ELSE (SELECT EXISTS (SELECT 1 FROM Sessions))
                END;
                """;
            hasSessions = Convert.ToInt64(command.ExecuteScalar()) != 0;
        }
        catch (SqliteException ex)
        {
            // Can't tell — assume it has data, so the worst case is a prompt rather than a silent overwrite.
            Logger.Warning(ex, "Couldn't check the local database for sessions; treating it as non-empty");
            hasSessions = true;
        }

        SqliteConnection.ClearAllPools();
        return (ComputeMd5(_databasePath), hasSessions);
    }

    /// <summary>Returns null if the file is a healthy database from this app, otherwise what's wrong with it.</summary>
    private static string? VerifyDatabase(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();

            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = integrity.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return $"integrity_check returned '{result}'";
            }

            using var table = connection.CreateCommand();
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Sessions';";
            return Convert.ToInt64(table.ExecuteScalar()) == 1 ? null : "no Sessions table";
        }
        catch (SqliteException ex)
        {
            return ex.Message;
        }
    }

    private void SaveState(string fileId, string md5) => _stateStore.Save(new SyncState(fileId, md5, _now()));

    private string TempPath(string purpose) => Path.Combine(AppDirectory, $"{Path.GetFileName(_databasePath)}.{purpose}-tmp");

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
