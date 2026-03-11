using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;
using SchedulingAssistant.Services;

namespace SchedulingAssistant.Data;

public static class SeedData
{
    /// <summary>
    /// Called by DatabaseContext after schema initialization.
    /// Ensures baseline records exist (Academic Unit, default Section Prefixes).
    /// </summary>
    public static void EnsureSeeded(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM AcademicUnits";
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        if (count == 0)
        {
            // Create a default Academic Unit
            cmd.CommandText = "INSERT INTO AcademicUnits (id, data) VALUES ($id, $data)";
            cmd.Parameters.Clear();
            var unit = new SchedulingAssistant.Models.AcademicUnit { Name = "Default" };
            cmd.Parameters.AddWithValue("$id", unit.Id);
            cmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(unit));
            cmd.ExecuteNonQuery();
        }

        EnsureDefaultSectionPrefixes(conn);
    }

    /// <summary>
    /// Seeds the default section prefixes when the SectionPrefixes table is empty.
    /// Creates the associated campuses (Abbotsford, Chilliwack) in SectionPropertyValues
    /// if they do not already exist, then inserts the four default prefix records:
    /// AB → Abbotsford, A# → Abbotsford, CH → Chilliwack, C# → Chilliwack.
    /// Uses INSERT OR IGNORE so it is safe to call even if rows already exist.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    private static void EnsureDefaultSectionPrefixes(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SectionPrefixes";
        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
            return; // already seeded

        var abbotsfordId = FindOrCreateCampus(conn, "Abbotsford");
        var chilliwackId = FindOrCreateCampus(conn, "Chilliwack");

        (string PrefixText, string CampusId)[] defaults =
        [
            ("AB", abbotsfordId),
            ("A#", abbotsfordId),
            ("CH", chilliwackId),
            ("C#", chilliwackId),
        ];

        cmd.CommandText =
            "INSERT OR IGNORE INTO SectionPrefixes (id, prefix, data) VALUES ($id, $prefix, $data)";
        var idParam     = cmd.Parameters.Add("$id",     SqliteType.Text);
        var prefixParam = cmd.Parameters.Add("$prefix", SqliteType.Text);
        var dataParam   = cmd.Parameters.Add("$data",   SqliteType.Text);

        foreach (var (prefixText, campusId) in defaults)
        {
            var sp = new SectionPrefix { Prefix = prefixText, CampusId = campusId };
            idParam.Value     = sp.Id;
            prefixParam.Value = sp.Prefix;
            dataParam.Value   = JsonHelpers.Serialize(sp);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Finds a campus in SectionPropertyValues by name (case-insensitive) and returns its ID.
    /// If no matching campus exists, a new one is inserted and its new ID is returned.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="name">Campus name to find or create (e.g. "Abbotsford").</param>
    /// <returns>The ID of the existing or newly created campus record.</returns>
    private static string FindOrCreateCampus(SqliteConnection conn, string name)
    {
        using var findCmd = conn.CreateCommand();
        findCmd.CommandText =
            "SELECT id FROM SectionPropertyValues " +
            "WHERE type = 'campus' AND LOWER(json_extract(data, '$.name')) = LOWER($name)";
        findCmd.Parameters.AddWithValue("$name", name);
        var existing = findCmd.ExecuteScalar() as string;
        if (existing is not null)
            return existing;

        var campus = new SectionPropertyValue { Name = name };
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText =
            "INSERT INTO SectionPropertyValues (id, type, name, data) " +
            "VALUES ($id, 'campus', $name, $data)";
        insertCmd.Parameters.AddWithValue("$id",   campus.Id);
        insertCmd.Parameters.AddWithValue("$name", campus.Name);
        insertCmd.Parameters.AddWithValue("$data", JsonHelpers.Serialize(campus));
        insertCmd.ExecuteNonQuery();
        return campus.Id;
    }

    /// <summary>
    /// Seeds a broadly applicable set of default legal start times for a new academic year.
    /// Called when no persisted data exists and no previous year is available to copy from.
    /// Uses the institution's standard block lengths (1.5h, 2h, 3h, 4h) with their
    /// corresponding start times throughout the academic day.
    /// Administrators can add, edit, or remove these through Scheduling Settings.
    /// Uses INSERT OR IGNORE so it is safe to call even if rows already exist.
    /// </summary>
    /// <param name="conn">Open SQLite connection.</param>
    /// <param name="academicYearId">ID of the academic year to seed.</param>
    public static void SeedDefaultLegalStartTimes(SqliteConnection conn, string academicYearId)
    {
        // Block lengths (hours) and their standard start times (minutes from midnight).
        // These times are institution-specific and optimized for the academic calendar.
        (double BlockLengthHours, int[] StartTimesMinutes)[] defaults =
        [
            // 1.5-hour blocks: 08:30, 10:00, 11:30, 13:00, 14:30, 16:00, 17:30, 18:00
            (1.5,  [510, 600, 690, 780, 870, 960, 1050, 1080]),
            // 2-hour blocks: 08:30, 09:30, 10:30, 13:00, 15:00, 17:30, 18:00, 19:30, 20:00
            (2.0,  [510, 540, 630, 780, 900, 1050, 1080, 1170, 1200]),
            // 3-hour blocks: 08:30, 11:30, 14:30, 16:00, 17:30, 18:00, 18:30, 19:00
            (3.0,  [510, 690, 870, 960, 1050, 1080, 1110, 1140]),
            // 4-hour blocks: 08:30, 13:00, 17:30, 18:00
            (4.0,  [510, 780, 1050, 1080]),
        ];

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO LegalStartTimes (academic_year_id, block_length, start_times) " +
            "VALUES ($ay, $bl, $st)";
        var ayParam = cmd.Parameters.Add("$ay", SqliteType.Text);
        var blParam = cmd.Parameters.Add("$bl", SqliteType.Real);
        var stParam = cmd.Parameters.Add("$st", SqliteType.Text);

        ayParam.Value = academicYearId;
        foreach (var (blockLengthHours, startTimesMinutes) in defaults)
        {
            blParam.Value = blockLengthHours;
            stParam.Value = JsonHelpers.Serialize(new List<int>(startTimesMinutes));
            cmd.ExecuteNonQuery();
        }
    }

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
