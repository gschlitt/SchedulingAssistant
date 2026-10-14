using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="BackupService.CheckIntegrityWithSummary"/> (spec item 5, step 5d): the same verdicts as
/// <see cref="BackupService.CheckIntegrityDetailed"/> — which now delegates to it and must behave exactly as before
/// — plus a short SQLite summary of why a file did not pass. The summary names pages and tables, never data, and
/// never a path; it is what the damage report of step 5e will carry.
/// </summary>
public sealed class BackupServiceIntegritySummaryTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public BackupServiceIntegritySummaryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"integrity_sum_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Full path of <paramref name="name"/> inside the temp directory.</summary>
    private string PathOf(string name = "test.db") => Path.Combine(_tempDir, name);

    // ── Ok ────────────────────────────────────────────────────────────────────

    /// <summary>A healthy database is Ok, with NO summary.</summary>
    [Fact]
    public void HealthyDatabase_IsOk_WithANullSummary()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);

        var (result, summary) = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Ok, result);
        Assert.Null(summary);
    }

    // ── Damaged by integrity_check ────────────────────────────────────────────

    /// <summary>
    /// A database with a trashed page is Damaged, and the summary is the first lines of SQLite's
    /// <c>integrity_check</c> output: non-empty, at most 5 lines, and free of the file's path.
    /// </summary>
    [Fact]
    public void DamagedPage_IsDamaged_WithAShortSummaryOfAtMostFiveLines()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        DbFileTestHelpers.TrashPage(path, pageNumber: 2);

        var (result, summary) = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Damaged, result);
        Assert.False(string.IsNullOrWhiteSpace(summary));
        var lines = summary!.Split('\n');
        Assert.InRange(lines.Length, 1, BackupService.IntegritySummaryMaxLines);
        Assert.DoesNotContain(_tempDir, summary);
        Assert.NotEqual("ok", lines[0], StringComparer.OrdinalIgnoreCase);   // it describes a problem
    }

    /// <summary>
    /// A file with MANY problems still yields at most 5 lines: damaging several pages of a larger file makes
    /// <c>integrity_check</c> report more than that, and the summary is cut at the fifth.
    /// </summary>
    [Fact]
    public void ManyProblems_AreCutAtFiveLines()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        for (var page = 2; page <= 6; page++)
            DbFileTestHelpers.TrashPage(path, page);

        var (result, summary) = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Damaged, result);
        Assert.InRange(summary!.Split('\n').Length, 1, BackupService.IntegritySummaryMaxLines);
    }

    // ── Verdicts from an exception ────────────────────────────────────────────

    /// <summary>
    /// A file that is not SQLite at all is Damaged on SQLite's say-so, and the summary is
    /// <c>SQLite error {code}: {message}</c> with SQLite's own code (26, NOTADB) and no path.
    /// </summary>
    [Fact]
    public void NonSqliteFile_IsDamaged_WithSqliteCodeAndMessage()
    {
        var path = PathOf("notadb.db");
        File.WriteAllText(path, "this is not a sqlite database, just some text that is long enough");

        var (result, summary) = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Damaged, result);
        Assert.StartsWith("SQLite error 26: ", summary);
        Assert.DoesNotContain("SQLite Error", summary);   // the code is stated once, not twice
        Assert.DoesNotContain(_tempDir, summary);
    }

    /// <summary>
    /// A missing file cannot be opened read-only (SQLITE_CANTOPEN): Unreadable — never damaged — with a summary of
    /// that SQLite error and no path.
    /// </summary>
    [Fact]
    public void MissingFile_IsUnreadable_WithSqliteCodeAndMessage()
    {
        var path = PathOf("nothing-here.db");

        var (result, summary) = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Unreadable, result);
        Assert.StartsWith("SQLite error 14: ", summary);
        Assert.DoesNotContain(_tempDir, summary);
    }

    /// <summary>
    /// A healthy file held exclusively by another program (a real sharing violation) is Unreadable, with a summary
    /// that says nothing of a path, and its bytes are untouched.
    /// </summary>
    [FactWindowsOnly]
    public void HealthyFileHeldExclusively_IsUnreadable_WithAPathFreeSummary()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var hashBefore = DbFileTestHelpers.HashOf(path);

        (IntegrityCheckResult Result, string? Summary) check;
        using (DbFileTestHelpers.LockExclusively(path))
            check = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Unreadable, check.Result);
        Assert.False(string.IsNullOrWhiteSpace(check.Summary));
        Assert.DoesNotContain(_tempDir, check.Summary);
        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(path));
    }

    /// <summary>
    /// The summary of a non-SQLite exception is its type name only: an I/O exception's own message names the file path,
    /// which must never travel in a report.
    /// </summary>
    [Fact]
    public void SummarizeIntegrityException_NonSqlite_IsTheTypeNameOnly()
    {
        var ex = new IOException(@"The process cannot access the file 'C:\secret\folder\db.db' because it is being used.");

        var summary = BackupService.SummarizeIntegrityException(ex);

        Assert.Contains(nameof(IOException), summary);
        Assert.DoesNotContain("secret", summary);
        Assert.DoesNotContain("C:", summary);
    }

    // ── CheckIntegrityDetailed is unchanged ───────────────────────────────────

    /// <summary>
    /// <see cref="BackupService.CheckIntegrityDetailed"/> and <see cref="BackupService.CheckIntegrity"/> give the same
    /// verdict as before — and as the summary variant — for every kind of file.
    /// </summary>
    [Fact]
    public void CheckIntegrityDetailed_StillGivesTheSameVerdicts()
    {
        var healthy = PathOf("healthy.db");
        var damaged = PathOf("damaged.db");
        var notDb   = PathOf("notadb.db");
        var missing = PathOf("missing.db");
        DbFileTestHelpers.CreateHealthyDatabase(healthy);
        DbFileTestHelpers.CreateHealthyDatabase(damaged);
        DbFileTestHelpers.TrashPage(damaged, 2);
        File.WriteAllText(notDb, "not a database, but long enough to have some bytes in it");

        foreach (var (path, expected) in new[]
        {
            (healthy, IntegrityCheckResult.Ok),
            (damaged, IntegrityCheckResult.Damaged),
            (notDb,   IntegrityCheckResult.Damaged),
            (missing, IntegrityCheckResult.Unreadable),
        })
        {
            Assert.Equal(expected, BackupService.CheckIntegrityDetailed(path));
            Assert.Equal(expected, BackupService.CheckIntegrityWithSummary(path).Result);
            Assert.Equal(expected == IntegrityCheckResult.Ok, BackupService.CheckIntegrity(path));
        }
    }
}
