using System.Text.Json;
using Serilog;

namespace PomodoroTimer.Core.Sync;

/// <summary>Per-device sync bookkeeping, kept in sync-state.json next to the db. Never uploaded.</summary>
/// <param name="FileId">The cloud file this device syncs with.</param>
/// <param name="SyncedMd5">S — MD5 of the db at the last successful upload or download.</param>
/// <param name="LastSyncedUtc">When that sync happened.</param>
public sealed record SyncState(string FileId, string SyncedMd5, DateTimeOffset LastSyncedUtc);

public sealed class SyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SyncStateStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    /// <summary>Returns the saved state, or null if there is none or it can't be read.</summary>
    public SyncState? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<SyncState>(File.ReadAllText(Path), JsonOptions);
            if (state is null || string.IsNullOrEmpty(state.FileId) || string.IsNullOrEmpty(state.SyncedMd5))
            {
                Log.Warning("Ignoring incomplete sync state file {Path}", Path);
                return null;
            }

            return state;
        }
        catch (JsonException ex)
        {
            // Treated as "no state", which can only lead to downloading over an empty db or a conflict prompt.
            Log.Warning(ex, "Ignoring unreadable sync state file {Path}", Path);
            return null;
        }
    }

    /// <summary>Writes the state atomically: temp file, flushed to disk, then moved over the old one.</summary>
    public void Save(SyncState state)
    {
        var tempPath = Path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, Path, overwrite: true);
    }
}
