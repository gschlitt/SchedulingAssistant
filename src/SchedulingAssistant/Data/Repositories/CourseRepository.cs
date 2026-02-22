using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class CourseRepository(DatabaseContext db)
{
    public List<Course> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, subject_id, data FROM Courses ORDER BY data ->> 'calendarCode'";
        return ReadCourses(cmd);
    }

    public List<Course> GetBySubject(string subjectId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, subject_id, data FROM Courses WHERE subject_id = $sid ORDER BY data ->> 'calendarCode'";
        cmd.Parameters.AddWithValue("$sid", subjectId);
        return ReadCourses(cmd);
    }

    public List<Course> GetAllActive()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, subject_id, data FROM Courses WHERE CAST(data ->> 'isActive' AS INTEGER) = 1 ORDER BY data ->> 'calendarCode'";
        return ReadCourses(cmd);
    }

    public Course? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, subject_id, data FROM Courses WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadCourses(cmd).FirstOrDefault();
    }

    /// <summary>Returns true if any sections reference this course.</summary>
    public bool HasSections(string courseId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Sections WHERE course_id = $id";
        cmd.Parameters.AddWithValue("$id", courseId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Returns true if a course with this calendar code already exists (case-insensitive).
    /// Pass excludeId to ignore the course currently being edited.
    /// </summary>
    public bool ExistsByCalendarCode(string code, string? excludeId = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM Courses WHERE LOWER(data ->> 'calendarCode') = LOWER($code)"
            : "SELECT COUNT(*) FROM Courses WHERE LOWER(data ->> 'calendarCode') = LOWER($code) AND id != $excludeId";
        cmd.Parameters.AddWithValue("$code", code);
        if (excludeId is not null) cmd.Parameters.AddWithValue("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(Course course)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Courses (id, subject_id, data) VALUES ($id, $sid, $data)";
        cmd.Parameters.AddWithValue("$id", course.Id);
        cmd.Parameters.AddWithValue("$sid", course.SubjectId);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(course));
        cmd.ExecuteNonQuery();
    }

    public void Update(Course course)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE Courses SET subject_id = $sid, data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", course.Id);
        cmd.Parameters.AddWithValue("$sid", course.SubjectId);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(course));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Courses WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<Course> ReadCourses(Microsoft.Data.Sqlite.SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<Course>();
        while (reader.Read())
        {
            var course = JsonHelpers.Deserialize<Course>(reader.GetString(2));
            course.Id = reader.GetString(0);
            course.SubjectId = reader.GetString(1);
            results.Add(course);
        }
        return results;
    }
}
