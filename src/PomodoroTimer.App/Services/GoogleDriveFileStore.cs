using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util;
using PomodoroTimer.Core.Sync;
using Serilog;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PomodoroTimer.App.Services;

/// <summary>Which file in Drive a <see cref="GoogleDriveFileStore"/> syncs: its name, type, and appProperties tag.</summary>
public sealed record DriveFileSpec(string FileName, string MimeType, string TagValue)
{
    /// <summary>pomodoro.db, tagged { pomodoroSync: primary }. Debug builds use pomodoro-dev.db tagged { pomodoroSync: dev }.</summary>
    public static DriveFileSpec Database { get; } = AppPaths.IsDevelopment
        ? new("pomodoro-dev.db", "application/x-sqlite3", "dev")
        : new("pomodoro.db", "application/x-sqlite3", "primary");

    /// <summary>names.txt, tagged { pomodoroSync: names }. Debug builds use names-dev.txt tagged { pomodoroSync: names-dev }.</summary>
    public static DriveFileSpec Names { get; } = AppPaths.IsDevelopment
        ? new("names-dev.txt", "text/plain", "names-dev")
        : new("names.txt", "text/plain", "names");
}

/// <summary>
/// One app file in the user's Google Drive (pomodoro.db or names.txt), found by its appProperties tag and kept in
/// a PomodoroTimer folder (tagged { pomodoroSync: folder }) that the app creates.
/// Only whole-file upload/download — the database is never accessed over the network.
/// Debug builds use differently named and tagged files in the same folder (see <see cref="DriveFileSpec"/>), so
/// development syncs never find, update, or download the real files.
/// </summary>
public sealed class GoogleDriveFileStore : ICloudFileStore, IDisposable
{
    private const string FolderName = "PomodoroTimer";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string AppPropertyKey = "pomodoroSync";
    private const string FolderPropertyValue = "folder";
    private const string FileFields = "id, md5Checksum, trashed, parents";

    private readonly DriveService _service;
    private readonly DriveFileSpec _spec;
    private string? _folderId;

    public GoogleDriveFileStore(IConfigurableHttpClientInitializer credential, DriveFileSpec spec)
    {
        _spec = spec;
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "PomodoroTimer",
            // Built-in exponential backoff for network errors and 503s...
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.Exception | ExponentialBackOffPolicy.UnsuccessfulResponse503
        });

        // ...plus rate limits (403/429) and the other transient 5xx responses. Few retries, so a shutdown sync
        // still finishes inside its timeout.
        _service.HttpClient.MessageHandler.AddUnsuccessfulResponseHandler(
            new BackOffHandler(new BackOffHandler.Initializer(new ExponentialBackOff(TimeSpan.FromMilliseconds(250), 4))
            {
                HandleUnsuccessfulResponseFunc = response => (int)response.StatusCode is 403 or 429 or 500 or 502 or 504
            }));
    }

    public async Task<IReadOnlyList<CloudFileInfo>> FindAsync(CancellationToken cancellationToken)
    {
        var request = _service.Files.List();
        request.Q = $"appProperties has {{ key='{AppPropertyKey}' and value='{_spec.TagValue}' }} and trashed = false";
        request.Spaces = "drive";
        request.Fields = "files(id, md5Checksum)";
        request.PageSize = 10;

        var result = await request.ExecuteAsync(cancellationToken);
        return (result.Files ?? new List<DriveFile>()).Select(ToInfo).ToList();
    }

    public async Task<CloudFileInfo?> GetMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            var request = _service.Files.Get(fileId);
            request.Fields = FileFields;
            var file = await request.ExecuteAsync(cancellationToken);
            return file.Trashed == true ? null : ToInfo(file);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CloudFileInfo> CreateAsync(string localPath, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile
        {
            Name = _spec.FileName,
            MimeType = _spec.MimeType,
            Parents = new List<string> { await GetOrCreateFolderAsync(cancellationToken) },
            AppProperties = new Dictionary<string, string> { [AppPropertyKey] = _spec.TagValue }
        };

        await using var stream = File.OpenRead(localPath);
        var request = _service.Files.Create(metadata, stream, _spec.MimeType);
        request.Fields = FileFields;
        ThrowIfFailed(await request.UploadAsync(cancellationToken));
        return ToInfo(request.ResponseBody);
    }

    public async Task<CloudFileInfo> UpdateAsync(string fileId, string localPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(localPath);
        var request = _service.Files.Update(new DriveFile(), fileId, stream, _spec.MimeType);
        request.Fields = FileFields;
        ThrowIfFailed(await request.UploadAsync(cancellationToken));

        await TryMoveIntoFolderAsync(request.ResponseBody, cancellationToken);
        return ToInfo(request.ResponseBody);
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken)
    {
        var progress = await _service.Files.Get(fileId).DownloadAsync(destination, cancellationToken);
        if (progress.Status != DownloadStatus.Completed)
        {
            Rethrow(progress.Exception, "Download from Google Drive did not complete.");
        }
    }

    public void Dispose() => _service.Dispose();

    /// <summary>
    /// The app's PomodoroTimer folder, found by its appProperties tag (so a rename doesn't matter) or created at
    /// the top of My Drive. With the drive.file scope the app only sees folders it created itself.
    /// </summary>
    private async Task<string> GetOrCreateFolderAsync(CancellationToken cancellationToken)
    {
        if (_folderId is not null)
        {
            return _folderId;
        }

        var list = _service.Files.List();
        list.Q = $"mimeType = '{FolderMimeType}' and appProperties has {{ key='{AppPropertyKey}' and value='{FolderPropertyValue}' }} and trashed = false";
        list.Spaces = "drive";
        list.Fields = "files(id)";
        list.OrderBy = "createdTime";
        list.PageSize = 10;

        var folders = (await list.ExecuteAsync(cancellationToken)).Files ?? new List<DriveFile>();
        if (folders.Count > 0)
        {
            // A folder holds no data itself, so with duplicates it's safe to pick one: the oldest.
            if (folders.Count > 1)
            {
                Log.Warning("Found {Count} {FolderName} folders in Google Drive; using the oldest, {FolderId}",
                    folders.Count, FolderName, folders[0].Id);
            }
            return _folderId = folders[0].Id;
        }

        var create = _service.Files.Create(new DriveFile
        {
            Name = FolderName,
            MimeType = FolderMimeType,
            AppProperties = new Dictionary<string, string> { [AppPropertyKey] = FolderPropertyValue }
        });
        create.Fields = "id";
        var folder = await create.ExecuteAsync(cancellationToken);
        Log.Information("Created Google Drive folder {FolderName} ({FolderId})", FolderName, folder.Id);
        return _folderId = folder.Id;
    }

    /// <summary>
    /// Moves the file into the app folder if it's elsewhere (e.g. uploaded to the top of My Drive by an
    /// earlier version). The file ID and revision history are kept. Best effort: the content upload has already
    /// succeeded, so a failed move is only logged and tried again on the next upload.
    /// </summary>
    private async Task TryMoveIntoFolderAsync(DriveFile file, CancellationToken cancellationToken)
    {
        try
        {
            var folderId = await GetOrCreateFolderAsync(cancellationToken);
            var parents = file.Parents ?? new List<string>();
            if (parents.Contains(folderId))
            {
                return;
            }

            var move = _service.Files.Update(new DriveFile(), file.Id);
            move.AddParents = folderId;
            if (parents.Count > 0)
            {
                move.RemoveParents = string.Join(",", parents);
            }
            move.Fields = "id";
            await move.ExecuteAsync(cancellationToken);
            Log.Information("Moved cloud file {FileId} into the {FolderName} folder", file.Id, FolderName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't move cloud file {FileId} into the {FolderName} folder; will try again on the next upload",
                file.Id, FolderName);
        }
    }

    private static CloudFileInfo ToInfo(DriveFile file) => new(file.Id, file.Md5Checksum);

    private static void ThrowIfFailed(IUploadProgress progress)
    {
        if (progress.Status != UploadStatus.Completed)
        {
            Rethrow(progress.Exception, "Upload to Google Drive did not complete.");
        }
    }

    private static void Rethrow(Exception? exception, string fallbackMessage)
    {
        if (exception is not null)
        {
            ExceptionDispatchInfo.Throw(exception);
        }

        throw new IOException(fallbackMessage);
    }
}
