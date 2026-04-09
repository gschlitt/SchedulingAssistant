using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="ICampusRepository"/>.
/// Uses the project's standard pattern: stable identity columns + a <c>data</c> JSON blob.
/// </summary>
public class CampusRepository : ICampusRepository
{
    private readonly IDatabaseContext _db;

    /// <param name="db">Injected database context providing the open connection.</param>
    public CampusRepository(IDatabaseContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public List<Campus> GetAll()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Campuses ORDER BY json_extract(data, '$.SortOrder')";
        using var reader = cmd.ExecuteReader();
        var results = new List<Campus>();
        while (reader.Read())
        {
            var campus = JsonHelpers.Deserialize<Campus>(reader.GetString(1));
            campus.Id = reader.GetString(0);
            results.Add(campus);
        }
        return results;
    }

    /// <inheritdoc/>
    public Campus? GetById(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Campuses WHERE id = $id";
        cmd.AddParam("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var campus = JsonHelpers.Deserialize<Campus>(reader.GetString(1));
        campus.Id = reader.GetString(0);
        return campus;
    }

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        if (excludeId is null)
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM Campuses WHERE LOWER(json_extract(data, '$.Name')) = LOWER($name)";
            cmd.AddParam("$name", name);
        }
        else
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM Campuses WHERE LOWER(json_extract(data, '$.Name')) = LOWER($name) AND id != $id";
            cmd.AddParam("$name", name);
            cmd.AddParam("$id", excludeId);
        }
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <inheritdoc/>
    public void Insert(Campus campus)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Campuses (id, name, data) VALUES ($id, $name, $data)";
        cmd.AddParam("$id",   campus.Id);
        cmd.AddParam("$name", campus.Name);
        cmd.AddParam("$data", JsonHelpers.Serialize(campus));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Update(Campus campus)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Campuses SET name = $name, data = $data WHERE id = $id";
        cmd.AddParam("$id",   campus.Id);
        cmd.AddParam("$name", campus.Name);
        cmd.AddParam("$data", JsonHelpers.Serialize(campus));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Delete(string id)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Campuses WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }
}
