using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class AcademicUnitRepository(DatabaseContext db)
{
    public List<AcademicUnit> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM AcademicUnits ORDER BY data ->> 'name'";
        using var reader = cmd.ExecuteReader();
        var results = new List<AcademicUnit>();
        while (reader.Read())
        {
            var unit = JsonHelpers.Deserialize<AcademicUnit>(reader.GetString(1));
            unit.Id = reader.GetString(0);
            results.Add(unit);
        }
        return results;
    }

    public AcademicUnit? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM AcademicUnits WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var unit = JsonHelpers.Deserialize<AcademicUnit>(reader.GetString(1));
        unit.Id = reader.GetString(0);
        return unit;
    }

    /// <summary>
    /// Returns true if an academic unit with this name already exists (case-insensitive).
    /// Pass excludeId to ignore the unit currently being edited.
    /// </summary>
    public bool ExistsByName(string name, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM AcademicUnits WHERE LOWER(data ->> 'name') = LOWER($name)"
            : "SELECT COUNT(*) FROM AcademicUnits WHERE LOWER(data ->> 'name') = LOWER($name) AND id != $excludeId";
        cmd.Parameters.AddWithValue("$name", name);
        if (excludeId is not null) cmd.Parameters.AddWithValue("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(AcademicUnit unit)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO AcademicUnits (id, name, data) VALUES ($id, $name, $data)";
        cmd.Parameters.AddWithValue("$id", unit.Id);
        cmd.Parameters.AddWithValue("$name", (object?)unit.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(unit));
        cmd.ExecuteNonQuery();
    }

    public void Update(AcademicUnit unit)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE AcademicUnits SET name = $name, data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", unit.Id);
        cmd.Parameters.AddWithValue("$name", (object?)unit.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(unit));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AcademicUnits WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
