using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the backup retention rule of 1.2.4 item 4.9 (<see cref="BackupService.SelectBackupsToDelete"/> and
/// <see cref="BackupService.RotateBackups"/>): keep the newest N, plus the last backup of each of the 7 days before
/// today and of each of the 4 weeks before this one that have backups; delete the rest; never touch a file that is
/// not a backup of this database.
/// </summary>
public sealed class BackupRetentionTests : IDisposable
{
    /// <summary>"Now" for every test: Wednesday 11 November 2026, 10:00 (its week starts Monday 9 November).</summary>
    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0);

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for the file-level tests.</summary>
    public BackupRetentionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"retention_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A backup file name of database <paramref name="prefix"/> taken at <paramref name="at"/>.</summary>
    /// <param name="at">When the backup was taken (local time).</param>
    /// <param name="prefix">The database name.</param>
    /// <returns>A path such as <c>C:\b\BIOL_2026-11-01_17-00-00.db</c>.</returns>
    private static string B(DateTime at, string prefix = "BIOL")
        => Path.Combine(@"C:\b", $"{prefix}_{at:yyyy-MM-dd_HH-mm-ss}.db");

    /// <summary>
    /// Alice's story: one backup a day for ten days, then six restarts this morning. The old rule (newest 5) would
    /// have kept only this morning's. Now the newest 5 stay, the last backup of each of the 7 days before today stays
    /// (4–10 November), and the last backup of the week before that (1 November) stays; only 2 and 3 November and
    /// the oldest of this morning's six go.
    /// </summary>
    [Fact]
    public void RestartsThisMorning_KeepTheEarlierDaysAndWeeks()
    {
        var daily = Enumerable.Range(1, 10).Select(d => B(new DateTime(2026, 11, d, 17, 0, 0))).ToList();
        var today = Enumerable.Range(0, 6).Select(i => B(new DateTime(2026, 11, 11, 8, i, 0))).ToList();

        var deleted = BackupService.SelectBackupsToDelete(daily.Concat(today), "BIOL", recentCount: 5, Now);

        Assert.Equal(new[] { daily[1], daily[2], today[0] }, deleted);   // 2 Nov, 3 Nov, oldest of today; oldest first
    }

    /// <summary>Of several backups on one earlier day, only that day's last one is kept by the day rule.</summary>
    [Fact]
    public void AnEarlierDay_KeepsOnlyItsLastBackup()
    {
        var morning = B(new DateTime(2026, 11, 10, 9, 0, 0));
        var noon    = B(new DateTime(2026, 11, 10, 12, 0, 0));
        var evening = B(new DateTime(2026, 11, 10, 18, 0, 0));
        var todays  = B(new DateTime(2026, 11, 11, 9, 0, 0));

        var deleted = BackupService.SelectBackupsToDelete(new[] { morning, noon, evening, todays }, "BIOL", 1, Now);

        Assert.Equal(new[] { morning, noon }, deleted);
    }

    /// <summary>
    /// Days are counted over days that HAVE a backup: a database nobody opened since September still keeps its
    /// September days, instead of losing them to the calendar.
    /// </summary>
    [Fact]
    public void AnIdleGap_DoesNotAgeOutTheDaysBeforeIt()
    {
        var september = Enumerable.Range(1, 3).Select(d => B(new DateTime(2026, 9, d, 16, 0, 0))).ToList();
        var todays    = Enumerable.Range(0, 3).Select(i => B(new DateTime(2026, 11, 11, 9, i, 0))).ToList();

        var deleted = BackupService.SelectBackupsToDelete(september.Concat(todays), "BIOL", 1, Now);

        Assert.Equal(new[] { todays[0], todays[1] }, deleted);   // only the extra ones from today go
    }

    /// <summary>
    /// One backup a week for ten weeks: each is its own day, so the 7-day rule keeps the latest seven (which cover
    /// the 4-week rule's four); the three oldest go.
    /// </summary>
    [Fact]
    public void WeeklyBackups_KeepTheLatestSevenDaysWorth()
    {
        var weekly = Enumerable.Range(1, 10)
            .Select(w => B(new DateTime(2026, 11, 4, 12, 0, 0).AddDays(-7 * w)))   // Wednesdays before this week
            .ToList();                                                           // newest first

        var deleted = BackupService.SelectBackupsToDelete(weekly, "BIOL", 1, Now);

        Assert.Equal(new[] { weekly[9], weekly[8], weekly[7] }, deleted);
    }

    /// <summary>The week rule reaches back further than the day rule: four earlier weeks, each keeping its last backup.</summary>
    [Fact]
    public void ManyBackupsPerDay_TheWeekRuleKeepsOnePerEarlierWeek()
    {
        // Every day from 5 October to 8 November, 3 backups a day.
        var all = new List<string>();
        for (var d = new DateTime(2026, 10, 5); d <= new DateTime(2026, 11, 8); d = d.AddDays(1))
            for (var h = 9; h <= 11; h++)
                all.Add(B(d.AddHours(h)));

        var kept = all.Except(BackupService.SelectBackupsToDelete(all, "BIOL", 1, Now)).ToList();

        var expected = new[]
        {
            B(new DateTime(2026, 11, 8, 11, 0, 0)),   // newest (N = 1), also the day rule's first
            B(new DateTime(2026, 11, 7, 11, 0, 0)), B(new DateTime(2026, 11, 6, 11, 0, 0)),
            B(new DateTime(2026, 11, 5, 11, 0, 0)), B(new DateTime(2026, 11, 4, 11, 0, 0)),
            B(new DateTime(2026, 11, 3, 11, 0, 0)), B(new DateTime(2026, 11, 2, 11, 0, 0)),   // 7 days: 2–8 Nov
            B(new DateTime(2026, 11, 1, 11, 0, 0)),   // week of 26 Oct
            B(new DateTime(2026, 10, 25, 11, 0, 0)),  // week of 19 Oct
            B(new DateTime(2026, 10, 18, 11, 0, 0)),  // week of 12 Oct (4th earlier week; week of 2 Nov is the first)
        };
        Assert.Equal(expected.OrderBy(p => p), kept.OrderBy(p => p));
    }

    /// <summary>Never deletes more than the old rule: with N at least the number of backups, nothing goes.</summary>
    [Fact]
    public void RecentCountCoveringEverything_DeletesNothing()
    {
        var files = Enumerable.Range(0, 10).Select(i => B(new DateTime(2026, 8, 1).AddDays(i * 3))).ToList();

        Assert.Empty(BackupService.SelectBackupsToDelete(files, "BIOL", recentCount: 10, Now));
    }

    /// <summary>
    /// Only this database's backups count: the backups of a database named <c>BIOL_TT</c> also match the folder glob
    /// <c>BIOL_*.db</c> (the old rule deleted them), and so does a file that is not a backup at all. Neither is ever
    /// returned.
    /// </summary>
    [Fact]
    public void OtherDatabasesAndNonBackups_AreNeverDeleted()
    {
        var others = new[]
        {
            B(new DateTime(2026, 1, 1, 9, 0, 0), "BIOL_TT"),
            B(new DateTime(2026, 1, 2, 9, 0, 0), "BIOL_TT"),
            Path.Combine(@"C:\b", "BIOL_notes.db"),
        };
        var ours = Enumerable.Range(0, 3).Select(i => B(new DateTime(2026, 11, 11, 9, i, 0))).ToList();

        var deleted = BackupService.SelectBackupsToDelete(others.Concat(ours), "BIOL", 1, Now);

        Assert.Equal(new[] { ours[0], ours[1] }, deleted);
    }

    /// <summary>The file-level rotation deletes each dropped backup with its companion CSV, and leaves everything else.</summary>
    [Fact]
    public void RotateBackups_DeletesDroppedPairs_AndLeavesTheRest()
    {
        string Make(string name) { var p = Path.Combine(_tempDir, name); File.WriteAllText(p, "x"); return p; }

        var oldDb   = Make("BIOL_2026-11-10_09-00-00.db");
        var oldCsv  = Make("BIOL_2026-11-10_09-00-00_2026-2027_sections.csv");
        var keptDb  = Make("BIOL_2026-11-10_18-00-00.db");
        var keptCsv = Make("BIOL_2026-11-10_18-00-00_2026-2027_sections.csv");
        var newest  = Make("BIOL_2026-11-11_09-00-00.db");
        var other   = Make("BIOL_TT_2026-01-01_09-00-00.db");

        BackupService.RotateBackups(_tempDir, "BIOL", maxCount: 1, now: Now);

        Assert.False(File.Exists(oldDb));
        Assert.False(File.Exists(oldCsv));
        Assert.True(File.Exists(keptDb));
        Assert.True(File.Exists(keptCsv));
        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(other));
    }
}
