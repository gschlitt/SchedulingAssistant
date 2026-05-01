using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="ISectionCodePatternRepository"/>.
/// </summary>
public class SectionCodePatternRepository : ISectionCodePatternRepository
{
    private readonly IDatabaseContext _db;

    /// <param name="db">Database context providing the SQLite connection.</param>
    public SectionCodePatternRepository(IDatabaseContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public List<SectionCodePattern> GetAll()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, data FROM SectionCodePatterns
            ORDER BY sort_order, name
            """;
        using var reader = cmd.ExecuteReader();
        var results = new List<SectionCodePattern>();
        while (reader.Read())
        {
            var pattern = JsonHelpers.Deserialize<SectionCodePattern>(reader.GetString(1));
            pattern.Id = reader.GetString(0);
            results.Add(pattern);
        }
        return results;
    }

    /// <inheritdoc/>
    public SectionCodePattern? GetById(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM SectionCodePatterns WHERE id = $id";
        cmd.AddParam("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var pattern = JsonHelpers.Deserialize<SectionCodePattern>(reader.GetString(1));
        pattern.Id = reader.GetString(0);
        return pattern;
    }

    /// <inheritdoc/>
    public bool ExistsByName(string name, string? excludeId = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        if (excludeId is null)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM SectionCodePatterns WHERE LOWER(name) = LOWER($name)";
            cmd.AddParam("$name", name);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM SectionCodePatterns WHERE LOWER(name) = LOWER($name) AND id != $id";
            cmd.AddParam("$name", name);
            cmd.AddParam("$id", excludeId);
        }
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <inheritdoc/>
    public void Insert(SectionCodePattern pattern)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO SectionCodePatterns (id, name, sort_order, data) VALUES ($id, $name, $sortOrder, $data)";
        cmd.AddParam("$id", pattern.Id);
        cmd.AddParam("$name", pattern.Name);
        cmd.AddParam("$sortOrder", pattern.SortOrder);
        cmd.AddParam("$data", JsonHelpers.Serialize(pattern));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Update(SectionCodePattern pattern)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE SectionCodePatterns SET name = $name, sort_order = $sortOrder, data = $data WHERE id = $id";
        cmd.AddParam("$id", pattern.Id);
        cmd.AddParam("$name", pattern.Name);
        cmd.AddParam("$sortOrder", pattern.SortOrder);
        cmd.AddParam("$data", JsonHelpers.Serialize(pattern));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Delete(string id)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM SectionCodePatterns WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }
}
