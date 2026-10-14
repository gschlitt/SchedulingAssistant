using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests that every place a damaged database is detected reports it (spec item 5, step 5e) — and only when the
/// evidence is positive damage inside a SQLite database, never for fake (DEBUG-injected) damage, and never with a
/// path in anything sent. The detection points covered here: the front-door check
/// (<see cref="DatabaseValidator.ValidateAsync"/>), the open-time check of the local copy
/// (<see cref="LocalCopyIntegrity"/>) and its re-check of the shared database, and the backup snapshot
/// (<see cref="BackupService"/>). The restore routine's backup check is covered in <c>DatabaseRestoreTests</c>.
/// Real SQLite files in awkward states are built in a temp folder (<see cref="DbFileTestHelpers"/>).
/// </summary>
public sealed class DatabaseDamageWiringTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly RecordingAppLogger _log = new();

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public DatabaseDamageWiringTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"damage_wiring_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Full path of <paramref name="name"/> inside the temp directory.</summary>
    private string PathOf(string name) => Path.Combine(_tempDir, name);

    /// <summary>A multi-page database whose page 2 is trashed: <c>integrity_check</c> reports problems.</summary>
    private string DamagedDb(string name = "damaged.db")
    {
        var path = PathOf(name);
        DbFileTestHelpers.CreateHealthyDatabase(path);
        DbFileTestHelpers.TrashPage(path, pageNumber: 2);
        return path;
    }

    /// <summary>A healthy multi-page database.</summary>
    private string HealthyDb(string name = "healthy.db")
    {
        var path = PathOf(name);
        DbFileTestHelpers.CreateHealthyDatabase(path);
        return path;
    }

    /// <summary>A text file, which is not a SQLite database at all.</summary>
    private string TextFile(string name = "notes.db")
    {
        var path = PathOf(name);
        File.WriteAllText(path, "this is not a sqlite database, just some text that is long enough");
        return path;
    }

    /// <summary>Asserts nothing that was sent mentions the temp folder, a drive path, or a database file name.</summary>
    private void AssertNothingPrivateWasSent()
    {
        foreach (var report in _log.Handled)
        {
            var texts = new List<string> { report.Context, report.Exception.Message, report.MetadataTab, report.GroupingKey ?? "" };
            texts.AddRange(report.Metadata!.Values);
            foreach (var text in texts)
            {
                Assert.DoesNotContain(_tempDir, text);
                Assert.DoesNotContain(Path.GetFileName(_tempDir), text);
                Assert.DoesNotContain(@":\", text);
                Assert.DoesNotContain(".db", text);
            }
        }
    }

    // ── DatabaseValidator.ValidateAsync (the front-door check) ────────────────

    /// <summary>
    /// A database with problems inside is Corrupt as before, and is reported once as a damaged SHARED database, with
    /// SQLite's summary and no path.
    /// </summary>
    [Fact]
    public async Task Validate_DamagedDatabase_ReportsSharedDatabase()
    {
        var result = await DatabaseValidator.ValidateAsync(DamagedDb(), logger: _log);

        Assert.Equal(DatabaseValidationResult.Corrupt, result);
        var report = Assert.Single(_log.Handled);
        Assert.Equal("SharedDatabase", report.Metadata!["kind"]);
        Assert.Equal("Database damage: SharedDatabase", report.Context);
        Assert.False(string.IsNullOrWhiteSpace(report.Metadata["summary"]));
        AssertNothingPrivateWasSent();
    }

    /// <summary>
    /// The other kind of reportable evidence: a database SQLite opens normally but whose <c>integrity_check</c> answers
    /// with problems. Reported the same way, with SQLite's own line as the summary.
    /// </summary>
    [Fact]
    public async Task Validate_DatabaseWithIntegrityCheckProblems_ReportsSharedDatabase()
    {
        var path = PathOf("problems.db");
        DbFileTestHelpers.CreateDatabaseWithIntegrityProblems(path);

        var result = await DatabaseValidator.ValidateAsync(path, logger: _log);

        Assert.Equal(DatabaseValidationResult.Corrupt, result);
        var report = Assert.Single(_log.Handled);
        Assert.Equal("SharedDatabase", report.Metadata!["kind"]);
        Assert.Contains("idx_sections_code", report.Metadata["summary"]);
        AssertNothingPrivateWasSent();
    }

    /// <summary>The chooser's check of a BACKUP the user picked reports it as a damaged backup being restored.</summary>
    [Fact]
    public async Task Validate_DamagedDatabase_WithReportAsBackup_ReportsBackupBeingRestored()
    {
        var result = await DatabaseValidator.ValidateAsync(DamagedDb(), DamagedFileKind.BackupBeingRestored, _log);

        Assert.Equal(DatabaseValidationResult.Corrupt, result);
        Assert.Equal("BackupBeingRestored", Assert.Single(_log.Handled).Metadata!["kind"]);
    }

    /// <summary>
    /// A file that is not a SQLite database at all (the usual wrong-file pick) is still Corrupt as before, but is NOT
    /// reported: it is far more likely the wrong file than damage.
    /// </summary>
    [Fact]
    public async Task Validate_TextFile_IsCorrupt_ButNotReported()
    {
        var result = await DatabaseValidator.ValidateAsync(TextFile(), logger: _log);

        Assert.Equal(DatabaseValidationResult.Corrupt, result);
        Assert.Empty(_log.Handled);
    }

    /// <summary>A healthy database and a missing file report nothing.</summary>
    [Fact]
    public async Task Validate_HealthyAndMissing_ReportNothing()
    {
        Assert.Equal(DatabaseValidationResult.Ok, await DatabaseValidator.ValidateAsync(HealthyDb(), logger: _log));
        Assert.Equal(DatabaseValidationResult.Missing, await DatabaseValidator.ValidateAsync(PathOf("nothing-here.db"), logger: _log));
        Assert.Equal(DatabaseValidationResult.Missing, await DatabaseValidator.ValidateAsync(null, logger: _log));

        Assert.Empty(_log.Handled);
    }

    /// <summary>A healthy file another program holds open is Unreadable (as before) and is never reported as damage.</summary>
    [FactWindowsOnly]
    public async Task Validate_LockedFile_IsUnreadable_AndNotReported()
    {
        var path = HealthyDb();
        DatabaseValidationResult result;
        using (DbFileTestHelpers.LockExclusively(path))
            result = await DatabaseValidator.ValidateAsync(path, logger: _log);

        Assert.Equal(DatabaseValidationResult.Unreadable, result);
        Assert.Empty(_log.Handled);
    }

    /// <summary>Checking the same damaged database again (the chooser does, on every selection) sends one report in all.</summary>
    [Fact]
    public async Task Validate_SameDamagedDatabaseTwice_ReportsOnce()
    {
        var path = DamagedDb();

        await DatabaseValidator.ValidateAsync(path, logger: _log);
        await DatabaseValidator.ValidateAsync(path, logger: _log);

        Assert.Single(_log.Handled);
    }

    // ── LocalCopyIntegrity.VerifyAtOpen (the open-time check of the local copy) ─

    /// <summary>The scripted checker: always answers with <paramref name="answer"/>.</summary>
    private static Func<string, LocalCopyCheck> Always(LocalCopyCheck answer) => _ => answer;

    /// <summary>Runs <see cref="LocalCopyIntegrity.VerifyAtOpen"/> with no waiting.</summary>
    private void Verify(string path, Func<string, LocalCopyCheck> check)
        => LocalCopyIntegrity.VerifyAtOpen(path, check, _log, sleep: _ => { });

    /// <summary>A copy with positive evidence of damage throws as before and is reported once as a damaged LOCAL COPY.</summary>
    [Theory]
    [InlineData(DamageEvidence.IntegrityProblems)]
    [InlineData(DamageEvidence.Corrupt)]
    public void VerifyAtOpen_Damaged_ReportsLocalCopy_AndStillThrows(DamageEvidence evidence)
    {
        var path = PathOf("local.db");

        var ex = Assert.Throws<DatabaseCorruptException>(() =>
            Verify(path, Always(new LocalCopyCheck(true, IntegrityCheckResult.Damaged, "Page 2: btree problem", evidence))));

        Assert.Equal("Page 2: btree problem", ex.Summary);
        var report = Assert.Single(_log.Handled);
        Assert.Equal("LocalCopy", report.Metadata!["kind"]);
        Assert.Equal("Page 2: btree problem", report.Metadata["summary"]);
        AssertNothingPrivateWasSent();
    }

    /// <summary>
    /// Damaged-but-not-a-database, and a damaged verdict with no evidence, still throw but are not reported.
    /// </summary>
    [Theory]
    [InlineData(DamageEvidence.NotADatabase)]
    [InlineData(DamageEvidence.None)]
    public void VerifyAtOpen_DamagedWithoutReportableEvidence_ThrowsButIsNotReported(DamageEvidence evidence)
    {
        Assert.Throws<DatabaseCorruptException>(() =>
            Verify(PathOf("local.db"), Always(new LocalCopyCheck(true, IntegrityCheckResult.Damaged, "SQLite error 26: x", evidence))));

        Assert.Empty(_log.Handled);
    }

    /// <summary>A copy that stays locked is reported as in use and never as damage.</summary>
    [Fact]
    public void VerifyAtOpen_Locked_IsNotReported()
    {
        Assert.Throws<DatabaseInUseException>(() =>
            Verify(PathOf("local.db"), Always(new LocalCopyCheck(true, IntegrityCheckResult.Unreadable, "SQLite error 14"))));

        Assert.Empty(_log.Handled);
    }

    /// <summary>
    /// Damage made up by the DEBUG fault plan is acted on like real damage (it throws) but is never sent: the log says
    /// the report was suppressed.
    /// </summary>
    [Fact]
    public void VerifyAtOpen_InjectedDamage_IsNotReported_AndTheLogSaysSo()
    {
        Assert.Throws<DatabaseCorruptException>(() =>
            Verify(PathOf("local.db"), Always(new LocalCopyCheck(
                true, IntegrityCheckResult.Damaged, "DEBUG fault injection: damaged", DamageEvidence.IntegrityProblems, Injected: true))));

        Assert.Empty(_log.Handled);
        Assert.Contains(_log.Infos, m => m.Contains("injected by a DEBUG fault plan") && m.Contains("no report is sent"));
    }

    /// <summary>
    /// End to end on a real file: <see cref="LocalCopyIntegrity.CheckOnce"/> on a database with a trashed page yields
    /// reportable evidence, and the open-time gate reports it (no path).
    /// </summary>
    [Fact]
    public void CheckOnce_ThenVerifyAtOpen_OnARealDamagedFile_ReportsLocalCopy()
    {
        var path = DamagedDb("local.db");

        var check = LocalCopyIntegrity.CheckOnce(path, faults: null, _log);
        Assert.Equal(IntegrityCheckResult.Damaged, check.Result);
        Assert.True(DatabaseDamageReporter.IsReportable(check.Evidence), $"evidence was {check.Evidence}");
        Assert.False(check.Injected);

        Assert.Throws<DatabaseCorruptException>(() => Verify(path, Always(check)));

        Assert.Equal("LocalCopy", Assert.Single(_log.Handled).Metadata!["kind"]);
        AssertNothingPrivateWasSent();
    }

    /// <summary>
    /// The DEBUG fault plan's <c>damaged</c> and <c>shared</c> faults make <see cref="LocalCopyIntegrity.CheckOnce"/>
    /// report damage marked as injected, even for a healthy file — and the gate then reports nothing to BugSnag.
    /// </summary>
    [Theory]
    [InlineData("damaged")]
    [InlineData("shared")]
    public void CheckOnce_WithInjectedDamage_IsMarkedInjected_AndNeverReported(string spec)
    {
        var path = HealthyDb("local.db");
        var plan = IntegrityFaultPlan.Parse(spec);

        var check = LocalCopyIntegrity.CheckOnce(path, plan, _log);

        Assert.Equal(IntegrityCheckResult.Damaged, check.Result);
        Assert.True(check.Injected);

        Assert.Throws<DatabaseCorruptException>(() => Verify(path, Always(check)));
        Assert.Empty(_log.Handled);
    }

    /// <summary>The injected <c>locked</c> fault is marked injected too (and, being a lock, is never a damage report).</summary>
    [Fact]
    public void CheckOnce_WithInjectedLock_IsMarkedInjected()
    {
        var check = LocalCopyIntegrity.CheckOnce(HealthyDb("local.db"), IntegrityFaultPlan.Parse("locked"), _log);

        Assert.Equal(IntegrityCheckResult.Unreadable, check.Result);
        Assert.True(check.Injected);
    }

    // ── LocalCopyIntegrity.RecheckSharedDatabaseAsync ─────────────────────────

    /// <summary>The re-check of a really damaged shared database reports it as a damaged SHARED database.</summary>
    [Fact]
    public async Task RecheckShared_RealDamage_ReportsSharedDatabase()
    {
        var result = await LocalCopyIntegrity.RecheckSharedDatabaseAsync(DamagedDb(), faults: null, _log);

        Assert.Equal(DatabaseValidationResult.Corrupt, result);
        Assert.Equal("SharedDatabase", Assert.Single(_log.Handled).Metadata!["kind"]);
    }

    /// <summary>The DEBUG <c>shared</c> fault makes the re-check say "damaged" without checking anything — and reports nothing.</summary>
    [Fact]
    public async Task RecheckShared_InjectedFault_ReportsNothing()
    {
        var result = await LocalCopyIntegrity.RecheckSharedDatabaseAsync(
            HealthyDb(), IntegrityFaultPlan.Parse("shared"), _log);

        Assert.Equal(DatabaseValidationResult.Corrupt, result);
        Assert.Empty(_log.Handled);
    }

    // ── BackupService: the backup snapshot ────────────────────────────────────

    /// <summary>A BackupService that only reports (no database or repositories are touched).</summary>
    private BackupService NewBackupService()
        => new(new Moq.Mock<TermPoint.Data.IDatabaseContext>().Object,
               null!, null!, null!, null!, null!, null!, null!, null!, _log);

    /// <summary>
    /// A snapshot with positive evidence of damage: reported once as a BACKUP SNAPSHOT (no path anywhere), the user is
    /// told with the exact notice text through <see cref="IAppLogger.NotifyUser"/>, and — the old behaviour this
    /// replaces — <c>LogError</c> is never called (it sent a report with no exception and a local path).
    /// </summary>
    [Fact]
    public void Snapshot_Damaged_IsReported_TheUserIsTold_AndLogErrorIsNotUsed()
    {
        NewBackupService().ReportSnapshotFailedIntegrity(
            new IntegrityCheckOutcome(IntegrityCheckResult.Damaged, "Page 2: btree problem", DamageEvidence.IntegrityProblems));

        var report = Assert.Single(_log.Handled);
        Assert.Equal("BackupSnapshot", report.Metadata!["kind"]);
        Assert.Equal("Database damage: BackupSnapshot", report.Context);
        Assert.Equal(new[] { "The latest backup failed its integrity check, so it may not be usable for a restore." }, _log.UserNotices);
        Assert.Equal(BackupService.SnapshotFailedIntegrityNotice, Assert.Single(_log.UserNotices));
        Assert.Empty(_log.Errors);
        Assert.Contains(_log.Warnings, m => m.Contains("backup snapshot did not pass its integrity check"));
        AssertNothingPrivateWasSent();
    }

    /// <summary>
    /// A snapshot that merely could not be read, or is not a database, still tells the user (as before) but is not
    /// reported to BugSnag.
    /// </summary>
    [Theory]
    [InlineData(IntegrityCheckResult.Unreadable, DamageEvidence.None)]
    [InlineData(IntegrityCheckResult.Damaged,    DamageEvidence.NotADatabase)]
    public void Snapshot_WithoutReportableEvidence_TellsTheUser_ButIsNotReported(IntegrityCheckResult result, DamageEvidence evidence)
    {
        NewBackupService().ReportSnapshotFailedIntegrity(new IntegrityCheckOutcome(result, "SQLite error 14: x", evidence));

        Assert.Empty(_log.Handled);
        Assert.Single(_log.UserNotices);
        Assert.Empty(_log.Errors);
    }

    /// <summary>Every failed snapshot tells the user, but the damage is reported once per run.</summary>
    [Fact]
    public void Snapshot_DamagedTwice_TellsTheUserTwice_AndReportsOnce()
    {
        var service = NewBackupService();
        var damaged = new IntegrityCheckOutcome(IntegrityCheckResult.Damaged, "x", DamageEvidence.Corrupt);

        service.ReportSnapshotFailedIntegrity(damaged);
        service.ReportSnapshotFailedIntegrity(damaged);

        Assert.Single(_log.Handled);
        Assert.Equal(2, _log.UserNotices.Count);
    }

    /// <summary>A logger that fails while reporting cannot turn a backup into an exception.</summary>
    [Fact]
    public void Snapshot_WhenTheLoggerFails_DoesNotThrow()
    {
        _log.ThrowFromReportHandled = true;

        var thrown = Record.Exception(() => NewBackupService().ReportSnapshotFailedIntegrity(
            new IntegrityCheckOutcome(IntegrityCheckResult.Damaged, "x", DamageEvidence.Corrupt)));

        Assert.Null(thrown);
    }
}
