using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class ReleaseRepository(DatabaseContext db)
{
    public List<Release> GetBySemester(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, semester_id, instructor_id, data FROM Releases WHERE semester_id = $sid ORDER BY data ->> 'title'";
        cmd.Parameters.AddWithValue("$sid", semesterId);
        return ReadReleases(cmd);
    }

    public List<Release> GetByInstructor(string semesterId, string instructorId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, semester_id, instructor_id, data FROM Releases WHERE semester_id = $sid AND instructor_id = $iid ORDER BY data ->> 'title'";
        cmd.Parameters.AddWithValue("$sid", semesterId);
        cmd.Parameters.AddWithValue("$iid", instructorId);
        return ReadReleases(cmd);
    }

    public Release? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, semester_id, instructor_id, data FROM Releases WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRelease(reader);
    }

    public void Insert(Release release)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Releases (id, semester_id, instructor_id, data) VALUES ($id, $sid, $iid, $data)";
        cmd.Parameters.AddWithValue("$id", release.Id);
        cmd.Parameters.AddWithValue("$sid", release.SemesterId);
        cmd.Parameters.AddWithValue("$iid", release.InstructorId);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(release));
        cmd.ExecuteNonQuery();
    }

    public void Update(Release release)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Releases SET semester_id = $sid, instructor_id = $iid, data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", release.Id);
        cmd.Parameters.AddWithValue("$sid", release.SemesterId);
        cmd.Parameters.AddWithValue("$iid", release.InstructorId);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(release));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Releases WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<Release> ReadReleases(Microsoft.Data.Sqlite.SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<Release>();
        while (reader.Read())
            results.Add(ReadRelease(reader));
        return results;
    }

    private static Release ReadRelease(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var release = JsonHelpers.Deserialize<Release>(reader.GetString(3));
        release.Id = reader.GetString(0);
        release.SemesterId = reader.GetString(1);
        release.InstructorId = reader.GetString(2);
        return release;
    }
}
