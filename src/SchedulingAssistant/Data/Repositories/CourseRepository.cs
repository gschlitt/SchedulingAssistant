using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class CourseRepository(IDatabaseContext db) : ICourseRepository
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
        cmd.AddParam("$sid", subjectId);
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
        cmd.AddParam("$id", id);
        return ReadCourses(cmd).FirstOrDefault();
    }

    /// <summary>Returns true if any sections reference this course.</summary>
    public bool HasSections(string courseId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Sections WHERE course_id = $id";
        cmd.AddParam("$id", courseId);
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
        cmd.AddParam("$code", code);
        if (excludeId is not null) cmd.AddParam("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Insert(Course course)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Courses (id, subject_id, calendar_code, title, data) " +
            "VALUES ($id, $sid, $calendarCode, $title, $data)";
        cmd.AddParam("$id", course.Id);
        cmd.AddParam("$sid", course.SubjectId);
        cmd.AddParam("$calendarCode", course.CalendarCode);
        cmd.AddParam("$title",        course.Title);
        cmd.AddParam("$data", JsonHelpers.Serialize(course));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Updates an existing course. Optionally participates in a caller-managed transaction.
    /// </summary>
    /// <param name="course">The course to update.</param>
    /// <param name="tx">Optional transaction to join. Pass null for auto-commit behaviour.</param>
    public void Update(Course course, System.Data.Common.DbTransaction? tx = null)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "UPDATE Courses SET subject_id = $sid, calendar_code = $calendarCode, title = $title, data = $data WHERE id = $id";
        cmd.AddParam("$id", course.Id);
        cmd.AddParam("$sid", course.SubjectId);
        cmd.AddParam("$calendarCode", course.CalendarCode);
        cmd.AddParam("$title",        course.Title);
        cmd.AddParam("$data", JsonHelpers.Serialize(course));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        db.MarkDirty();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Courses WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<Course> ReadCourses(System.Data.Common.DbCommand cmd)
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
