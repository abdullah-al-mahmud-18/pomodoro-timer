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
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PomodoroTimer.App.Services;

/// <summary>
/// The single pomodoro.db file in the user's Google Drive, tagged with appProperties { pomodoroSync: primary }.
/// Only whole-file upload/download — the database is never accessed over the network.
/// </summary>
public sealed class GoogleDriveFileStore : ICloudFileStore, IDisposable
{
    private const string FileName = "pomodoro.db";
    private const string MimeType = "application/x-sqlite3";
    private const string AppPropertyKey = "pomodoroSync";
    private const string AppPropertyValue = "primary";
    private const string FileFields = "id, md5Checksum, trashed";

    private readonly DriveService _service;

    public GoogleDriveFileStore(IConfigurableHttpClientInitializer credential)
    {
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
        request.Q = $"appProperties has {{ key='{AppPropertyKey}' and value='{AppPropertyValue}' }} and trashed = false";
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
            Name = FileName,
            MimeType = MimeType,
            AppProperties = new Dictionary<string, string> { [AppPropertyKey] = AppPropertyValue }
        };

        await using var stream = File.OpenRead(localPath);
        var request = _service.Files.Create(metadata, stream, MimeType);
        request.Fields = FileFields;
        ThrowIfFailed(await request.UploadAsync(cancellationToken));
        return ToInfo(request.ResponseBody);
    }

    public async Task<CloudFileInfo> UpdateAsync(string fileId, string localPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(localPath);
        var request = _service.Files.Update(new DriveFile(), fileId, stream, MimeType);
        request.Fields = FileFields;
        ThrowIfFailed(await request.UploadAsync(cancellationToken));
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
