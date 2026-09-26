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
            PlannedDurationSeconds = null,
            ActualDurationSeconds = 600,
            StartedAt = now.AddMinutes(-10),
            EndedAt = now,
            Completed = true
        });

        repo.Delete(session.Id);

        Assert.Empty(repo.GetAll());
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
