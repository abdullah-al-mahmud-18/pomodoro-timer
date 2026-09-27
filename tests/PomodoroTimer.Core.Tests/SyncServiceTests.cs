using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;
using PomodoroTimer.Core.Sync;
using Xunit;

namespace PomodoroTimer.Core.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _dbPath;
    private readonly string _statePath;
    private readonly FakeCloudFileStore _cloud = new();
    private DateTimeOffset _now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    public SyncServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pomodoro-sync-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_directory);
        _dbPath = Path.Combine(_directory, "pomodoro.db");
        _statePath = Path.Combine(_directory, "sync-state.json");
    }

    private SyncService CreateService() => new(_cloud, _dbPath, _statePath, clock: () => _now);

    private SyncState? LoadState() => new SyncStateStore(_statePath).Load();

    [Fact]
    public async Task FirstSync_NoCloudFile_CreatesItAndRecordsState()
    {
        CreateDatabase(_dbPath, "Local session");
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncAction.CreateCloudFile, plan.Action);
        Assert.Equal(SyncResultKind.Uploaded, result.Kind);
        var file = Assert.Single(_cloud.Files);
        Assert.Equal(File.ReadAllBytes(_dbPath), file.Value);

        var state = LoadState();
        Assert.NotNull(state);
        Assert.Equal(file.Key, state!.FileId);
        Assert.Equal(SyncService.ComputeMd5(_dbPath), state.SyncedMd5);
        Assert.Equal(_now, state.LastSyncedUtc);
    }

    [Fact]
    public async Task NewDevice_WithEmptyLocalDb_DownloadsCloudCopy()
    {
        var cloudId = _cloud.Seed(CreateDatabaseBytes("From other device"));
        CreateDatabase(_dbPath);
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncAction.Download, plan.Action);
        Assert.Equal(SyncResultKind.Downloaded, result.Kind);
        Assert.Equal(_cloud.Files[cloudId], File.ReadAllBytes(_dbPath));
        Assert.Equal(new[] { "From other device" }, SessionNames(_dbPath));
        Assert.Equal(cloudId, LoadState()!.FileId);
    }

    [Fact]
    public async Task NewDevice_WithLocalSessions_IsConflict_AndCannotBeExecutedUnresolved()
    {
        _cloud.Seed(CreateDatabaseBytes("From other device"));
        CreateDatabase(_dbPath, "Local session");
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);

        Assert.Equal(SyncAction.Conflict, plan.Action);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(plan, plan.Action, CancellationToken.None));
        Assert.Equal(new[] { "Local session" }, SessionNames(_dbPath));
    }

    [Fact]
    public async Task Conflict_KeepThisDevice_UpdatesTheSameCloudFile()
    {
        var cloudId = _cloud.Seed(CreateDatabaseBytes("From other device"));
        CreateDatabase(_dbPath, "Local session");
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        await service.ExecuteAsync(plan, SyncAction.Upload, CancellationToken.None);

        var file = Assert.Single(_cloud.Files);
        Assert.Equal(cloudId, file.Key);
        Assert.Equal(File.ReadAllBytes(_dbPath), file.Value);
    }

    [Fact]
    public async Task CloudNewer_BacksUpLocalThenDownloads()
    {
        await SyncedDevice("Shared session");
        var localBefore = File.ReadAllBytes(_dbPath);
        var cloudId = LoadState()!.FileId;
        _cloud.Files[cloudId] = CreateDatabaseBytes("Shared session", "Added on other device");
        _now = _now.AddHours(1);
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncAction.Download, plan.Action);
        Assert.Equal(SyncResultKind.Downloaded, result.Kind);
        Assert.Equal(new[] { "Added on other device", "Shared session" }, SessionNames(_dbPath).OrderBy(n => n));

        Assert.NotNull(result.BackupPath);
        Assert.Equal(_dbPath + ".bak-20260927-110000", result.BackupPath);
        Assert.Equal(localBefore, File.ReadAllBytes(result.BackupPath!));
        Assert.Equal(SyncService.ComputeMd5(_dbPath), LoadState()!.SyncedMd5);
    }

    [Fact]
    public async Task LocalChangesNeverUploaded_UploadsToSameFile()
    {
        await SyncedDevice("Shared session");
        var cloudId = LoadState()!.FileId;
        AddSession(_dbPath, "Added offline");
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncAction.Upload, plan.Action);
        Assert.Equal(SyncResultKind.Uploaded, result.Kind);
        var file = Assert.Single(_cloud.Files);
        Assert.Equal(cloudId, file.Key);
        Assert.Equal(File.ReadAllBytes(_dbPath), file.Value);
        Assert.Equal(1, _cloud.CreateCount);
    }

    [Fact]
    public async Task InSync_DoesNothing()
    {
        await SyncedDevice("Shared session");
        var uploadsBefore = _cloud.UploadCount;
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncAction.None, plan.Action);
        Assert.Equal(SyncResultKind.NothingToDo, result.Kind);
        Assert.Equal(uploadsBefore, _cloud.UploadCount);
        Assert.Equal(0, _cloud.DownloadCount);
    }

    [Fact]
    public async Task DeletedCloudFile_IsReportedNotRecreated()
    {
        await SyncedDevice("Shared session");
        _cloud.Files.Clear();
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);

        Assert.Equal(SyncAction.CloudFileDeleted, plan.Action);
        Assert.Empty(_cloud.Files);
    }

    [Fact]
    public async Task SeveralTaggedCloudFiles_AreNotGuessedBetween()
    {
        _cloud.Seed(CreateDatabaseBytes("One"));
        _cloud.Seed(CreateDatabaseBytes("Two"));
        CreateDatabase(_dbPath, "Local session");

        var plan = await CreateService().PlanAsync(CancellationToken.None);

        Assert.Equal(SyncAction.AmbiguousCloudFiles, plan.Action);
        Assert.Equal(2, plan.CloudMatchCount);
    }

    [Fact]
    public async Task FailedDownloadVerification_LeavesLocalDbUntouched()
    {
        await SyncedDevice("Shared session");
        var localBefore = File.ReadAllBytes(_dbPath);
        var stateBefore = LoadState();
        _cloud.Files[stateBefore!.FileId] = "this is not a sqlite database"u8.ToArray();
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncAction.Download, plan.Action);
        Assert.Equal(SyncResultKind.DownloadVerificationFailed, result.Kind);
        Assert.Equal(localBefore, File.ReadAllBytes(_dbPath));
        Assert.Equal(stateBefore, LoadState());
        Assert.Empty(Backups());
        Assert.Empty(Directory.GetFiles(_directory, "*-tmp"));
    }

    [Fact]
    public async Task DownloadWithWrongChecksum_LeavesLocalDbUntouched()
    {
        await SyncedDevice("Shared session");
        var localBefore = File.ReadAllBytes(_dbPath);
        var cloudId = LoadState()!.FileId;
        _cloud.Files[cloudId] = CreateDatabaseBytes("Newer");
        _cloud.ReportedMd5Override = "00000000000000000000000000000000";
        var service = CreateService();

        var plan = await service.PlanAsync(CancellationToken.None);
        var result = await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);

        Assert.Equal(SyncResultKind.DownloadVerificationFailed, result.Kind);
        Assert.Equal(localBefore, File.ReadAllBytes(_dbPath));
    }

    [Fact]
    public async Task Backups_KeepOnlyTheFiveMostRecent()
    {
        await SyncedDevice("Shared session");
        var cloudId = LoadState()!.FileId;

        for (var i = 0; i < 7; i++)
        {
            _now = _now.AddMinutes(1);
            _cloud.Files[cloudId] = CreateDatabaseBytes($"Version {i}");
            var service = CreateService();
            var plan = await service.PlanAsync(CancellationToken.None);
            Assert.Equal(SyncAction.Download, plan.Action);
            await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);
        }

        var backups = Backups().Select(path => Path.GetFileName(path)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(SyncService.BackupsToKeep, backups.Count);
        Assert.Equal(
            new[] { "10:03", "10:04", "10:05", "10:06", "10:07" }.Select(t => $"pomodoro.db.bak-20260927-{t.Replace(":", "")}00"),
            backups);
    }

    [Fact]
    public async Task Shutdown_NoLocalChanges_SkipsUpload()
    {
        await SyncedDevice("Shared session");
        var uploadsBefore = _cloud.UploadCount;

        var result = await CreateService().UploadOnShutdownAsync(CancellationToken.None);

        Assert.Equal(SyncResultKind.NothingToDo, result.Kind);
        Assert.Equal(uploadsBefore, _cloud.UploadCount);
    }

    [Fact]
    public async Task Shutdown_LocalChanged_CloudUnchanged_Uploads()
    {
        await SyncedDevice("Shared session");
        AddSession(_dbPath, "New this session");

        var result = await CreateService().UploadOnShutdownAsync(CancellationToken.None);

        Assert.Equal(SyncResultKind.Uploaded, result.Kind);
        Assert.Equal(File.ReadAllBytes(_dbPath), Assert.Single(_cloud.Files).Value);
        Assert.Equal(SyncService.ComputeMd5(_dbPath), LoadState()!.SyncedMd5);
    }

    [Fact]
    public async Task Shutdown_CloudChangedSinceLastSync_DoesNotUpload()
    {
        await SyncedDevice("Shared session");
        var stateBefore = LoadState()!;
        var otherDeviceBytes = CreateDatabaseBytes("Shared session", "From other device");
        _cloud.Files[stateBefore.FileId] = otherDeviceBytes;
        AddSession(_dbPath, "New this session");
        var uploadsBefore = _cloud.UploadCount;

        var result = await CreateService().UploadOnShutdownAsync(CancellationToken.None);

        Assert.Equal(SyncResultKind.SkippedCloudChanged, result.Kind);
        Assert.Equal(uploadsBefore, _cloud.UploadCount);
        Assert.Equal(otherDeviceBytes, _cloud.Files[stateBefore.FileId]);
        Assert.Equal(stateBefore, LoadState());
    }

    [Fact]
    public async Task Shutdown_CloudFileDeleted_DoesNotUpload()
    {
        await SyncedDevice("Shared session");
        _cloud.Files.Clear();
        AddSession(_dbPath, "New this session");

        var result = await CreateService().UploadOnShutdownAsync(CancellationToken.None);

        Assert.Equal(SyncResultKind.SkippedCloudMissing, result.Kind);
        Assert.Empty(_cloud.Files);
    }

    [Fact]
    public async Task Shutdown_FailedUpload_KeepsStateSoNextStartUploads()
    {
        await SyncedDevice("Shared session");
        var stateBefore = LoadState();
        AddSession(_dbPath, "New this session");
        _cloud.FailUploads = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateService().UploadOnShutdownAsync(CancellationToken.None));
        Assert.Equal(stateBefore, LoadState());

        _cloud.FailUploads = false;
        var service = CreateService();
        var plan = await service.PlanAsync(CancellationToken.None);
        Assert.Equal(SyncAction.Upload, plan.Action);
        Assert.Equal(SyncResultKind.Uploaded, (await service.ExecuteAsync(plan, plan.Action, CancellationToken.None)).Kind);
    }

    [Fact]
    public void StateStore_UnreadableFile_IsTreatedAsNoState()
    {
        File.WriteAllText(_statePath, "{ not json");
        Assert.Null(new SyncStateStore(_statePath).Load());
    }

    /// <summary>Puts this device in a clean "synced" state: local db with the given sessions, uploaded, state recorded.</summary>
    private async Task SyncedDevice(params string[] sessionNames)
    {
        CreateDatabase(_dbPath, sessionNames);
        var service = CreateService();
        var plan = await service.PlanAsync(CancellationToken.None);
        await service.ExecuteAsync(plan, plan.Action, CancellationToken.None);
        Assert.NotNull(LoadState());
    }

    private string[] Backups() => Directory.GetFiles(_directory, "pomodoro.db.bak-*");

    private static void CreateDatabase(string path, params string[] sessionNames)
    {
        var database = new Database(path);
        var repository = new SessionRepository(database);
        foreach (var name in sessionNames)
        {
            repository.Add(NewSession(name));
        }
        SqliteConnection.ClearAllPools();
    }

    private byte[] CreateDatabaseBytes(params string[] sessionNames)
    {
        var path = Path.Combine(_directory, $"other-{Guid.NewGuid()}.db");
        CreateDatabase(path, sessionNames);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    private static void AddSession(string path, string name)
    {
        new SessionRepository(new Database(path)).Add(NewSession(name));
        SqliteConnection.ClearAllPools();
    }

    private static List<string> SessionNames(string path)
    {
        var names = new SessionRepository(new Database(path)).GetAll().Select(s => s.Name).ToList();
        SqliteConnection.ClearAllPools();
        return names;
    }

    private static Session NewSession(string name) => new()
    {
        Name = name,
        Mode = TimerMode.Timer,
        Category = SessionCategory.Work,
        PlannedDurationSeconds = 60,
        ActualDurationSeconds = 60,
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        EndedAt = DateTimeOffset.UtcNow,
        Completed = true
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    /// <summary>In-memory stand-in for Google Drive: file ID → content, with Drive-style MD5s.</summary>
    private sealed class FakeCloudFileStore : ICloudFileStore
    {
        private int _nextId;

        public Dictionary<string, byte[]> Files { get; } = new();
        public int CreateCount { get; private set; }
        public int UploadCount { get; private set; }
        public int DownloadCount { get; private set; }
        public bool FailUploads { get; set; }

        /// <summary>When set, metadata reports this MD5 instead of the real one (simulates a corrupted transfer).</summary>
        public string? ReportedMd5Override { get; set; }

        public string Seed(byte[] content)
        {
            var id = $"file-{++_nextId}";
            Files[id] = content;
            return id;
        }

        public Task<IReadOnlyList<CloudFileInfo>> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CloudFileInfo>>(Files.Keys.Select(Info).ToList());

        public Task<CloudFileInfo?> GetMetadataAsync(string fileId, CancellationToken cancellationToken) =>
            Task.FromResult(Files.ContainsKey(fileId) ? Info(fileId) : null);

        public Task<CloudFileInfo> CreateAsync(string localPath, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            CreateCount++;
            UploadCount++;
            var id = Seed(System.IO.File.ReadAllBytes(localPath));
            return Task.FromResult(Info(id));
        }

        public Task<CloudFileInfo> UpdateAsync(string fileId, string localPath, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            if (!Files.ContainsKey(fileId))
            {
                throw new InvalidOperationException("Update of a file that doesn't exist.");
            }

            UploadCount++;
            Files[fileId] = System.IO.File.ReadAllBytes(localPath);
            return Task.FromResult(Info(fileId));
        }

        public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken)
        {
            DownloadCount++;
            await destination.WriteAsync(Files[fileId], cancellationToken);
        }

        private CloudFileInfo Info(string id) =>
            new(id, ReportedMd5Override ?? Convert.ToHexStringLower(MD5.HashData(Files[id])));

        private void ThrowIfFailing()
        {
            if (FailUploads)
            {
                throw new HttpRequestException("Simulated network failure");
            }
        }
    }
}
