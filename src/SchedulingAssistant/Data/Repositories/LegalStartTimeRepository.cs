using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class LegalStartTimeRepository(DatabaseContext db)
{
    public List<LegalStartTime> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT block_length, start_times FROM LegalStartTimes ORDER BY block_length";
        using var reader = cmd.ExecuteReader();
        var results = new List<LegalStartTime>();
        while (reader.Read())
            results.Add(new LegalStartTime
            {
                BlockLength = reader.GetDouble(0),
                StartTimes = JsonHelpers.Deserialize<List<int>>(reader.GetString(1))
            });
        return results;
    }

    public LegalStartTime? GetByBlockLength(double blockLength)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT block_length, start_times FROM LegalStartTimes WHERE block_length = $bl";
        cmd.Parameters.AddWithValue("$bl", blockLength);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new LegalStartTime
        {
            BlockLength = reader.GetDouble(0),
            StartTimes = JsonHelpers.Deserialize<List<int>>(reader.GetString(1))
        };
    }

    public void Insert(LegalStartTime entry)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO LegalStartTimes (block_length, start_times) VALUES ($bl, $st)";
        cmd.Parameters.AddWithValue("$bl", entry.BlockLength);
        cmd.Parameters.AddWithValue("$st", JsonHelpers.Serialize(entry.StartTimes));
        cmd.ExecuteNonQuery();
    }

    public void Update(LegalStartTime entry)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE LegalStartTimes SET start_times = $st WHERE block_length = $bl";
        cmd.Parameters.AddWithValue("$bl", entry.BlockLength);
        cmd.Parameters.AddWithValue("$st", JsonHelpers.Serialize(entry.StartTimes));
        cmd.ExecuteNonQuery();
    }

    public void Delete(double blockLength)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM LegalStartTimes WHERE block_length = $bl";
        cmd.Parameters.AddWithValue("$bl", blockLength);
        cmd.ExecuteNonQuery();
    }
}
