using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class SectionRepository(IDatabaseContext db) : ISectionRepository
{
    public List<Section> GetAll()
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, semester_id, course_id, data FROM Sections";
        return ReadSections(cmd);
    }

    public List<Section> GetAll(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, course_id, data FROM Sections WHERE semester_id = $sid ORDER BY data ->> 'sectionCode'";
        cmd.AddParam("$sid", semesterId);
        return ReadSections(cmd);
    }

    public Section? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, course_id, data FROM Sections WHERE id = $id";
        cmd.AddParam("$id", id);
        return ReadSections(cmd).FirstOrDefault();
    }

    /// <summary>
    /// Returns all sections for the given course across all semesters, ordered by section code.
    /// </summary>
    public List<Section> GetByCourseId(string courseId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, course_id, data FROM Sections WHERE course_id = $cid ORDER BY data ->> 'sectionCode'";
        cmd.AddParam("$cid", courseId);
        return ReadSections(cmd);
    }

    public void Insert(Section section, System.Data.Common.DbTransaction? tx = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        // room_id column kept in schema for backward compat but always NULL — room is now per-meeting in JSON
        // course_code is denormalised from Courses via subquery for easy DB browsing
        cmd.CommandText = """
            INSERT INTO Sections (id, semester_id, course_id, room_id, section_code, course_code,
                                  semester_name, academic_year_name, data)
            VALUES ($id, $sid, $cid, NULL, $sectionCode,
                    (SELECT json_extract(data, '$.calendarCode') FROM Courses WHERE id = $cid),
                    (SELECT name FROM Semesters WHERE id = $sid),
                    (SELECT ay.name FROM AcademicYears ay
                     JOIN Semesters s ON s.academic_year_id = ay.id WHERE s.id = $sid),
                    $data)
            """;
        cmd.AddParam("$id", section.Id);
        cmd.AddParam("$sid", section.SemesterId);
        cmd.AddParam("$cid", section.CourseId);
        cmd.AddParam("$sectionCode", section.SectionCode);
        cmd.AddParam("$data", JsonHelpers.Serialize(section));
        cmd.ExecuteNonQuery();
    }

    public void Update(Section section, System.Data.Common.DbTransaction? tx = null)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        // room_id column kept in schema for backward compat but always NULL — room is now per-meeting in JSON
        // course_code is denormalised from Courses via subquery for easy DB browsing
        cmd.CommandText = """
            UPDATE Sections
            SET semester_id        = $sid,
                course_id          = $cid,
                room_id            = NULL,
                section_code       = $sectionCode,
                course_code        = (SELECT json_extract(data, '$.calendarCode') FROM Courses WHERE id = $cid),
                semester_name      = (SELECT name FROM Semesters WHERE id = $sid),
                academic_year_name = (SELECT ay.name FROM AcademicYears ay
                                      JOIN Semesters s ON s.academic_year_id = ay.id WHERE s.id = $sid),
                data               = $data
            WHERE id = $id
            """;
        cmd.AddParam("$id", section.Id);
        cmd.AddParam("$sid", section.SemesterId);
        cmd.AddParam("$cid", section.CourseId);
        cmd.AddParam("$sectionCode", section.SectionCode);
        cmd.AddParam("$data", JsonHelpers.Serialize(section));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the total number of sections across all semesters of the given academic year.
    /// </summary>
    public int CountByAcademicYear(string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM Sections WHERE semester_id IN (SELECT id FROM Semesters WHERE academic_year_id = $ayid)";
        cmd.AddParam("$ayid", academicYearId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Returns true if another section for the same course in the same semester already uses
    /// <paramref name="sectionCode"/> (case-insensitive).
    /// Pass <paramref name="excludeId"/> = the current section's id when editing (null for new sections).
    /// </summary>
    public bool ExistsBySectionCode(string semesterId, string courseId, string sectionCode, string? excludeId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = excludeId is null
            ? "SELECT COUNT(*) FROM Sections WHERE semester_id = $sid AND course_id = $cid AND LOWER(data ->> 'sectionCode') = LOWER($code)"
            : "SELECT COUNT(*) FROM Sections WHERE semester_id = $sid AND course_id = $cid AND LOWER(data ->> 'sectionCode') = LOWER($code) AND id != $excludeId";
        cmd.AddParam("$sid", semesterId);
        cmd.AddParam("$cid", courseId);
        cmd.AddParam("$code", sectionCode);
        if (excludeId is not null)
            cmd.AddParam("$excludeId", excludeId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Sections WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the total number of sections in the given semester.
    /// </summary>
    public int CountBySemesterId(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Sections WHERE semester_id = $sid";
        cmd.AddParam("$sid", semesterId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Deletes all sections from the given semester.
    /// </summary>
    public void DeleteBySemesterId(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Sections WHERE semester_id = $sid";
        cmd.AddParam("$sid", semesterId);
        cmd.ExecuteNonQuery();
    }

    private static List<Section> ReadSections(System.Data.Common.DbCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<Section>();
        while (reader.Read())
        {
            var section = JsonHelpers.Deserialize<Section>(reader.GetString(3));
            section.Id = reader.GetString(0);
            section.SemesterId = reader.GetString(1);
            section.CourseId = reader.IsDBNull(2) ? null : reader.GetString(2);
            results.Add(section);
        }
        return results;
    }
}
