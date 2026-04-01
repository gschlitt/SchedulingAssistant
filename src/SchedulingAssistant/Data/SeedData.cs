using System.Data.Common;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data;

public static class SeedData
{
    /// <summary>
    /// Called by DatabaseContext after schema initialization.
    /// Ensures a baseline record exists, namely a Default Academic Unit
    /// </summary>
    public static void EnsureSeeded(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AcademicUnits";
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        if (count == 0)
        {
            // Create a default Academic Unit
            cmd.CommandText = "INSERT INTO AcademicUnits (id, data) VALUES ($id, $data)";
            cmd.Parameters.Clear();
            var unit = new SchedulingAssistant.Models.AcademicUnit { Name = "Default Academic Unit" };
            cmd.AddParam("$id", unit.Id);
            cmd.AddParam("$data", JsonHelpers.Serialize(unit));
            cmd.ExecuteNonQuery();
        }   
    }





    /// <summary>
    /// Finds a campus in the <c>Campuses</c> table by name (case-insensitive) and returns its ID.
    /// If no matching campus exists, a new one is inserted and its new ID is returned.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="name">Campus name to find or create.</param>
    /// <returns>The ID of the existing or newly created campus record.</returns>
    public static string FindOrCreateCampus(DbConnection conn, string name)
    {
        using var findCmd = conn.CreateCommand();
        findCmd.CommandText =
            "SELECT id FROM Campuses WHERE LOWER(json_extract(data, '$.Name')) = LOWER($name)";
        findCmd.AddParam("$name", name);
        var existing = findCmd.ExecuteScalar() as string;
        if (existing is not null)
            return existing;

        var campus = new SchedulingAssistant.Models.Campus { Name = name };
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText =
            "INSERT INTO Campuses (id, name, data) VALUES ($id, $name, $data)";
        insertCmd.AddParam("$id",   campus.Id);
        insertCmd.AddParam("$name", campus.Name);
        insertCmd.AddParam("$data", JsonHelpers.Serialize(campus));
        insertCmd.ExecuteNonQuery();
        return campus.Id;
    }

    /// <summary>
    /// Seeds the default legal start times from <see cref="AppDefaults.LegalStartTimes"/>
    /// into the given academic year. Called by the data migration utility when creating an
    /// academic year that has no existing start-time configuration to copy from.
    /// Uses INSERT OR IGNORE so it is safe to call even if rows already exist.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="academicYearId">ID of the academic year to seed.</param>
    public static void SeedDefaultLegalStartTimes(DbConnection conn, string academicYearId)
    {
        var data = AppDefaults.LegalStartTimes
            .Select(x => (x.BlockHours, new List<int>(x.StartMinutes)))
            .ToList<(double BlockLengthHours, List<int> StartMinutes)>();
        SeedWizardLegalStartTimes(conn, academicYearId, data);
    }

    /// <summary>
    /// Seeds legal start times from caller-supplied data into the given academic year.
    /// Used by the startup wizard (manual and .tpconfig paths) and by the
    /// Files / New Database config-transfer path.
    /// Uses INSERT OR IGNORE, so it is safe to call even if some rows already exist.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="academicYearId">ID of the academic year to seed.</param>
    /// <param name="data">
    /// Sequence of (block length in hours, list of start times in minutes from midnight).
    /// Rows whose start-time list is empty are skipped.
    /// </param>
    public static void SeedWizardLegalStartTimes(
        DbConnection conn,
        string academicYearId,
        IEnumerable<(double BlockLengthHours, List<int> StartMinutes)> data)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO LegalStartTimes (academic_year_id, block_length, start_times) " +
            "VALUES ($ay, $bl, $st)";
        var ayParam = cmd.CreateParameter(); ayParam.ParameterName = "$ay"; cmd.Parameters.Add(ayParam);
        var blParam = cmd.CreateParameter(); blParam.ParameterName = "$bl"; cmd.Parameters.Add(blParam);
        var stParam = cmd.CreateParameter(); stParam.ParameterName = "$st"; cmd.Parameters.Add(stParam);

        ayParam.Value = academicYearId;
        foreach (var (blockLengthHours, startMinutes) in data)
        {
            if (startMinutes.Count == 0) continue;
            blParam.Value = blockLengthHours;
            stParam.Value = JsonHelpers.Serialize(startMinutes);
            cmd.ExecuteNonQuery();
        }
    }

    
}
