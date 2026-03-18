namespace SchedulingAssistant.Data.Repositories;

using SchedulingAssistant.Models;

public class InstructorCommitmentRepository(IDatabaseContext db) : IInstructorCommitmentRepository
{
    public List<InstructorCommitment> GetByInstructor(string semesterId, string instructorId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, instructor_id, semester_id, data FROM InstructorCommitments WHERE semester_id = $sid AND instructor_id = $iid ORDER BY json_extract(data, '$.name')";
        cmd.AddParam("$sid", semesterId);
        cmd.AddParam("$iid", instructorId);
        return ReadCommitments(cmd);
    }

    public InstructorCommitment? GetById(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, instructor_id, semester_id, data FROM InstructorCommitments WHERE id = $id";
        cmd.AddParam("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadCommitment(reader);
    }

    public void Insert(InstructorCommitment commitment)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO InstructorCommitments (id, instructor_id, semester_id, instructor_name, semester_name, data)
            VALUES ($id, $iid, $sid,
                (SELECT first_name || ' ' || last_name FROM Instructors WHERE id = $iid),
                (SELECT ay.name || ' — ' || s.name FROM Semesters s JOIN AcademicYears ay ON ay.id = s.academic_year_id WHERE s.id = $sid),
                $data)
            """;
        cmd.AddParam("$id", commitment.Id);
        cmd.AddParam("$iid", commitment.InstructorId);
        cmd.AddParam("$sid", commitment.SemesterId);
        cmd.AddParam("$data", JsonHelpers.Serialize(commitment));
        cmd.ExecuteNonQuery();
    }

    public void Update(InstructorCommitment commitment)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            UPDATE InstructorCommitments SET
                instructor_id   = $iid,
                semester_id     = $sid,
                instructor_name = (SELECT first_name || ' ' || last_name FROM Instructors WHERE id = $iid),
                semester_name   = (SELECT ay.name || ' — ' || s.name FROM Semesters s JOIN AcademicYears ay ON ay.id = s.academic_year_id WHERE s.id = $sid),
                data            = $data
            WHERE id = $id
            """;
        cmd.AddParam("$id", commitment.Id);
        cmd.AddParam("$iid", commitment.InstructorId);
        cmd.AddParam("$sid", commitment.SemesterId);
        cmd.AddParam("$data", JsonHelpers.Serialize(commitment));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM InstructorCommitments WHERE id = $id";
        cmd.AddParam("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<InstructorCommitment> ReadCommitments(System.Data.Common.DbCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var results = new List<InstructorCommitment>();
        while (reader.Read())
            results.Add(ReadCommitment(reader));
        return results;
    }

    private static InstructorCommitment ReadCommitment(System.Data.Common.DbDataReader reader)
    {
        var commitment = JsonHelpers.Deserialize<InstructorCommitment>(reader.GetString(3));
        commitment.Id = reader.GetString(0);
        commitment.InstructorId = reader.GetString(1);
        commitment.SemesterId = reader.GetString(2);
        return commitment;
    }
}
