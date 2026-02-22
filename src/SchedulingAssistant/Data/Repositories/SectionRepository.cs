using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class SectionRepository(DatabaseContext db)
{
    public List<Section> GetAll(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, course_id, instructor_id, room_id, data FROM Sections WHERE semester_id = $sid ORDER BY data ->> 'sectionCode'";
        cmd.Parameters.AddWithValue("$sid", semesterId);
        return ReadSections(cmd);
    }

    public Section? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, semester_id, course_id, instructor_id, room_id, data FROM Sections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadSections(cmd).FirstOrDefault();
    }

    public void Insert(Section section)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Sections (id, semester_id, course_id, instructor_id, room_id, data) VALUES ($id, $sid, $cid, $iid, $rid, $data)";
        cmd.Parameters.AddWithValue("$id", section.Id);
        cmd.Parameters.AddWithValue("$sid", section.SemesterId);
        cmd.Parameters.AddWithValue("$cid", (object?)section.CourseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iid", (object?)section.InstructorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rid", (object?)section.RoomId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(section));
        cmd.ExecuteNonQuery();
    }

    public void Update(Section section)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "UPDATE Sections SET semester_id = $sid, course_id = $cid, instructor_id = $iid, room_id = $rid, data = $data WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", section.Id);
        cmd.Parameters.AddWithValue("$sid", section.SemesterId);
        cmd.Parameters.AddWithValue("$cid", (object?)section.CourseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iid", (object?)section.InstructorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rid", (object?)section.RoomId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(section));
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
        cmd.Parameters.AddWithValue("$ayid", academicYearId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Sections WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<Section> ReadSections(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<Section>();
        while (reader.Read())
        {
            var section = JsonHelpers.Deserialize<Section>(reader.GetString(5));
            section.Id = reader.GetString(0);
            section.SemesterId = reader.GetString(1);
            section.CourseId = reader.IsDBNull(2) ? null : reader.GetString(2);
            section.InstructorId = reader.IsDBNull(3) ? null : reader.GetString(3);
            section.RoomId = reader.IsDBNull(4) ? null : reader.GetString(4);
            results.Add(section);
        }
        return results;
    }
}
