using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="LocalCopyIntegrity"/>, the three-way open-time check of the local copy and the re-check
/// of the shared database (spec item 5, step 5d): Ok opens; a copy another program holds is retried quietly —
/// exactly 3 attempts in all — and then reported as <see cref="DatabaseInUseException"/>; only positive evidence of
/// damage is <see cref="DatabaseCorruptException"/>, carrying SQLite's summary. The scripted tests drive the
/// gate with a fake checker and a recording "sleep", so no time is spent; the file tests use real SQLite files.
/// </summary>
public sealed class LocalCopyIntegrityTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for this test instance.</summary>
    public LocalCopyIntegrityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lci_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Removes the temporary directory (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Full path of <paramref name="name"/> inside the temp directory.</summary>
    private string PathOf(string name = "local.db") => Path.Combine(_tempDir, name);

    /// <summary>A recording logger.</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        /// <summary>Every information line, in order.</summary>
        public List<string> Lines { get; } = new();
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) => Lines.Add(message);
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }

    /// <summary>
    /// A scripted checker: answers with the given results in order (repeating the last), and counts its calls. The
    /// paired "sleep" records every wait instead of waiting.
    /// </summary>
    private sealed class Script
    {
        private readonly Queue<LocalCopyCheck> _answers;
        private LocalCopyCheck _last;

        /// <summary>Builds a script of answers (at least one).</summary>
        /// <param name="answers">The answers, in the order the gate will ask.</param>
        public Script(params LocalCopyCheck[] answers)
        {
            _answers = new Queue<LocalCopyCheck>(answers);
            _last = answers[^1];
        }

        /// <summary>How many times the gate asked.</summary>
        public int Calls { get; private set; }

        /// <summary>The waits the gate asked for.</summary>
        public List<TimeSpan> Sleeps { get; } = new();

        /// <summary>The checker to hand to the gate.</summary>
        public LocalCopyCheck Check(string path)
        {
            Calls++;
            if (_answers.Count > 0) _last = _answers.Dequeue();
            return _last;
        }

        /// <summary>The "sleep" to hand to the gate.</summary>
        public void Sleep(TimeSpan delay) => Sleeps.Add(delay);
    }

    private static LocalCopyCheck Ok() => new(true, IntegrityCheckResult.Ok, null);
    private static LocalCopyCheck Busy(string summary = "SQLite error 14: 'unable to open database file'.")
        => new(true, IntegrityCheckResult.Unreadable, summary);
    private static LocalCopyCheck Damaged(string summary = "*** in database main ***\nPage 2: btree problem")
        => new(true, IntegrityCheckResult.Damaged, summary);
    private static LocalCopyCheck TimedOut() => new(false, default, null);

    // ── The gate: Ok / Locked / Damaged ───────────────────────────────────────

    /// <summary>A copy that passes opens at once: one check, no waiting, nothing thrown.</summary>
    [Fact]
    public void Ok_ReturnsAfterOneCheck_WithoutWaiting()
    {
        var script = new Script(Ok());

        LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep);

        Assert.Equal(1, script.Calls);
        Assert.Empty(script.Sleeps);
    }

    /// <summary>
    /// A copy that stays locked is attempted EXACTLY 3 times, one second apart (2 waits), and then reported as
    /// "in use" — never as damaged — carrying the attempt count and SQLite's account of the last attempt.
    /// </summary>
    [Fact]
    public void AlwaysLocked_IsAttemptedExactlyThreeTimes_ThenThrowsInUse()
    {
        var script = new Script(Busy("SQLite error 14: last"));

        var ex = Assert.Throws<DatabaseInUseException>(() =>
            LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep));

        Assert.Equal(3, script.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1) }, script.Sleeps);
        Assert.Equal(3, ex.Attempts);
        Assert.Equal("SQLite error 14: last", ex.Summary);
        Assert.Equal("x.db", ex.DatabasePath);
        Assert.DoesNotContain("x.db", ex.Message);   // the message is path-free: the wizard shows it inline
    }

    /// <summary>The documented numbers: 3 attempts in all, 1 second apart.</summary>
    [Fact]
    public void TheDocumentedRetryNumbers_AreThreeAttemptsOneSecondApart()
    {
        Assert.Equal(3, LocalCopyIntegrity.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), LocalCopyIntegrity.RetryDelay);
    }

    /// <summary>Locked once, then free: the second attempt passes, one wait happened, and nothing is thrown.</summary>
    [Fact]
    public void LockedThenFree_OpensOnTheSecondAttempt()
    {
        var script = new Script(Busy(), Ok());

        LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep);

        Assert.Equal(2, script.Calls);
        Assert.Single(script.Sleeps);
    }

    /// <summary>Locked twice, free on the third and last attempt: still opens.</summary>
    [Fact]
    public void LockedTwiceThenFree_OpensOnTheThirdAttempt()
    {
        var script = new Script(Busy(), Busy(), Ok());

        LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep);

        Assert.Equal(3, script.Calls);
        Assert.Equal(2, script.Sleeps.Count);
    }

    /// <summary>
    /// Damage is reported at once, with SQLite's summary, and is never retried: a damaged file does not heal by waiting.
    /// </summary>
    [Fact]
    public void Damaged_ThrowsCorruptAtOnce_CarryingTheSummary_WithoutRetrying()
    {
        var script = new Script(Damaged("the summary"));

        var ex = Assert.Throws<DatabaseCorruptException>(() =>
            LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep));

        Assert.Equal(1, script.Calls);
        Assert.Empty(script.Sleeps);
        Assert.Equal("the summary", ex.Summary);
        Assert.Equal("x.db", ex.DatabasePath);
    }

    /// <summary>Locked at first, damaged when finally readable: damage is reported on that attempt, without further waiting.</summary>
    [Fact]
    public void LockedThenDamaged_ThrowsCorruptOnTheSecondAttempt()
    {
        var script = new Script(Busy(), Damaged());

        Assert.Throws<DatabaseCorruptException>(() =>
            LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep));

        Assert.Equal(2, script.Calls);
        Assert.Single(script.Sleeps);
    }

    /// <summary>
    /// A check that did not finish within its deadline (an unreachable location) is SKIPPED, as it always was: the
    /// checkout has its own handling for that and a verdict on an unreadable file means nothing. Nothing is thrown.
    /// </summary>
    [Fact]
    public void TimedOut_IsSkipped_NothingThrown()
    {
        var script = new Script(TimedOut());
        var log = new RecordingLogger();

        LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, log, script.Sleep);

        Assert.Equal(1, script.Calls);
        Assert.Contains(log.Lines, l => l.Contains("timed out"));
    }

    /// <summary>A check that times out during a retry also ends the gate quietly (the location went away meanwhile).</summary>
    [Fact]
    public void LockedThenTimedOut_IsSkipped()
    {
        var script = new Script(Busy(), TimedOut());

        LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep);

        Assert.Equal(2, script.Calls);
    }

    /// <summary>The attempt count and the wait are parameters, so a caller (or a test) can change them.</summary>
    [Fact]
    public void AttemptsAndDelay_AreConfigurable()
    {
        var script = new Script(Busy());

        var ex = Assert.Throws<DatabaseInUseException>(() =>
            LocalCopyIntegrity.VerifyAtOpen("x.db", script.Check, new RecordingLogger(), script.Sleep,
                maxAttempts: 2, retryDelay: TimeSpan.FromMilliseconds(5)));

        Assert.Equal(2, script.Calls);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(5) }, script.Sleeps);
        Assert.Equal(2, ex.Attempts);
    }

    // ── The gate over real files ──────────────────────────────────────────────

    /// <summary>A healthy database passes the real check, with the production checker and no fault plan.</summary>
    [Fact]
    public void RealHealthyFile_Passes()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var script = new Script(Ok());   // only for its Sleep recorder

        LocalCopyIntegrity.VerifyAtOpen(
            path, p => LocalCopyIntegrity.CheckOnce(p, null, new RecordingLogger()), new RecordingLogger(), script.Sleep);

        Assert.Empty(script.Sleeps);
    }

    /// <summary>
    /// A real database with a trashed page is Damaged: the gate throws <see cref="DatabaseCorruptException"/> at
    /// once, with a non-empty summary of at most 5 lines.
    /// </summary>
    [Fact]
    public void RealDamagedFile_ThrowsCorrupt_WithABoundedSummary()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        DbFileTestHelpers.TrashPage(path, 2);
        var script = new Script(Ok());

        var ex = Assert.Throws<DatabaseCorruptException>(() =>
            LocalCopyIntegrity.VerifyAtOpen(
                path, p => LocalCopyIntegrity.CheckOnce(p, null, new RecordingLogger()), new RecordingLogger(), script.Sleep));

        Assert.False(string.IsNullOrWhiteSpace(ex.Summary));
        Assert.InRange(ex.Summary!.Split('\n').Length, 1, BackupService.IntegritySummaryMaxLines);
        Assert.Empty(script.Sleeps);   // damage is never retried
    }

    /// <summary>A file that is not a SQLite database at all is Damaged, with SQLite's own code in the summary.</summary>
    [Fact]
    public void RealNonSqliteFile_ThrowsCorrupt_WithSqliteCodeInTheSummary()
    {
        var path = PathOf("notadb.db");
        File.WriteAllText(path, new string('x', 4096));
        var script = new Script(Ok());

        var ex = Assert.Throws<DatabaseCorruptException>(() =>
            LocalCopyIntegrity.VerifyAtOpen(
                path, p => LocalCopyIntegrity.CheckOnce(p, null, new RecordingLogger()), new RecordingLogger(), script.Sleep));

        Assert.StartsWith("SQLite error 26:", ex.Summary);
        Assert.DoesNotContain(_tempDir, ex.Summary);   // no path in the summary
    }

    /// <summary>
    /// A healthy file that another program holds exclusively (a real sharing violation) is attempted exactly 3
    /// times and reported as "in use" — NEVER as damaged — and the file's bytes are untouched.
    /// </summary>
    [FactWindowsOnly]
    public void RealHealthyFileHeldExclusively_ThreeAttempts_ThenInUse_NotDamaged()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var hashBefore = DbFileTestHelpers.HashOf(path);
        var attempts = 0;
        var script = new Script(Ok());

        using (DbFileTestHelpers.LockExclusively(path))
        {
            var ex = Assert.Throws<DatabaseInUseException>(() =>
                LocalCopyIntegrity.VerifyAtOpen(
                    path,
                    p => { attempts++; return LocalCopyIntegrity.CheckOnce(p, null, new RecordingLogger()); },
                    new RecordingLogger(), script.Sleep));

            Assert.Equal(3, attempts);
            Assert.Equal(2, script.Sleeps.Count);
            Assert.False(string.IsNullOrWhiteSpace(ex.Summary));
        }

        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(path));
    }

    /// <summary>
    /// A file held for the first attempt only (antivirus finishing its scan) opens on the second attempt: the
    /// "sleep" lets go of the lock, exactly as time would.
    /// </summary>
    [FactWindowsOnly]
    public void RealHealthyFileHeldThenReleased_OpensOnTheSecondAttempt()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var attempts = 0;
        var holder = DbFileTestHelpers.LockExclusively(path);
        try
        {
            LocalCopyIntegrity.VerifyAtOpen(
                path,
                p => { attempts++; return LocalCopyIntegrity.CheckOnce(p, null, new RecordingLogger()); },
                new RecordingLogger(),
                sleep: _ => holder.Dispose());   // the scan ends while we wait
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal(2, attempts);
    }

    // ── The DEBUG fault plan, as wired into the checks ────────────────────────

    /// <summary>
    /// <c>locked:3</c>: one whole open's worth of attempts (all three) report the file as unreadable, so the gate
    /// throws "in use" — over a perfectly healthy file — and the NEXT open ("Try again") finds the plan spent and
    /// passes.
    /// </summary>
    [Fact]
    public void FaultPlan_LockedThree_FailsOneWholeOpen_ThenTheNextOpenSucceeds()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var plan = IntegrityFaultPlan.Parse("locked:3");
        var log = new RecordingLogger();
        var script = new Script(Ok());

        Assert.Throws<DatabaseInUseException>(() =>
            LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep));
        Assert.Equal(0, plan!.Remaining);
        Assert.Contains(log.Lines, l => l.Contains("TERMPOINT_DEBUG_FAIL_INTEGRITY") && l.Contains("locked"));

        // "Try again": the plan is spent, so the real check runs and the healthy file passes.
        LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep);
    }

    /// <summary>
    /// <c>damaged:1</c>: the first open-time check reports damage over a healthy file; the second (the open after a
    /// fresh copy) passes.
    /// </summary>
    [Fact]
    public void FaultPlan_DamagedOne_FailsTheFirstCheck_ThenThePlanIsSpent()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var plan = IntegrityFaultPlan.Parse("damaged:1");
        var log = new RecordingLogger();
        var script = new Script(Ok());

        var ex = Assert.Throws<DatabaseCorruptException>(() =>
            LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep));
        Assert.False(string.IsNullOrWhiteSpace(ex.Summary));

        LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep);
    }

    /// <summary><c>damaged:2</c>: the first open AND the open after the fresh copy both report damage (-> "keeps failing"); a third passes.</summary>
    [Fact]
    public void FaultPlan_DamagedTwo_FailsTwoOpens_ThenThePlanIsSpent()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var plan = IntegrityFaultPlan.Parse("damaged:2");
        var log = new RecordingLogger();
        var script = new Script(Ok());

        for (var i = 0; i < 2; i++)
            Assert.Throws<DatabaseCorruptException>(() =>
                LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep));

        LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep);
    }

    /// <summary>
    /// <c>shared</c> is usable on its own: the handler's re-check is only reached when the local copy fails, so a
    /// <c>shared</c> plan makes the local copy's check report damage too — WITHOUT spending its budget — until the
    /// re-check has spent it. After that the local copy's check is the real one again (the open the user picks from the
    /// recovery chooser passes).
    /// </summary>
    [Fact]
    public async Task FaultPlan_SharedOne_MakesTheLocalCheckFail_UntilTheRecheckHasSpentTheBudget()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var plan = IntegrityFaultPlan.Parse("shared:1")!;
        var log = new RecordingLogger();
        var script = new Script(Ok());

        // The open: the local copy "is damaged" — and asking again (a retry) does not change that, nothing was spent.
        for (var i = 0; i < 3; i++)
            Assert.Throws<DatabaseCorruptException>(() =>
                LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep));
        Assert.Equal(1, plan.Remaining);

        // The handler re-checks the shared database: reported damaged, budget spent.
        Assert.Equal(DatabaseValidationResult.Corrupt, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(path, plan, log));
        Assert.Equal(0, plan.Remaining);

        // The open the user picks from the recovery chooser: the real check, and the healthy file passes.
        LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep);
    }

    /// <summary>An unlimited <c>shared</c> plan keeps the local copy "damaged" and the shared database "damaged" for good.</summary>
    [Fact]
    public async Task FaultPlan_SharedUnlimited_KeepsBothDamaged()
    {
        var path = PathOf();
        DbFileTestHelpers.CreateHealthyDatabase(path);
        var plan = IntegrityFaultPlan.Parse("shared")!;
        var log = new RecordingLogger();
        var script = new Script(Ok());

        for (var i = 0; i < 3; i++)
        {
            Assert.Throws<DatabaseCorruptException>(() =>
                LocalCopyIntegrity.VerifyAtOpen(path, p => LocalCopyIntegrity.CheckOnce(p, plan, log), log, script.Sleep));
            Assert.Equal(DatabaseValidationResult.Corrupt, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(path, plan, log));
        }
    }

    /// <summary><see cref="IntegrityFaultPlan.HasFailuresLeft"/> only peeks: asking spends nothing, and it turns false once the budget is used up (an unlimited plan never runs out).</summary>
    [Fact]
    public void FaultPlan_HasFailuresLeft_IsAReadOnlyPeek()
    {
        var plan = IntegrityFaultPlan.Parse("shared:1")!;

        Assert.True(plan.HasFailuresLeft);
        Assert.True(plan.HasFailuresLeft);   // asking spends nothing
        Assert.Equal(1, plan.Remaining);

        Assert.True(plan.TryConsume(InjectedIntegrityFault.Shared));
        Assert.False(plan.HasFailuresLeft);
        Assert.True(IntegrityFaultPlan.Parse("locked")!.HasFailuresLeft);   // unlimited
    }

    // ── The re-check of the shared database ───────────────────────────────────

    /// <summary>With no plan, the re-check is the real front-door check: a healthy file is Ok, a damaged one Corrupt, a missing one Missing.</summary>
    [Fact]
    public async Task Recheck_WithoutAPlan_IsTheRealFrontDoorCheck()
    {
        var healthy = PathOf("healthy.db");
        var damaged = PathOf("damaged.db");
        DbFileTestHelpers.CreateHealthyDatabase(healthy);
        DbFileTestHelpers.CreateHealthyDatabase(damaged);
        DbFileTestHelpers.TrashPage(damaged, 2);
        var log = new RecordingLogger();

        Assert.Equal(DatabaseValidationResult.Ok, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(healthy, null, log));
        Assert.Equal(DatabaseValidationResult.Corrupt, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(damaged, null, log));
        Assert.Equal(DatabaseValidationResult.Missing, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(PathOf("nope.db"), null, log));
    }

    /// <summary>
    /// <c>shared</c>: the re-check reports damage over a perfectly healthy shared database (so the local copy is not
    /// "healed" but the user is sent to the recovery chooser) — once per budgeted call — and the healthy file is not
    /// modified.
    /// </summary>
    [Fact]
    public async Task Recheck_SharedFault_ReportsDamage_ThenTheRealCheckResumes()
    {
        var healthy = PathOf("healthy.db");
        DbFileTestHelpers.CreateHealthyDatabase(healthy);
        var hash = DbFileTestHelpers.HashOf(healthy);
        var plan = IntegrityFaultPlan.Parse("shared:1");
        var log = new RecordingLogger();

        Assert.Equal(DatabaseValidationResult.Corrupt, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(healthy, plan, log));
        Assert.Contains(log.Lines, l => l.Contains("TERMPOINT_DEBUG_FAIL_INTEGRITY") && l.Contains("shared"));
        Assert.Equal(DatabaseValidationResult.Ok, await LocalCopyIntegrity.RecheckSharedDatabaseAsync(healthy, plan, log));
        Assert.Equal(hash, DbFileTestHelpers.HashOf(healthy));
    }

    /// <summary>The local-copy faults do not touch the shared re-check.</summary>
    [Fact]
    public async Task Recheck_LocalCopyFaults_DoNotAffectTheSharedRecheck()
    {
        var healthy = PathOf("healthy.db");
        DbFileTestHelpers.CreateHealthyDatabase(healthy);

        foreach (var spec in new[] { "locked", "damaged" })
            Assert.Equal(DatabaseValidationResult.Ok,
                await LocalCopyIntegrity.RecheckSharedDatabaseAsync(healthy, IntegrityFaultPlan.Parse(spec), new RecordingLogger()));
    }

    /// <summary>Every validator result maps to the decision table's input; an unknown one maps to the retryable "unreadable", never to "damaged".</summary>
    [Theory]
    [InlineData(DatabaseValidationResult.Ok,          SharedDatabaseVerdict.Ok)]
    [InlineData(DatabaseValidationResult.Corrupt,     SharedDatabaseVerdict.Damaged)]
    [InlineData(DatabaseValidationResult.Unreadable,  SharedDatabaseVerdict.Unreadable)]
    [InlineData(DatabaseValidationResult.Unreachable, SharedDatabaseVerdict.Unreachable)]
    [InlineData(DatabaseValidationResult.Missing,     SharedDatabaseVerdict.Missing)]
    [InlineData((DatabaseValidationResult)99,         SharedDatabaseVerdict.Unreadable)]
    public void ToVerdict_MapsEveryValidationResult(DatabaseValidationResult validation, SharedDatabaseVerdict expected)
        => Assert.Equal(expected, LocalCopyIntegrity.ToVerdict(validation));

    /// <summary>
    /// The fresh-copy result maps onto the table's inputs: made → open again; the shared location not responding or a
    /// held file → the matching front-door message (never "this computer has a problem"); anything else → "keeps
    /// failing".
    /// </summary>
    [Theory]
    [InlineData(LocalCopyRefreshResult.Ok,                  SharedDatabaseVerdict.Ok,          FreshCopyState.Made)]
    [InlineData(LocalCopyRefreshResult.Unreachable,         SharedDatabaseVerdict.Unreachable, FreshCopyState.NotTried)]
    [InlineData(LocalCopyRefreshResult.Unreadable,          SharedDatabaseVerdict.Unreadable,  FreshCopyState.NotTried)]
    [InlineData(LocalCopyRefreshResult.Failed,              SharedDatabaseVerdict.Ok,          FreshCopyState.Failed)]
    [InlineData(LocalCopyRefreshResult.HoldsUnsavedChanges, SharedDatabaseVerdict.Ok,          FreshCopyState.Failed)]
    public void AfterFreshCopy_MapsEveryRefreshResult(
        LocalCopyRefreshResult result, SharedDatabaseVerdict expectedShared, FreshCopyState expectedFresh)
    {
        var (shared, fresh) = LocalCopyIntegrity.AfterFreshCopy(result, SharedDatabaseVerdict.Ok);

        Assert.Equal(expectedShared, shared);
        Assert.Equal(expectedFresh, fresh);
    }

    /// <summary>
    /// Fed back into the decision table for a damaged copy (no recovered changes), each fresh-copy result leads to
    /// exactly one final step and never to a second fresh copy.
    /// </summary>
    [Theory]
    [InlineData(LocalCopyRefreshResult.Unreachable, LocalCopyAction.ShowUnreachableMessage)]
    [InlineData(LocalCopyRefreshResult.Unreadable,  LocalCopyAction.ShowUnreadableMessage)]
    [InlineData(LocalCopyRefreshResult.Failed,      LocalCopyAction.ShowKeepsFailing)]
    public void AfterFreshCopy_ThenDecide_GivesTheMatchingMessage(LocalCopyRefreshResult result, LocalCopyAction expected)
    {
        var (shared, fresh) = LocalCopyIntegrity.AfterFreshCopy(result, SharedDatabaseVerdict.Ok);

        Assert.Equal(expected,
            LocalCopyCheckDecision.Decide(LocalCopyVerdict.Damaged, holdsRecoveredChanges: false, shared, fresh));
    }
}
