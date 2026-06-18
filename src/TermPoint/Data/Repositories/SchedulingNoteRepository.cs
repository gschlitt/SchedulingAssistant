using TermPoint.Models;

namespace TermPoint.Data.Repositories;

/// <summary>
/// SQLite-backed repository for <see cref="SchedulingNote"/>. There is at most one row per
/// (instructor, semester) pair, so <see cref="Save"/> performs an upsert keyed on that pair.
/// The <c>instructor_name</c> and <c>semester_name</c> columns are denormalized copies kept
/// for human-readability of the raw table; the app reads its data from the JSON column.
/// </summary>
public class SchedulingNoteRepository(IDatabaseContext db) : ISchedulingNoteRepository
{
    public SchedulingNote? Get(string semesterId, string instructorId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, instructor_id, semester_id, data FROM SchedulingNotes WHERE semester_id = $sid AND instructor_id = $iid";
        cmd.AddParam("$sid", semesterId);
        cmd.AddParam("$iid", instructorId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadNote(reader);
    }

    public List<SchedulingNote> GetBySemester(string semesterId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, instructor_id, semester_id, data FROM SchedulingNotes WHERE semester_id = $sid";
        cmd.AddParam("$sid", semesterId);
        using var reader = cmd.ExecuteReader();
        var results = new List<SchedulingNote>();
        while (reader.Read())
            results.Add(ReadNote(reader));
        return results;
    }

    public void Save(SchedulingNote note)
    {
        db.MarkDirty();

        // One note per (instructor, semester): reuse the existing row's id if present so the
        // surrogate key stays stable, otherwise insert a fresh row.
        var existingId = GetExistingId(note.SemesterId, note.InstructorId);

        using var cmd = db.Connection.CreateCommand();
        if (existingId is null)
        {
            cmd.CommandText = """
                INSERT INTO SchedulingNotes (id, instructor_id, semester_id, instructor_name, semester_name, data)
                VALUES ($id, $iid, $sid,
                    (SELECT first_name || ' ' || last_name FROM Instructors WHERE id = $iid),
                    (SELECT ay.name || ' — ' || s.name FROM Semesters s JOIN AcademicYears ay ON ay.id = s.academic_year_id WHERE s.id = $sid),
                    $data)
                """;
            cmd.AddParam("$id", note.Id);
        }
        else
        {
            cmd.CommandText = """
                UPDATE SchedulingNotes SET
                    instructor_name = (SELECT first_name || ' ' || last_name FROM Instructors WHERE id = $iid),
                    semester_name   = (SELECT ay.name || ' — ' || s.name FROM Semesters s JOIN AcademicYears ay ON ay.id = s.academic_year_id WHERE s.id = $sid),
                    data            = $data
                WHERE id = $id
                """;
            cmd.AddParam("$id", existingId);
        }

        cmd.AddParam("$iid", note.InstructorId);
        cmd.AddParam("$sid", note.SemesterId);
        cmd.AddParam("$data", JsonHelpers.Serialize(note));
        cmd.ExecuteNonQuery();
    }

    private string? GetExistingId(string semesterId, string instructorId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM SchedulingNotes WHERE semester_id = $sid AND instructor_id = $iid";
        cmd.AddParam("$sid", semesterId);
        cmd.AddParam("$iid", instructorId);
        return cmd.ExecuteScalar() as string;
    }

    private static SchedulingNote ReadNote(System.Data.Common.DbDataReader reader)
    {
        var note = JsonHelpers.Deserialize<SchedulingNote>(reader.GetString(3));
        note.Id = reader.GetString(0);
        note.InstructorId = reader.GetString(1);
        note.SemesterId = reader.GetString(2);
        return note;
    }
}
