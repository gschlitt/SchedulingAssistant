using Microsoft.Data.Sqlite;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Data;

public static class SeedData
{
    private static readonly LegalStartTime[] Defaults =
    [
        new() { BlockLength = 1.5, StartTimes = [510, 600, 690, 780, 870, 960, 1050, 1080] },
        new() { BlockLength = 2.0, StartTimes = [510, 570, 630, 780, 900, 1050, 1080, 1170, 1200] },
        new() { BlockLength = 3.0, StartTimes = [510, 690, 870, 960, 1050, 1080, 1110, 1140] },
        new() { BlockLength = 4.0, StartTimes = [510, 780, 1050, 1080] },
    ];

    public static void EnsureSeeded(SqliteConnection conn)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM LegalStartTimes";
        var count = (long)countCmd.ExecuteScalar()!;
        if (count > 0) return;

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText =
            "INSERT OR IGNORE INTO LegalStartTimes (block_length, start_times) VALUES ($bl, $st)";
        var blParam = insertCmd.Parameters.Add("$bl", SqliteType.Real);
        var stParam = insertCmd.Parameters.Add("$st", SqliteType.Text);

        foreach (var entry in Defaults)
        {
            blParam.Value = entry.BlockLength;
            stParam.Value = JsonHelpers.Serialize(entry.StartTimes);
            insertCmd.ExecuteNonQuery();
        }
    }
}
