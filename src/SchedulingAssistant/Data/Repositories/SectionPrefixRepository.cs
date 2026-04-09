using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// CRUD repository for <see cref="SectionPrefix"/> records stored in the
/// <c>SectionPrefixes</c> table.
/// </summary>
public class SectionPrefixRepository : ISectionPrefixRepository
{
    private readonly IDatabaseContext _db;

    /// <param name="db">The active database context.</param>
    public SectionPrefixRepository(IDatabaseContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns all section prefixes ordered alphabetically by prefix text.
    /// </summary>
    public List<SectionPrefix> GetAll()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM SectionPrefixes ORDER BY LOWER(prefix)";
        using var reader = cmd.ExecuteReader();
        var results = new List<SectionPrefix>();
        while (reader.Read())
        {
            var sp = JsonHelpers.Deserialize<SectionPrefix>(reader.GetString(1));
            sp.Id = reader.GetString(0);
            results.Add(sp);
        }
        return results;
    }

    /// <summary>
    /// Inserts a new section prefix record.
    /// </summary>
    /// <param name="prefix">The prefix to insert. Its <c>Id</c> must be set.</param>
    public void Insert(SectionPrefix prefix)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO SectionPrefixes (id, prefix, data) VALUES ($id, $prefix, $data)";
        cmd.AddParam("$id", prefix.Id);
        cmd.AddParam("$prefix", prefix.Prefix);
        cmd.AddParam("$data", JsonHelpers.Serialize(prefix));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Updates an existing section prefix record, matched by <c>Id</c>.
    /// </summary>
    /// <param name="prefix">The prefix with updated values.</param>
    public void Update(SectionPrefix prefix)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE SectionPrefixes SET prefix = $prefix, data = $data WHERE id = $id";
        cmd.AddParam("$id", prefix.Id);
        cmd.AddParam("$prefix", prefix.Prefix);
        cmd.AddParam("$data", JsonHelpers.Serialize(prefix));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes a section prefix by its ID.
    /// </summary>
    /// <param name="id">ID of the prefix to delete.</param>
    /// <param name="tx">Optional transaction to participate in.</param>
    public void Delete(string id, System.Data.Common.DbTransaction? tx = null)
    {
        _db.MarkDirty();
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM SectionPrefixes WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns true if a prefix with the given text already exists (case-insensitive),
    /// optionally excluding a specific record (useful during edit validation).
    /// </summary>
    /// <param name="prefixText">The prefix text to check.</param>
    /// <param name="excludeId">ID of the record being edited, to exclude from the check.</param>
    public bool ExistsByPrefix(string prefixText, string? excludeId = null)
    {
        using var cmd = _db.Connection.CreateCommand();
        if (excludeId is null)
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM SectionPrefixes WHERE LOWER(prefix) = LOWER($prefix)";
        }
        else
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM SectionPrefixes WHERE LOWER(prefix) = LOWER($prefix) AND id != $excludeId";
            cmd.AddParam("$excludeId", excludeId);
        }
        cmd.AddParam("$prefix", prefixText);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}
