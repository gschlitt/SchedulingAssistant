using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

/// <summary>
/// SQLite-backed implementation of <see cref="IMeetingRepository"/>.
/// The <c>Meetings</c> table uses the standard pattern: dedicated columns for
/// identity and DB-browsing fields, with a <c>data JSON</c> column for the rest.
/// <para>
/// Column layout: <c>id TEXT, semester_id TEXT, title TEXT, data TEXT</c>.
/// <c>title</c> is denormalised from the JSON for easy DB browsing but is always
/// authoritative in the JSON; the two are kept in sync on every write.
/// </para>
/// </summary>
public class MeetingRepository(IDatabaseContext db) : IMeetingRepository
{
    /// <inheritdoc/>
    public List<Meeting> GetAll(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, title, data FROM Meetings WHERE semester_id = $sid ORDER BY title";
        cmd.AddParam("$sid", semesterId);
        return ReadMeetings(cmd);
    }

    /// <inheritdoc/>
    public Meeting? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, title, data FROM Meetings WHERE id = $id";
        cmd.AddParam("$id", id);
        return ReadMeetings(cmd).FirstOrDefault();
    }

    /// <inheritdoc/>
    public void Insert(Meeting meeting)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Meetings (id, semester_id, title, data) VALUES ($id, $sid, $title, $data)";
        cmd.AddParam("$id",    meeting.Id);
        cmd.AddParam("$sid",   meeting.SemesterId);
        cmd.AddParam("$title", meeting.Title);
        cmd.AddParam("$data",  JsonHelpers.Serialize(meeting));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Update(Meeting meeting)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Meetings
            SET semester_id = $sid,
                title       = $title,
                data        = $data
            WHERE id = $id
            """;
        cmd.AddParam("$id",    meeting.Id);
        cmd.AddParam("$sid",   meeting.SemesterId);
        cmd.AddParam("$title", meeting.Title);
        cmd.AddParam("$data",  JsonHelpers.Serialize(meeting));
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Meetings WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public void DeleteBySemesterId(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Meetings WHERE semester_id = $sid";
        cmd.AddParam("$sid", semesterId);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public int CountBySemesterId(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Meetings WHERE semester_id = $sid";
        cmd.AddParam("$sid", semesterId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads a result set of meeting rows into a list.
    /// Id and SemesterId are pulled from dedicated columns and overwrite whatever was
    /// in the JSON, matching the pattern used by <see cref="SectionRepository"/>.
    /// Title is also pulled from its dedicated column (authoritative source) so that
    /// partial or stale JSON data cannot cause a mismatch.
    /// </summary>
    private static List<Meeting> ReadMeetings(System.Data.Common.DbCommand cmd)
    {
        using var reader  = cmd.ExecuteReader();
        var       results = new List<Meeting>();
        while (reader.Read())
        {
            var meeting = JsonHelpers.Deserialize<Meeting>(reader.GetString(3));
            meeting.Id         = reader.GetString(0);
            meeting.SemesterId = reader.GetString(1);
            meeting.Title      = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            results.Add(meeting);
        }
        return results;
    }
}
