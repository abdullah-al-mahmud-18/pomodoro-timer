namespace PomodoroTimer.Core.Sync;

/// <summary>Identity and content hash of the database file stored in the cloud.</summary>
/// <param name="Id">The provider's file ID.</param>
/// <param name="Md5">Lowercase hex MD5 of the file's content, or null if the provider didn't report one.</param>
public sealed record CloudFileInfo(string Id, string? Md5);

/// <summary>
/// The whole-file operations sync needs from a cloud provider. Implementations never read or write the database
/// over the network — they only move complete files.
/// </summary>
public interface ICloudFileStore
{
    /// <summary>Finds every (non-trashed) file tagged as this app's primary database.</summary>
    Task<IReadOnlyList<CloudFileInfo>> FindAsync(CancellationToken cancellationToken);

    /// <summary>Gets a file's metadata, or null if it no longer exists (deleted or trashed).</summary>
    Task<CloudFileInfo?> GetMetadataAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>Uploads <paramref name="localPath"/> as a new, tagged file.</summary>
    Task<CloudFileInfo> CreateAsync(string localPath, CancellationToken cancellationToken);

    /// <summary>Replaces the content of an existing file with <paramref name="localPath"/>.</summary>
    Task<CloudFileInfo> UpdateAsync(string fileId, string localPath, CancellationToken cancellationToken);

    /// <summary>Writes a file's content to <paramref name="destination"/>.</summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken);
}
