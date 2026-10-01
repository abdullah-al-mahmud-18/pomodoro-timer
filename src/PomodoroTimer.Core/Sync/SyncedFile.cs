using System.Text;
using Microsoft.Data.Sqlite;
using Serilog;

namespace PomodoroTimer.Core.Sync;

/// <summary>
/// A local file that <see cref="SyncService"/> keeps in step with one cloud file, plus the checks specific to its
/// format: whether it holds any data worth protecting, and whether a downloaded copy is safe to swap in.
/// </summary>
public abstract class SyncedFile
{
    protected SyncedFile(string localPath, string displayName)
    {
        LocalPath = localPath;
        DisplayName = displayName;
    }

    public string LocalPath { get; }

    /// <summary>How the file is named in logs, e.g. "database".</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Whether the existing local file holds data (decides download vs. conflict on a device that has never synced).
    /// When unsure, implementations return true, so the worst case is a prompt rather than a silent overwrite.
    /// </summary>
    public abstract bool HasContent();

    /// <summary>Returns null if a downloaded copy is valid, otherwise what's wrong with it.</summary>
    public abstract string? Verify(string path);

    /// <summary>Lets go of anything holding the local file open, before it's hashed, copied, or replaced.</summary>
    public virtual void Release()
    {
    }

    /// <summary>Throws if the local file mustn't be replaced right now.</summary>
    public virtual void EnsureReplaceable()
    {
    }
}

/// <summary>The SQLite database, pomodoro.db.</summary>
public sealed class DatabaseSyncFile : SyncedFile
{
    public DatabaseSyncFile(string localPath) : base(localPath, "database")
    {
    }

    /// <summary>Whether the db has any sessions. Opening it also rolls back any hot journal from a crash.</summary>
    public override bool HasContent()
    {
        bool hasSessions;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = LocalPath,
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
            Log.ForContext<SyncService>().Warning(ex, "Couldn't check the local database for sessions; treating it as non-empty");
            hasSessions = true;
        }

        SqliteConnection.ClearAllPools();
        return hasSessions;
    }

    /// <summary>Opens the copy read-only and requires integrity_check "ok" and a Sessions table.</summary>
    public override string? Verify(string path)
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

    public override void Release() => SqliteConnection.ClearAllPools();

    /// <summary>A leftover rollback journal or WAL would be applied to the *new* file on next open and corrupt it.</summary>
    public override void EnsureReplaceable()
    {
        foreach (var sidecar in new[] { "-journal", "-wal" })
        {
            if (File.Exists(LocalPath + sidecar))
            {
                throw new IOException($"Not replacing the local database: {Path.GetFileName(LocalPath + sidecar)} exists, so it may be in use or need recovery.");
            }
        }
    }
}

/// <summary>The user-edited names list, names.txt (see <see cref="Names.NameList"/>).</summary>
public sealed class NamesSyncFile : SyncedFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public NamesSyncFile(string localPath) : base(localPath, "names list")
    {
    }

    public override bool HasContent()
    {
        try
        {
            return Names.NameList.Parse(File.ReadAllText(LocalPath)).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<SyncService>().Warning(ex, "Couldn't read the local names list; treating it as non-empty");
            return true;
        }
    }

    /// <summary>Requires valid UTF-8 text with no NUL characters — anything else isn't a names list.</summary>
    public override string? Verify(string path)
    {
        try
        {
            var text = StrictUtf8.GetString(File.ReadAllBytes(path));
            return text.Contains('\0') ? "file contains binary data" : null;
        }
        catch (DecoderFallbackException)
        {
            return "file is not valid UTF-8 text";
        }
    }
}
