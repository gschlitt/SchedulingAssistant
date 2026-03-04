using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class InstructorRepository(DatabaseContext db)
{
    public List<Instructor> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Instructors ORDER BY data ->> 'lastName', data ->> 'firstName'";
        using var reader = cmd.ExecuteReader();
        var results = new List<Instructor>();
        while (reader.Read())
        {
            var instructor = JsonHelpers.Deserialize<Instructor>(reader.GetString(1));
            instructor.Id = reader.GetString(0);
            results.Add(instructor);
        }
        return results;
    }

    public Instructor? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, data FROM Instructors WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var instructor = JsonHelpers.Deserialize<Instructor>(reader.GetString(1));
        instructor.Id = reader.GetString(0);
        return instructor;
    }

    /// <summary>Returns true if any sections reference this instructor (searches JSON instructorAssignments array).</summary>
    public bool HasSections(string instructorId)
    {
        using var cmd = db.Connection.CreateCommand();
        // instructorAssignments is a JSON array of objects; each object has an instructorId property
        cmd.CommandText = """
            SELECT COUNT(*) FROM Sections
            WHERE EXISTS (
                SELECT 1 FROM json_each(data, '$.instructorAssignments')
                WHERE json_extract(value, '$.instructorId') = $id
            )
            """;
        cmd.Parameters.AddWithValue("$id", instructorId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Returns true if an instructor with these initials already exists (case-insensitive).
    /// Pass excludeId to ignore the instructor currently being edited.
    /// </summary>
    public bool ExistsByInitials(string initials, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM Instructors WHERE LOWER(data ->> 'initials') = LOWER($initials)"
            : "SELECT COUNT(*) FROM Instructors WHERE LOWER(data ->> 'initials') = LOWER($initials) AND id != $excludeId";
        cmd.Parameters.AddWithValue("$initials", initials);
        if (excludeId is not null) cmd.Parameters.AddWithValue("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(Instructor instructor)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Instructors (id, last_name, first_name, initials, data) " +
            "VALUES ($id, $lastName, $firstName, $initials, $data)";
        cmd.Parameters.AddWithValue("$id", instructor.Id);
        cmd.Parameters.AddWithValue("$lastName",  (object?)instructor.LastName  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$firstName", (object?)instructor.FirstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$initials",  (object?)instructor.Initials  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(instructor));
        cmd.ExecuteNonQuery();
    }

    public void Update(Instructor instructor, SqliteTransaction? tx = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "UPDATE Instructors SET last_name = $lastName, first_name = $firstName, " +
            "initials = $initials, data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", instructor.Id);
        cmd.Parameters.AddWithValue("$lastName",  (object?)instructor.LastName  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$firstName", (object?)instructor.FirstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$initials",  (object?)instructor.Initials  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(instructor));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Instructors WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
