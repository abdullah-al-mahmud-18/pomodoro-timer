using Microsoft.Data.Sqlite;

namespace PomodoroTimer.Core.Data;

/// <summary>
/// Owns the SQLite connection string and schema creation for the single local database file.
/// </summary>
public class Database
{
    public string ConnectionString { get; }

    public Database(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();

        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Mode TEXT NOT NULL,
                PlannedDurationSeconds INTEGER NULL,
                ActualDurationSeconds INTEGER NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NOT NULL,
                Completed INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Sessions_StartedAt ON Sessions (StartedAt DESC);
            """;
        command.ExecuteNonQuery();

        AddCategoryColumnIfMissing(connection);
    }

    private static void AddCategoryColumnIfMissing(SqliteConnection connection)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name = 'Category';";
        var columnExists = (long)checkCommand.ExecuteScalar()! > 0;

        if (!columnExists)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Sessions ADD COLUMN Category TEXT NOT NULL DEFAULT 'Work';";
            alterCommand.ExecuteNonQuery();
        }
    }
}
