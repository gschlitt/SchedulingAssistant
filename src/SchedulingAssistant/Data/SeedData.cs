using Microsoft.Data.Sqlite;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.Data;

public static class SeedData
{
    /// <summary>
    /// Called by DatabaseContext after schema initialization. New databases are left empty —
    /// users create their first academic year explicitly, at which point they are offered
    /// the persisted configuration if it exists.
    /// </summary>
    public static void EnsureSeeded(SqliteConnection conn) { }

    /// <summary>
    /// Import persisted start times data for a specific academic year.
    /// Used when a user creates a new academic year and chooses to import from persisted config.
    /// </summary>
    public static void ImportPersistedStartTimes(SqliteConnection conn, string academicYearId)
    {
        var persistedData = LegalStartTimesDataStore.LoadPersistedData();
        if (persistedData?.AcademicYears.Count == 0) return;
        if (persistedData == null) return;

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText =
            "INSERT OR IGNORE INTO LegalStartTimes (academic_year_id, block_length, start_times) VALUES ($ay, $bl, $st)";
        var ayParam = insertCmd.Parameters.Add("$ay", SqliteType.Text);
        var blParam = insertCmd.Parameters.Add("$bl", SqliteType.Real);
        var stParam = insertCmd.Parameters.Add("$st", SqliteType.Text);

        // Use the first academic year's configuration from the persisted data
        var firstAyExport = persistedData.AcademicYears.FirstOrDefault();
        if (firstAyExport == null) return;

        foreach (var blockLength in firstAyExport.BlockLengths)
        {
            ayParam.Value = academicYearId;
            blParam.Value = blockLength.BlockLengthHours;
            stParam.Value = JsonHelpers.Serialize(blockLength.StartTimesMinutes);
            insertCmd.ExecuteNonQuery();
        }
    }
}
