using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class SectionPropertyRepository(DatabaseContext db)
{
    public List<SectionPropertyValue> GetAll(string type)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, data FROM SectionPropertyValues WHERE type = $type ORDER BY LOWER(data ->> 'name')";
        cmd.Parameters.AddWithValue("$type", type);
        return Read(cmd);
    }

    public SectionPropertyValue? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM SectionPropertyValues WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return Read(cmd).FirstOrDefault();
    }

    public void Insert(string type, SectionPropertyValue value)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO SectionPropertyValues (id, type, data) VALUES ($id, $type, $data)";
        cmd.Parameters.AddWithValue("$id", value.Id);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(value));
        cmd.ExecuteNonQuery();
    }

    public void Update(SectionPropertyValue value)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE SectionPropertyValues SET data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", value.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(value));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SectionPropertyValues WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns true if a value with this name already exists within the given type (case-insensitive).
    /// Pass excludeId to skip the record currently being edited.
    /// </summary>
    public bool ExistsByName(string type, string name, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM SectionPropertyValues WHERE type = $type AND LOWER(data ->> 'name') = LOWER($name)"
            : "SELECT COUNT(*) FROM SectionPropertyValues WHERE type = $type AND LOWER(data ->> 'name') = LOWER($name) AND id != $excludeId";
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$name", name);
        if (excludeId is not null) cmd.Parameters.AddWithValue("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static List<SectionPropertyValue> Read(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<SectionPropertyValue>();
        while (reader.Read())
        {
            var v = JsonHelpers.Deserialize<SectionPropertyValue>(reader.GetString(1));
            v.Id = reader.GetString(0);
            results.Add(v);
        }
        return results;
    }
}
