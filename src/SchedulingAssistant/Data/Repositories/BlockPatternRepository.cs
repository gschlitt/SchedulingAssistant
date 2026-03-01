using SchedulingAssistant.Data;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class BlockPatternRepository
{
    private readonly DatabaseContext _db;

    public BlockPatternRepository(DatabaseContext db)
    {
        _db = db;
    }

    public List<BlockPattern> GetAll()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM BlockPatterns ORDER BY data ->> 'name'";
        using var reader = cmd.ExecuteReader();
        var results = new List<BlockPattern>();
        while (reader.Read())
        {
            var pattern = JsonHelpers.Deserialize<BlockPattern>(reader.GetString(1));
            pattern.Id = reader.GetString(0);
            results.Add(pattern);
        }
        return results;
    }

    public BlockPattern? GetById(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM BlockPatterns WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var pattern = JsonHelpers.Deserialize<BlockPattern>(reader.GetString(1));
        pattern.Id = reader.GetString(0);
        return pattern;
    }

    public void Insert(BlockPattern pattern)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO BlockPatterns (id, name, data) VALUES ($id, $name, $data)";
        cmd.Parameters.AddWithValue("$id", pattern.Id);
        cmd.Parameters.AddWithValue("$name", pattern.Name);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(pattern));
        cmd.ExecuteNonQuery();
    }

    public void Update(BlockPattern pattern)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE BlockPatterns SET name = $name, data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", pattern.Id);
        cmd.Parameters.AddWithValue("$name", pattern.Name);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(pattern));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM BlockPatterns WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
