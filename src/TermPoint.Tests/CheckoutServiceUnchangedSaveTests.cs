using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for spec item 3.4 (1.2.4; 1.2.3 section 29b, finding F5): a save whose snapshot holds nothing the shared database
/// D does not already have writes nothing. The decision compares the snapshot's content fingerprint
/// (<see cref="SqliteContentHash"/>) with <see cref="CheckoutService.ContentHashAtCheckout"/>, and skips only when the
/// starting point is known, no rename is unconfirmed, and no dirty marker exists.
///
/// <para>"Not normalised" D means a D whose header differs from TermPoint's own snapshot of it (finding F5): a new
/// database, a VACUUM INTO copy, a WAL-header database, a header written by another SQLite version. A no-edit save of each
/// used to rewrite D; now D's bytes and last-write time stay exactly as they were, and no temporary file is written.</para>
///
/// <para>The app's connection to D' is simulated by a plain connection in WAL mode (what <c>DatabaseContext</c> opens);
/// edits are simulated as in <see cref="CheckoutServiceMidSaveEditTests"/>.</para>
/// </summary>
public sealed class CheckoutServiceUnchangedSaveTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workingDir;

    /// <summary>Creates isolated temporary directories for D and the working copies.</summary>
    public CheckoutServiceUnchangedSaveTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"usv_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"usv_wrk_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Deletes both temporary directories.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir,    recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>The four kinds of D that used to be rewritten by a no-edit save (finding F5).</summary>
    public enum Variant { NewDatabase, VacuumCopy, WalHeader, OtherSqliteVersion }

    /// <summary>One writer session with the app's WAL connection to D' open. Disposing closes the connection.</summary>
    private sealed class Rig : IDisposable
    {
        /// <summary>The service under test.</summary>
        public required CheckoutService Svc { get; init; }
        /// <summary>Its lock service.</summary>
        public required WriteLockService Lock { get; init; }
        /// <summary>Path of D.</summary>
        public required string Db { get; init; }
        /// <summary>The app's connection to D'.</summary>
        public required SqliteConnection App { get; init; }
        /// <summary>Path of the dirty marker.</summary>
        public string Marker => Svc.WorkingPath + ".dirty";

        /// <summary>A user edit as <c>DatabaseContext</c> makes it: count, write, report.</summary>
        /// <param name="value">The row inserted into <c>_edits</c>.</param>
        public void Edit(string value) { Svc.NoteEdit(); RawWrite(value); Svc.MarkDirty(); }

        /// <summary>A committed write the app never reported (no count, no marker): only the content or <c>data_version</c> can notice it.</summary>
        /// <param name="value">The row inserted into <c>_edits</c>.</param>
        public void RawWrite(string value)
        {
            using var cmd = App.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS _edits(val TEXT); INSERT INTO _edits VALUES($v)";
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Closes the app's connection.</summary>
        public void Dispose() => App.Dispose();
    }

    /// <summary>Creates D of the given kind in the share folder.</summary>
    /// <param name="variant">Which not-normalised D to build.</param>
    /// <returns>The path of D.</returns>
    private async Task<string> CreateDAsync(Variant variant)
    {
        var db = Path.Combine(_tempDir, $"dept_{variant}.db");
        switch (variant)
        {
            case Variant.NewDatabase:
                Assert.Equal(NewDatabaseOutcome.Created, (await NewDatabaseFile.CreateAsync(db, _tempDir)).Outcome);
                break;

            case Variant.VacuumCopy:
            {
                var source = Path.Combine(_tempDir, "vacuum_source.db");
                CreatePopulated(source);
                using var conn = new SqliteConnection($"Data Source={source};Pooling=False");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO $p";
                cmd.Parameters.AddWithValue("$p", db);
                cmd.ExecuteNonQuery();
                break;
            }

            case Variant.WalHeader:
            {
                CreatePopulated(db);
                using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO _t(v) VALUES('in wal')";
                    cmd.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();
                Assert.Equal(2, File.ReadAllBytes(db)[18]);
                break;
            }

            case Variant.OtherSqliteVersion:
            {
                CreatePopulated(db);
                using var fs = new FileStream(db, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                fs.Position = 96;
                fs.Write(new byte[] { 0x00, 0x2D, 0xC6, 0xC0 }, 0, 4);
                break;
            }
        }
        return db;
    }

    /// <summary>Creates a small multi-page database with a <c>_t</c> table at <paramref name="path"/>.</summary>
    /// <param name="path">Where to create it.</param>
    private static void CreatePopulated(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE _t(id INTEGER PRIMARY KEY, v TEXT)";
        cmd.ExecuteNonQuery();
        using var tx = conn.BeginTransaction();
        for (var i = 0; i < 100; i++)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO _t(v) VALUES($v)";
            ins.Parameters.AddWithValue("$v", new string('x', 100) + i);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Checks out <paramref name="db"/> for writing and opens the app's WAL connection to D'.</summary>
    /// <param name="db">Path of D.</param>
    /// <param name="logger">Optional logger.</param>
    private async Task<Rig> OpenAsync(string db, IAppLogger? logger = null)
    {
        var lockSvc = new WriteLockService();
        var svc = new CheckoutService(
            lockService: lockSvc, logger: logger ?? new QuietLogger(), dispatch: a => a(), workingDir: _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        return WrapAsRig(svc, lockSvc, db);
    }

    /// <summary>Opens the app's WAL connection on the service's working copy and wraps it.</summary>
    private static Rig WrapAsRig(CheckoutService svc, WriteLockService lockSvc, string db)
    {
        var app = new SqliteConnection($"Data Source={svc.WorkingPath};Mode=ReadWrite;Pooling=False");
        app.Open();
        using var wal = app.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL";
        wal.ExecuteScalar();
        return new Rig { Svc = svc, Lock = lockSvc, Db = db, App = app };
    }

    /// <summary>Reads the <c>_edits</c> values of a database, in insertion order (empty when there is no such table).</summary>
    /// <param name="path">A SQLite file.</param>
    private static List<string> Edits(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='_edits'";
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0) return new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _edits ORDER BY rowid";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Whether two files have the same bytes AND the same last-write time (D untouched).</summary>
    private sealed record FileState(byte[] Bytes, DateTime LastWriteUtc)
    {
        /// <summary>Reads the current state of a file.</summary>
        /// <param name="path">The file.</param>
        public static FileState Of(string path) => new(File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path));

        /// <summary>Asserts that <paramref name="path"/> is exactly as this state recorded it.</summary>
        /// <param name="path">The file.</param>
        public void AssertUnchanged(string path)
        {
            Assert.Equal(Bytes, File.ReadAllBytes(path));
            Assert.Equal(LastWriteUtc, File.GetLastWriteTimeUtc(path));
        }
    }

    /// <summary>No-op logger.</summary>
    private sealed class QuietLogger : IAppLogger
    {
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) { }
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) { }
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }

    // ── The skip ──────────────────────────────────────────────────────────────

    /// <summary>
    /// THE F5 PROBE: a no-edit save of each not-normalised D leaves D's bytes AND last-write time unchanged and writes no
    /// temporary file; it succeeds, clears nothing, and keeps the known fingerprint.
    /// </summary>
    [Theory]
    [InlineData(Variant.NewDatabase)]
    [InlineData(Variant.VacuumCopy)]
    [InlineData(Variant.WalHeader)]
    [InlineData(Variant.OtherSqliteVersion)]
    public async Task NoEditSave_OfANotNormalisedD_WritesNothing(Variant variant)
    {
        var db = await CreateDAsync(variant);
        using var rig = await OpenAsync(db);
        var known = rig.Svc.ContentHashAtCheckout;
        Assert.NotNull(known);
        var hashAtCheckout = rig.Svc.HashAtCheckout;
        var before = FileState.Of(db);

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        before.AssertUnchanged(db);
        ShareTemp.AssertNone(db);
        Assert.False(rig.Svc.LastSaveKeptMarker);
        Assert.False(File.Exists(rig.Marker));
        Assert.Equal(known, rig.Svc.ContentHashAtCheckout);
        Assert.Equal(hashAtCheckout, rig.Svc.HashAtCheckout);
        Assert.True(rig.Svc.SessionDirty, "a skipped save changes none of the session's state");
    }

    /// <summary>The same no-edit save at exit: <c>ReleaseAsync(saveFirst: true)</c> releases the lock and returns Success; D untouched.</summary>
    [Fact]
    public async Task ReleaseWithNoEdits_ReleasesTheLock_AndLeavesDUntouched()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        using var rig = await OpenAsync(db);
        var before = FileState.Of(db);

        var outcome = await rig.Svc.ReleaseAsync(saveFirst: true);

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.False(rig.Lock.IsWriter);
        before.AssertUnchanged(db);
        ShareTemp.AssertNone(db);
    }

    /// <summary>The save events: a skipped save still raises SaveCompleted (the outage notice clears on it) and no failure banner.</summary>
    [Fact]
    public async Task ASkippedSave_RaisesSaveCompleted_AndNoBanner()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        var completed = 0;
        var banners = new List<string>();
        rig.Svc.SaveCompleted += () => completed++;
        rig.Svc.SaveFailed    += (m, _) => banners.Add(m);

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        Assert.Equal(1, completed);
        Assert.Empty(banners);
    }

    // ── Real edits are saved ──────────────────────────────────────────────────

    /// <summary>A real edit is saved: it reaches D, the marker is deleted, and the fingerprint becomes the saved snapshot's.</summary>
    [Fact]
    public async Task ARealEdit_IsSaved_AndTheFingerprintFollowsIt()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        using var rig = await OpenAsync(db);
        var before = rig.Svc.ContentHashAtCheckout;
        rig.Edit("e1");

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        Assert.Contains("e1", Edits(db));
        Assert.False(File.Exists(rig.Marker));
        Assert.NotEqual(before, rig.Svc.ContentHashAtCheckout);
        Assert.Equal(SqliteContentHash.Compute(db), rig.Svc.ContentHashAtCheckout);
    }

    /// <summary>An edit made after a skipped save is saved by the next save (and a no-change save after THAT is skipped again).</summary>
    [Fact]
    public async Task AnEditAfterASkippedSave_IsSavedByTheNextSave()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());   // skipped
        var untouched = FileState.Of(db);

        rig.Edit("e2");
        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        Assert.Contains("e2", Edits(db));
        Assert.False(File.Exists(rig.Marker));

        var after = FileState.Of(db);
        Assert.NotEqual(untouched.Bytes, after.Bytes);
        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());   // nothing new: skipped again
        after.AssertUnchanged(db);
    }

    /// <summary>
    /// A write that bypasses <c>MarkDirty</c> (and the edit count) made BEFORE the save is in the snapshot, whose content
    /// differs from D's, so it is saved: the content check is what protects it (no marker, no counter).
    /// </summary>
    [Fact]
    public async Task AWriteThatBypassesMarkDirty_IsSaved_BecauseTheContentDiffers()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        using var rig = await OpenAsync(db);
        rig.RawWrite("silent");
        Assert.False(File.Exists(rig.Marker));

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        Assert.Contains("silent", Edits(db));
    }

    /// <summary>
    /// A bypassing write that commits DURING a skipped save (after the snapshot, before the decision): the snapshot lacks it
    /// so the save is skipped, but data_version moved, so the save reports a kept marker (<c>LastSaveKeptMarker</c>) and
    /// holds the lock at exit. That pass writes no marker (nothing called <c>MarkDirty</c>); the helper's extra pass sees
    /// different content and saves the write. Exactly: pass 1 skipped + flagged, pass 2 real; D ends with the write.
    /// </summary>
    [Fact]
    public async Task ABypassingWriteDuringASkippedSave_IsSavedByTheHelpersSecondPass()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        using var rig = await OpenAsync(db);
        var fired = false;
        CrashPoints.Hook = name =>
        {
            if (name == "Save.AfterLocalSnapshot" && !fired) { fired = true; rig.RawWrite("silent-late"); }
        };
        try
        {
            var before = FileState.Of(db);

            // One plain save: skipped (the snapshot predates the write), flagged, lock kept, D untouched, nothing marked.
            Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync(releaseLockAfter: true));
            Assert.True(rig.Svc.LastSaveKeptMarker);
            Assert.True(rig.Lock.IsWriter, "the lock is kept for the extra pass");
            Assert.False(File.Exists(rig.Marker));
            before.AssertUnchanged(db);

            // The helper: the same situation, then it saves again and the write reaches D. (Hook already spent.)
            Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveUntilMarkerClearAsync(releaseLockAfter: true));
            Assert.Contains("silent-late", Edits(db));
            Assert.False(rig.Svc.LastSaveKeptMarker);
            Assert.False(rig.Lock.IsWriter);
        }
        finally { CrashPoints.Hook = null; }
    }

    /// <summary>
    /// An edit (counted, written, marked) that arrives right after the snapshot is not skipped at all: its marker exists
    /// before the decision, so the save is a real one that keeps the marker (item 3.2) and the next save carries the edit.
    /// </summary>
    [Fact]
    public async Task AFullEditDuringTheSnapshotWindow_MakesTheSaveARealOne_AndKeepsTheMarker()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        var fired = false;
        CrashPoints.Hook = name =>
        {
            if (name == "Save.AfterLocalSnapshot" && !fired) { fired = true; rig.Edit("late"); }
        };
        try
        {
            Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());
            Assert.True(rig.Svc.LastSaveKeptMarker);
            Assert.True(File.Exists(rig.Marker));
            Assert.DoesNotContain("late", Edits(db));
        }
        finally { CrashPoints.Hook = null; }

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());
        Assert.Contains("late", Edits(db));
        Assert.False(File.Exists(rig.Marker));
    }

    /// <summary>
    /// A counted edit whose write and marker have not happened yet when the decision is made (the counter moved and
    /// nothing else): the skipped save sets <c>LastSaveKeptMarker</c>, keeps the lock at exit, and the helper's next pass
    /// saves what is there. (The edit's own <c>MarkDirty</c> writes the marker; the skipped save writes none.)
    /// </summary>
    [Fact]
    public async Task ACountedEditDuringASkippedSave_KeepsTheLock_AndTheNextPassSavesIt()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        var fired = false;
        CrashPoints.Hook = name =>
        {
            if (name == "Save.AfterLocalSnapshot" && !fired) { fired = true; rig.Svc.NoteEdit(); rig.RawWrite("counted"); }
        };
        try
        {
            Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync(releaseLockAfter: true));
            Assert.True(rig.Svc.LastSaveKeptMarker);
            Assert.True(rig.Lock.IsWriter);
            Assert.DoesNotContain("counted", Edits(db));

            Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveUntilMarkerClearAsync(releaseLockAfter: true));
            Assert.Contains("counted", Edits(db));
            Assert.False(rig.Lock.IsWriter);
        }
        finally { CrashPoints.Hook = null; }
    }

    // ── When the starting point is not known, or there is unsaved work: never skip ──

    /// <summary>A save while a marker exists is a real one even if the content is equal (a write that changed nothing).</summary>
    [Fact]
    public async Task ASaveWhileAMarkerExists_NeverSkips_EvenWhenTheContentIsEqual()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        using var rig = await OpenAsync(db);
        rig.Svc.NoteEdit();
        rig.Svc.MarkDirty();            // a marker, but no change to the data
        Assert.True(File.Exists(rig.Marker));
        var before = FileState.Of(db);

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        Assert.NotEqual(before.Bytes, File.ReadAllBytes(db));   // D was rewritten: not skipped
        Assert.False(File.Exists(rig.Marker));
    }

    /// <summary>A save with a pending (unconfirmed) rename hash never skips; the real save clears the pending hash and learns D.</summary>
    [Fact]
    public async Task ASaveWithAPendingHash_NeverSkips()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        using var rig = await OpenAsync(db);
        rig.Svc.RecordPendingSaveHash("0000-not-d");
        var before = FileState.Of(db);

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        Assert.NotEqual(before.Bytes, File.ReadAllBytes(db));
        Assert.Equal(SqliteContentHash.Compute(db), rig.Svc.ContentHashAtCheckout);
    }

    /// <summary>
    /// A crash-recovery checkout never skips: the fingerprint is not known (null), so the first save is a real one even
    /// when the recovered copy holds the same data as D (the marker is removed here to isolate the null fingerprint).
    /// </summary>
    [Fact]
    public async Task ARecoveryCheckout_HasNoFingerprint_AndItsFirstSaveIsReal()
    {
        var db = await CreateDAsync(Variant.OtherSqliteVersion);
        var svc1 = new CheckoutService(new WriteLockService(), new QuietLogger(), a => a(), _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc1.CheckoutAsync(db));
        Assert.NotNull(svc1.ContentHashAtCheckout);
        svc1.NoteEdit();
        svc1.MarkDirty();   // the marker a crashed session leaves
        var workingPath = svc1.WorkingPath;
        // The crash: the first service is abandoned, its lock left as a stale lock.
        var lockPath = Path.ChangeExtension(db, ".lock");
        File.Delete(lockPath);

        var svc2 = new CheckoutService(new WriteLockService(), new QuietLogger(), a => a(), _workingDir);
        Assert.True(svc2.DetectCrashRecovery(db));
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc2.CheckoutAsync(db, recoverWorkingCopy: true));
        Assert.Equal(workingPath, svc2.WorkingPath);
        Assert.Null(svc2.ContentHashAtCheckout);

        File.Delete(workingPath + ".dirty");
        var before = FileState.Of(db);
        Assert.Equal(SaveOutcome.Success, await svc2.SaveAsync());

        Assert.NotEqual(before.Bytes, File.ReadAllBytes(db));   // a real save
        Assert.NotNull(svc2.ContentHashAtCheckout);              // which learns D
    }

    /// <summary>A fresh checkout starts with no fingerprint until its copy is verified, and a failed-open reset forgets it.</summary>
    [Fact]
    public async Task TheFingerprint_IsKnownAfterACheckout_AndForgottenByAFailedOpenReset()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        Assert.Equal(SqliteContentHash.Compute(db), rig.Svc.ContentHashAtCheckout);

        rig.App.Dispose();
        SqliteConnection.ClearAllPools();
        await rig.Svc.ResetAfterFailedOpenAsync();

        Assert.Null(rig.Svc.ContentHashAtCheckout);
    }

    /// <summary>
    /// The refresh of a writer's local copy (<c>RefreshWriterCopyAsync</c>, reached through <c>RefreshLocalCopyAfterFailedCheckAsync</c>) sets
    /// the fingerprint from the fresh copy; verified through its public entry point when there is no unsaved work.
    /// </summary>
    [Fact]
    public async Task ARefreshedWriterCopy_HasTheFingerprintOfD()
    {
        var db = await CreateDAsync(Variant.WalHeader);
        using var rig = await OpenAsync(db);
        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());   // skipped: nothing to refresh away
        rig.App.Dispose();
        SqliteConnection.ClearAllPools();

        var result = await rig.Svc.RefreshLocalCopyAfterFailedCheckAsync();

        Assert.Equal(LocalCopyRefreshResult.Ok, result);
        Assert.Equal(SqliteContentHash.Compute(db), rig.Svc.ContentHashAtCheckout);
    }

    // ── The checks before the skip give today's outcomes ──────────────────────

    /// <summary>D changed by someone else before the skip: SourceModified, the conflict banner state, nothing skipped.</summary>
    [Fact]
    public async Task DChangedByAnotherProgram_StillGivesSourceModified()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO _t(v) VALUES('colleague')";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(SaveOutcome.SourceModified, await rig.Svc.SaveAsync());
        Assert.True(rig.Svc.HasSaveConflict);
    }

    /// <summary>The lock taken over before the skip: LockLost, as today.</summary>
    [Fact]
    public async Task ALockTakenOver_StillGivesLockLost()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        var now = DateTime.UtcNow;
        File.WriteAllText(Path.ChangeExtension(db, ".lock"), System.Text.Json.JsonSerializer.Serialize(new LockFileData
        {
            Username = "other_user", Machine = "OTHER-PC", SessionGuid = Guid.NewGuid().ToString(), Acquired = now, Heartbeat = now
        }));

        var outcome = await rig.Svc.SaveAsync();

        Assert.Equal(SaveOutcome.LockLost, outcome);
    }

    /// <summary>The location not answering at the source hash before the skip: CopyError with the one outage notice, as today.</summary>
    [Fact]
    public async Task AnUnreachableLocation_StillGivesCopyError_AndTheOutageNotice()
    {
        var db = await CreateDAsync(Variant.VacuumCopy);
        using var rig = await OpenAsync(db);
        var banners = new List<string>();
        rig.Svc.SaveFailed += (m, _) => banners.Add(m);
        NetworkFaults.Decide = r => r.Label == "ComputeHash" && string.Equals(r.Path, db, StringComparison.OrdinalIgnoreCase)
            ? NetworkFaultDecision.TimeOut
            : NetworkFaultDecision.None;
        try
        {
            Assert.Equal(SaveOutcome.CopyError, await rig.Svc.SaveAsync());
        }
        finally { NetworkFaults.Decide = null; }

        Assert.Single(banners);
        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());   // and afterwards the skip works
    }

    // ── The real database ─────────────────────────────────────────────────────

    /// <summary>
    /// The local real-data database, copied into a temporary share folder first (never opened in place): a no-edit save
    /// leaves D's bytes and last-write time alone. Runs only on a machine that has the private database.
    /// </summary>
    [FactRequiresLocalDb]
    public async Task NoEditSave_OfTheLocalRealDatabase_WritesNothing()
    {
        var db = Path.Combine(_tempDir, "real.db");
        File.Copy(RoomAvailabilityIntegrationTests.SourceDbPath, db);
        // The real file may still have a -wal beside it on its home machine; the copy is the main file only.
        using var rig = await OpenAsync(db);
        var before = FileState.Of(db);

        Assert.Equal(SaveOutcome.Success, await rig.Svc.SaveAsync());

        before.AssertUnchanged(db);
        ShareTemp.AssertNone(db);
    }
}
