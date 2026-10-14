using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="BackupService.CheckIntegrityWithSummary"/> (spec item 5, step 5d): the same verdicts as
/// <see cref="BackupService.CheckIntegrityDetailed"/> — which now delegates to it and must behave exactly as before
/// — plus a short SQLite summary of why a file did not pass. The summary names pages and tables, never data, and
/// never a path; it is what the damage report of step 5e carries.
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

        var (result, summary, _) = BackupService.CheckIntegrityWithSummary(path);

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

        var (result, summary, _) = BackupService.CheckIntegrityWithSummary(path);

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

        var (result, summary, _) = BackupService.CheckIntegrityWithSummary(path);

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

        var (result, summary, _) = BackupService.CheckIntegrityWithSummary(path);

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

        var (result, summary, _) = BackupService.CheckIntegrityWithSummary(path);

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

        IntegrityCheckOutcome check;
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

    // ── Damage evidence (spec item 5, step 5e) ────────────────────────────────

    /// <summary>
    /// Which verdicts carry which evidence, for real files: Ok and Unreadable carry none; integrity_check problems
    /// are IntegrityProblems; a trashed page or a file cut short after its header is damage SQLite itself recognises
    /// (Corrupt or IntegrityProblems — both reportable); a file that is not SQLite at all is NotADatabase (Damaged,
    /// but not reportable).
    /// </summary>
    [Fact]
    public void Evidence_FollowsTheKindOfFile()
    {
        var healthy   = PathOf("healthy.db");
        var trashed   = PathOf("trashed.db");
        var withProblems = PathOf("problems.db");
        var notDb     = PathOf("notadb.db");
        var shortText = PathOf("short.db");
        var empty     = PathOf("empty.db");
        var truncated = PathOf("truncated.db");
        var missing   = PathOf("missing.db");
        DbFileTestHelpers.CreateHealthyDatabase(healthy);
        DbFileTestHelpers.CreateHealthyDatabase(trashed);
        DbFileTestHelpers.TrashPage(trashed, 2);
        DbFileTestHelpers.CreateDatabaseWithIntegrityProblems(withProblems);
        File.WriteAllText(notDb, "this is not a sqlite database, just some text that is long enough");
        File.WriteAllText(shortText, "hello");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        File.WriteAllBytes(truncated, File.ReadAllBytes(healthy)[..512]);

        Assert.Equal(DamageEvidence.None,              BackupService.CheckIntegrityWithSummary(healthy).Evidence);
        var trashedCheck = BackupService.CheckIntegrityWithSummary(trashed);   // SQLite refuses it (CORRUPT) or integrity_check lists problems
        Assert.Equal(IntegrityCheckResult.Damaged, trashedCheck.Result);
        Assert.True(DatabaseDamageReporter.IsReportable(trashedCheck.Evidence), $"evidence was {trashedCheck.Evidence}");
        Assert.Equal(DamageEvidence.IntegrityProblems, BackupService.CheckIntegrityWithSummary(withProblems).Evidence);
        Assert.Equal(DamageEvidence.NotADatabase,      BackupService.CheckIntegrityWithSummary(notDb).Evidence);
        Assert.Equal(DamageEvidence.NotADatabase,      BackupService.CheckIntegrityWithSummary(shortText).Evidence);
        Assert.Equal(DamageEvidence.None,              BackupService.CheckIntegrityWithSummary(empty).Evidence);   // SQLite: a valid empty database
        Assert.Equal(DamageEvidence.None,              BackupService.CheckIntegrityWithSummary(missing).Evidence);

        var truncatedCheck = BackupService.CheckIntegrityWithSummary(truncated);
        Assert.Equal(IntegrityCheckResult.Damaged, truncatedCheck.Result);
        Assert.True(DatabaseDamageReporter.IsReportable(truncatedCheck.Evidence), $"evidence was {truncatedCheck.Evidence}");
    }

    /// <summary>A file held exclusively by another program is Unreadable and carries no evidence.</summary>
    [FactWindowsOnly]
    public void Evidence_OfAFileHeldExclusively_IsNone()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);

        IntegrityCheckOutcome check;
        using (DbFileTestHelpers.LockExclusively(path))
            check = BackupService.CheckIntegrityWithSummary(path);

        Assert.Equal(IntegrityCheckResult.Unreadable, check.Result);
        Assert.Equal(DamageEvidence.None, check.Evidence);
    }

    /// <summary>
    /// The exception classification: SQLITE_CORRUPT (11, and its extended codes) is Corrupt; SQLITE_NOTADB (26) and
    /// SQLITE_FORMAT (24) are NotADatabase; every other code, and every non-SQLite exception, is None.
    /// </summary>
    [Theory]
    [InlineData(11,  DamageEvidence.Corrupt)]
    [InlineData(267, DamageEvidence.Corrupt)]        // SQLITE_CORRUPT_VTAB (11 | 1<<8)
    [InlineData(523, DamageEvidence.Corrupt)]        // SQLITE_CORRUPT_SEQUENCE (11 | 2<<8)
    [InlineData(26,  DamageEvidence.NotADatabase)]
    [InlineData(24,  DamageEvidence.NotADatabase)]
    [InlineData(14,  DamageEvidence.None)]           // SQLITE_CANTOPEN
    [InlineData(5,   DamageEvidence.None)]           // SQLITE_BUSY
    [InlineData(10,  DamageEvidence.None)]           // SQLITE_IOERR
    [InlineData(1,   DamageEvidence.None)]           // SQLITE_ERROR
    public void ClassifyDamageEvidence_ByCode(int code, DamageEvidence expected)
    {
        Assert.Equal(expected, BackupService.ClassifyDamageEvidence(new SqliteException("x", code)));
    }

    /// <summary>Non-SQLite exceptions carry no evidence of damage.</summary>
    [Fact]
    public void ClassifyDamageEvidence_NonSqlite_IsNone()
    {
        Assert.Equal(DamageEvidence.None, BackupService.ClassifyDamageEvidence(new IOException("sharing violation")));
        Assert.Equal(DamageEvidence.None, BackupService.ClassifyDamageEvidence(new UnauthorizedAccessException()));
        Assert.Equal(DamageEvidence.None, BackupService.ClassifyDamageEvidence(new InvalidOperationException("odd")));
    }

    /// <summary>
    /// Evidence and verdict agree: every exception that classifies as Damaged carries evidence (and vice versa),
    /// so the report can never be about a file the app did not call damaged.
    /// </summary>
    [Fact]
    public void ClassifyDamageEvidence_AgreesWithTheVerdict()
    {
        for (var code = 0; code < 1000; code++)
        {
            var ex = new SqliteException("x", code);
            var damaged = BackupService.ClassifyIntegrityException(ex) == IntegrityCheckResult.Damaged;
            var hasEvidence = BackupService.ClassifyDamageEvidence(ex) != DamageEvidence.None;
            Assert.Equal(damaged, hasEvidence);
        }
    }

    /// <summary>Only integrity problems and SQLITE_CORRUPT are reportable; "not a database" and "no evidence" are not.</summary>
    [Theory]
    [InlineData(DamageEvidence.IntegrityProblems, true)]
    [InlineData(DamageEvidence.Corrupt,           true)]
    [InlineData(DamageEvidence.NotADatabase,      false)]
    [InlineData(DamageEvidence.None,              false)]
    public void IsReportable_OnlyForPositiveEvidenceInsideADatabase(DamageEvidence evidence, bool expected)
    {
        Assert.Equal(expected, DatabaseDamageReporter.IsReportable(evidence));
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
