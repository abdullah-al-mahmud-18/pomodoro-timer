using PomodoroTimer.Core.Data;
using PomodoroTimer.Core.Models;
using Xunit;

namespace PomodoroTimer.Core.Tests;

public class RepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _database;

    public RepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pomodoro-test-{Guid.NewGuid()}.db");
        _database = new Database(_dbPath);
    }

    [Fact]
    public void SessionRepository_AddAndGetAll_RoundTrips()
    {
        var repo = new SessionRepository(_database);
        var now = DateTimeOffset.UtcNow;

        repo.Add(new Session
        {
            Name = "Deep work",
            Mode = TimerMode.Timer,
            Category = SessionCategory.Work,
            PlannedDurationSeconds = 1500,
            ActualDurationSeconds = 1500,
            StartedAt = now.AddMinutes(-25),
            EndedAt = now,
            Completed = true
        });

        var all = repo.GetAll();

        Assert.Single(all);
        Assert.Equal("Deep work", all[0].Name);
        Assert.True(all[0].Completed);
        Assert.Equal(SessionCategory.Work, all[0].Category);
    }

    [Fact]
    public void SessionRepository_Delete_Removes()
    {
        var repo = new SessionRepository(_database);
        var now = DateTimeOffset.UtcNow;

        var session = repo.Add(new Session
        {
            Name = "Reading",
            Mode = TimerMode.Stopwatch,
            Category = SessionCategory.Break,
            PlannedDurationSeconds = null,
            ActualDurationSeconds = 600,
            StartedAt = now.AddMinutes(-10),
            EndedAt = now,
            Completed = true
        });

        repo.Delete(session.Id);

        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public void SessionRepository_DeleteMany_RemovesOnlyGivenIds()
    {
        var repo = new SessionRepository(_database);
        var now = DateTimeOffset.UtcNow;

        Session AddSession(string name) => repo.Add(new Session
        {
            Name = name,
            Mode = TimerMode.Timer,
            Category = SessionCategory.Work,
            PlannedDurationSeconds = 60,
            ActualDurationSeconds = 60,
            StartedAt = now.AddMinutes(-1),
            EndedAt = now,
            Completed = true
        });

        var first = AddSession("First");
        var second = AddSession("Second");
        var kept = AddSession("Kept");

        repo.DeleteMany(new[] { first.Id, second.Id });

        var remaining = Assert.Single(repo.GetAll());
        Assert.Equal(kept.Id, remaining.Id);
    }

    [Fact]
    public void Database_MigratesExistingSessionsTable_WithoutCategoryColumn()
    {
        // Simulate a pre-Category database by dropping the column the fresh Database() ctor just added.
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE Sessions_Old (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    PlannedDurationSeconds INTEGER NULL,
                    ActualDurationSeconds INTEGER NOT NULL,
                    StartedAt TEXT NOT NULL,
                    EndedAt TEXT NOT NULL,
                    Completed INTEGER NOT NULL
                );
                DROP TABLE Sessions;
                ALTER TABLE Sessions_Old RENAME TO Sessions;
                """;
            command.ExecuteNonQuery();
        }

        // Re-running Database's migration logic against the same file should add Category back without error.
        var migrated = new Database(_dbPath);
        var repo = new SessionRepository(migrated);

        var all = repo.GetAll();
        Assert.Empty(all);
    }

    public void Dispose()
    {
        SqliteConnectionPoolCleanup();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static void SqliteConnectionPoolCleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
