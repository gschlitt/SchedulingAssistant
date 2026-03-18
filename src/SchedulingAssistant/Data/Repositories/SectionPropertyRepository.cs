using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class SectionPropertyRepository(IDatabaseContext db) : ISectionPropertyRepository
{
    /// <summary>
    /// Returns all property values of the given type, ordered by <see cref="SectionPropertyValue.SortOrder"/>
    /// ascending, then alphabetically by name as a tiebreaker.  Sorting is done in C# after
    /// deserialization so that existing rows whose JSON predates the SortOrder field correctly
    /// default to 0 rather than sorting unexpectedly.
    /// </summary>
    public List<SectionPropertyValue> GetAll(string type)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM SectionPropertyValues WHERE type = $type";
        cmd.AddParam("$type", type);
        return Read(cmd)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SectionPropertyValue? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM SectionPropertyValues WHERE id = $id";
        cmd.AddParam("$id", id);
        return Read(cmd).FirstOrDefault();
    }

    public void Insert(string type, SectionPropertyValue value)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO SectionPropertyValues (id, type, name, data) VALUES ($id, $type, $name, $data)";
        cmd.AddParam("$id", value.Id);
        cmd.AddParam("$type", type);
        cmd.AddParam("$name", value.Name);
        cmd.AddParam("$data", JsonHelpers.Serialize(value));
        cmd.ExecuteNonQuery();
    }

    public void Update(SectionPropertyValue value)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE SectionPropertyValues SET name = $name, data = $data WHERE id = $id";
        cmd.AddParam("$id", value.Id);
        cmd.AddParam("$name", value.Name);
        cmd.AddParam("$data", JsonHelpers.Serialize(value));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id, System.Data.Common.DbTransaction? tx = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM SectionPropertyValues WHERE id = $id";
        cmd.AddParam("$id", id);
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
        cmd.AddParam("$type", type);
        cmd.AddParam("$name", name);
        if (excludeId is not null) cmd.AddParam("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static List<SectionPropertyValue> Read(System.Data.Common.DbCommand cmd)
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
