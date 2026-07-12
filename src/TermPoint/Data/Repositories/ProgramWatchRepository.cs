using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// SQLite-backed repository for <see cref="ProgramWatch"/>. Each watch belongs to a single
/// semester. The <c>name</c> and <c>mode</c> columns are denormalized copies kept for
/// human-readability of the raw table; the app reads its data from the JSON column.
/// </summary>
public class ProgramWatchRepository(IDatabaseContext db) : IProgramWatchRepository
{
    /// <inheritdoc/>
    public List<ProgramWatch> GetAll(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM ProgramWatches WHERE semester_id = $sid ORDER BY name";
        cmd.AddParam("$sid", semesterId);
        using var reader = cmd.ExecuteReader();
        var results = new List<ProgramWatch>();
        while (reader.Read())
        {
            var watch = JsonHelpers.Deserialize<ProgramWatch>(reader.GetString(1));
            watch.Id = reader.GetString(0);
            results.Add(watch);
        }
        return results;
    }

    /// <inheritdoc/>
    public void Save(string semesterId, ProgramWatch watch)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ProgramWatches (id, semester_id, name, mode, data)
            VALUES ($id, $sid, $name, $mode, $data)
            ON CONFLICT(id) DO UPDATE SET
                name = $name,
                mode = $mode,
                data = $data
            """;
        cmd.AddParam("$id", watch.Id);
        cmd.AddParam("$sid", semesterId);
        cmd.AddParam("$name", watch.Name);
        cmd.AddParam("$mode", watch.Mode.ToString().ToLowerInvariant());
        cmd.AddParam("$data", JsonHelpers.Serialize(watch));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Delete(string id)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ProgramWatches WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }
}
