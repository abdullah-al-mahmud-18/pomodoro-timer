using Microsoft.Data.Sqlite;
using PomodoroTimer.Core.Models;

namespace PomodoroTimer.Core.Data;

public class SessionRepository
{
    private readonly Database _database;

    public SessionRepository(Database database)
    {
        _database = database;
    }

    public List<Session> GetAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Mode, PlannedDurationSeconds, ActualDurationSeconds, StartedAt, EndedAt, Completed, Category
            FROM Sessions
            ORDER BY StartedAt DESC;
            """;

        var results = new List<Session>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSession(reader));
        }
        return results;
    }

    public Session Add(Session session)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Sessions (Name, Mode, PlannedDurationSeconds, ActualDurationSeconds, StartedAt, EndedAt, Completed, Category)
            VALUES ($name, $mode, $planned, $actual, $startedAt, $endedAt, $completed, $category);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", session.Name);
        command.Parameters.AddWithValue("$mode", session.Mode.ToString());
        command.Parameters.AddWithValue("$planned", (object?)session.PlannedDurationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$actual", session.ActualDurationSeconds);
        command.Parameters.AddWithValue("$startedAt", session.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$endedAt", session.EndedAt.ToString("O"));
        command.Parameters.AddWithValue("$completed", session.Completed ? 1 : 0);
        command.Parameters.AddWithValue("$category", session.Category.ToString());

        var id = (long)command.ExecuteScalar()!;
        session.Id = (int)id;
        return session;
    }

    public void Delete(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Deletes all sessions with the given ids in a single transaction.</summary>
    public void DeleteMany(IEnumerable<int> ids)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Sessions WHERE Id = $id;";
        var idParameter = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (var id in ids)
        {
            idParameter.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static Session ReadSession(SqliteDataReader reader)
    {
        return new Session
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Mode = Enum.Parse<TimerMode>(reader.GetString(2)),
            PlannedDurationSeconds = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            ActualDurationSeconds = reader.GetInt32(4),
            StartedAt = DateTimeOffset.Parse(reader.GetString(5)),
            EndedAt = DateTimeOffset.Parse(reader.GetString(6)),
            Completed = reader.GetInt32(7) != 0,
            Category = Enum.Parse<SessionCategory>(reader.GetString(8))
        };
    }
}
