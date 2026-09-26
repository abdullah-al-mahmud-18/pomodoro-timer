using Microsoft.Data.Sqlite;
using PomodoroTimer.Core.Models;

namespace PomodoroTimer.Core.Data;

public class PresetRepository
{
    private readonly Database _database;

    public PresetRepository(Database database)
    {
        _database = database;
    }

    public List<Preset> GetAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Mode, DurationSeconds, CreatedAt FROM Presets ORDER BY CreatedAt DESC;";

        var results = new List<Preset>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadPreset(reader));
        }
        return results;
    }

    public Preset Add(string name, TimerMode mode, int? durationSeconds)
    {
        var createdAt = DateTimeOffset.UtcNow;

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Presets (Name, Mode, DurationSeconds, CreatedAt)
            VALUES ($name, $mode, $duration, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$mode", mode.ToString());
        command.Parameters.AddWithValue("$duration", (object?)durationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));

        var id = (long)command.ExecuteScalar()!;

        return new Preset
        {
            Id = (int)id,
            Name = name,
            Mode = mode,
            DurationSeconds = durationSeconds,
            CreatedAt = createdAt
        };
    }

    public void Delete(int id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Presets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static Preset ReadPreset(SqliteDataReader reader)
    {
        return new Preset
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Mode = Enum.Parse<TimerMode>(reader.GetString(2)),
            DurationSeconds = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(4))
        };
    }
}
