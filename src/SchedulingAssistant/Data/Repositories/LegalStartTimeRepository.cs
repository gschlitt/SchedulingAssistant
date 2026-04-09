using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data.Repositories;

public class LegalStartTimeRepository(IDatabaseContext db) : ILegalStartTimeRepository
{
    public List<LegalStartTime> GetAll(string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT block_length, start_times FROM LegalStartTimes WHERE academic_year_id = $ay ORDER BY block_length";
        cmd.AddParam("$ay", academicYearId);
        using var reader = cmd.ExecuteReader();
        var results = new List<LegalStartTime>();
        while (reader.Read())
            results.Add(new LegalStartTime
            {
                BlockLength = reader.GetDouble(0),
                StartTimes = JsonHelpers.Deserialize<List<int>>(reader.GetString(1))
            });
        return results;
    }

    public LegalStartTime? GetByBlockLength(string academicYearId, double blockLength)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT block_length, start_times FROM LegalStartTimes WHERE academic_year_id = $ay AND block_length = $bl";
        cmd.AddParam("$ay", academicYearId);
        cmd.AddParam("$bl", blockLength);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new LegalStartTime
        {
            BlockLength = reader.GetDouble(0),
            StartTimes = JsonHelpers.Deserialize<List<int>>(reader.GetString(1))
        };
    }

    public void Insert(LegalStartTime entry, string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO LegalStartTimes (academic_year_id, academic_year_name, block_length, start_times) " +
            "VALUES ($ay, (SELECT name FROM AcademicYears WHERE id = $ay), $bl, $st)";
        cmd.AddParam("$ay", academicYearId);
        cmd.AddParam("$bl", entry.BlockLength);
        cmd.AddParam("$st", JsonHelpers.Serialize(entry.StartTimes));
        cmd.ExecuteNonQuery();
    }

    public void Update(LegalStartTime entry, string academicYearId)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE LegalStartTimes SET start_times = $st WHERE academic_year_id = $ay AND block_length = $bl";
        cmd.AddParam("$ay", academicYearId);
        cmd.AddParam("$bl", entry.BlockLength);
        cmd.AddParam("$st", JsonHelpers.Serialize(entry.StartTimes));
        cmd.ExecuteNonQuery();
    }

    public void Delete(string academicYearId, double blockLength)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM LegalStartTimes WHERE academic_year_id = $ay AND block_length = $bl";
        cmd.AddParam("$ay", academicYearId);
        cmd.AddParam("$bl", blockLength);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Copies all legal start times from a previous academic year to a new one.
    /// If fromAcademicYearId is null, no copy is performed.
    /// </summary>
    public void CopyFromPreviousYear(string toAcademicYearId, string? fromAcademicYearId)
    {
        if (string.IsNullOrEmpty(fromAcademicYearId)) return;

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LegalStartTimes (academic_year_id, academic_year_name, block_length, start_times)
            SELECT $to_ay, (SELECT name FROM AcademicYears WHERE id = $to_ay), block_length, start_times
            FROM LegalStartTimes
            WHERE academic_year_id = $from_ay
            """;
        cmd.AddParam("$to_ay", toAcademicYearId);
        cmd.AddParam("$from_ay", fromAcademicYearId);
        cmd.ExecuteNonQuery();
    }
}
