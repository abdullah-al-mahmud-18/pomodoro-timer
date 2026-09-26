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
    public void PresetRepository_AddAndGetAll_RoundTrips()
    {
        var repo = new PresetRepository(_database);

        repo.Add("Deep work", TimerMode.Timer, 3000);
        repo.Add("Free reading", TimerMode.Stopwatch, null);

        var all = repo.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.Name == "Deep work" && p.DurationSeconds == 3000);
        Assert.Contains(all, p => p.Name == "Free reading" && p.DurationSeconds == null);
    }

    [Fact]
    public void PresetRepository_Delete_Removes()
    {
        var repo = new PresetRepository(_database);
        var preset = repo.Add("Short break", TimerMode.Timer, 300);

        repo.Delete(preset.Id);

        Assert.Empty(repo.GetAll());
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
