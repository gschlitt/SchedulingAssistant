using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class SubjectRepository(DatabaseContext db)
{
    public List<Subject> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Subjects ORDER BY data ->> 'name'";
        using var reader = cmd.ExecuteReader();
        var results = new List<Subject>();
        while (reader.Read())
        {
            var subject = JsonHelpers.Deserialize<Subject>(reader.GetString(1));
            subject.Id = reader.GetString(0);
            results.Add(subject);
        }
        return results;
    }

    public Subject? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Subjects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var subject = JsonHelpers.Deserialize<Subject>(reader.GetString(1));
        subject.Id = reader.GetString(0);
        return subject;
    }

    /// <summary>Returns true if any courses belong to this subject.</summary>
    public bool HasCourses(string subjectId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Courses WHERE subject_id = $id";
        cmd.Parameters.AddWithValue("$id", subjectId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Returns true if a subject with this name already exists (case-insensitive).
    /// Pass excludeId to ignore the subject currently being edited.
    /// </summary>
    public bool ExistsByName(string name, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM Subjects WHERE LOWER(data ->> 'name') = LOWER($name)"
            : "SELECT COUNT(*) FROM Subjects WHERE LOWER(data ->> 'name') = LOWER($name) AND id != $excludeId";
        cmd.Parameters.AddWithValue("$name", name);
        if (excludeId is not null) cmd.Parameters.AddWithValue("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Returns true if a subject with this calendar abbreviation already exists (case-insensitive).
    /// Pass excludeId to ignore the subject currently being edited.
    /// </summary>
    public bool ExistsByAbbreviation(string abbreviation, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM Subjects WHERE LOWER(data ->> 'calendarAbbreviation') = LOWER($abbr)"
            : "SELECT COUNT(*) FROM Subjects WHERE LOWER(data ->> 'calendarAbbreviation') = LOWER($abbr) AND id != $excludeId";
        cmd.Parameters.AddWithValue("$abbr", abbreviation);
        if (excludeId is not null) cmd.Parameters.AddWithValue("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(Subject subject)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Subjects (id, data) VALUES ($id, $data)";
        cmd.Parameters.AddWithValue("$id", subject.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(subject));
        cmd.ExecuteNonQuery();
    }

    public void Update(Subject subject)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Subjects SET data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", subject.Id);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(subject));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Subjects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
