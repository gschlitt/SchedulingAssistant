using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for spec item 3.2 (1.2.4): an edit made while a save runs must not lose the dirty marker that protects it.
/// A save reads the edit count (<see cref="CheckoutService.NoteEdit"/>) and SQLite's <c>PRAGMA data_version</c> on its own
/// source connection just before its snapshot, and again at step 6; if either moved, an edit arrived during the save, so
/// the marker is kept (rewritten with the hash of the D the save just landed) and the next save carries the edit.
///
/// <para>Edits are simulated the way production makes them: <see cref="CheckoutService.NoteEdit"/> (the
/// <c>onEveryWrite</c> callback), a real SQL write on a SECOND connection to D' (the app's connection, in WAL mode like
/// <c>DatabaseContext</c>), then <see cref="CheckoutService.MarkDirty"/> (the <c>onFirstWrite</c> callback). "During the
/// upload" is the <see cref="CheckoutService.BeforeTmpVerifyAsync"/> seam: after the snapshot and the copy to D.tmp,
/// before step 6.</para>
/// </summary>
public sealed class CheckoutServiceMidSaveEditTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _workingDir;

    /// <summary>Creates isolated temporary directories for D and the working copies.</summary>
    public CheckoutServiceMidSaveEditTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"msx_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"msx_wrk_{id}");
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

    /// <summary>
    /// One writer session on a fresh database D (a <c>_test</c> table holding the row "base"), with the app's own
    /// connection to D' open in WAL mode. Disposing it closes that connection.
    /// </summary>
    private sealed class Session : IDisposable
    {
        /// <summary>The checkout service under test.</summary>
        public required CheckoutService Svc { get; init; }

        /// <summary>Its lock service.</summary>
        public required WriteLockService Lock { get; init; }

        /// <summary>Path of D.</summary>
        public required string Db { get; init; }

        /// <summary>The app's connection to D' (the one <c>DatabaseContext</c> would hold), in WAL mode; null for a session without one.</summary>
        public SqliteConnection? App { get; init; }

        /// <summary>The path of the dirty marker file.</summary>
        public string MarkerPath => Svc.WorkingPath + ".dirty";

        /// <summary>
        /// Makes a user edit the way <c>DatabaseContext</c> does: counts it, writes through the app's connection, then
        /// reports the write to the checkout service (which writes the marker).
        /// </summary>
        /// <param name="value">The row inserted into <c>_test</c>.</param>
        public void Edit(string value)
        {
            Svc.NoteEdit();
            RawWrite(value);
            Svc.MarkDirty();
        }

        /// <summary>
        /// Commits a row through the app's connection WITHOUT counting it and without reporting it: only SQLite's
        /// <c>data_version</c> can notice this write.
        /// </summary>
        /// <param name="value">The row inserted into <c>_test</c>.</param>
        public void RawWrite(string value)
        {
            using var cmd = App!.CreateCommand();
            cmd.CommandText = "INSERT INTO _test VALUES($v)";
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Closes the app's connection.</summary>
        public void Dispose() => App?.Dispose();
    }

    /// <summary>
    /// Creates D (a <c>_test</c> table with the row "base"), checks it out for writing and opens the app's WAL connection
    /// to D'.
    /// </summary>
    /// <param name="withAppConnection">False for a session with no open connection to D' (the disposal tests).</param>
    /// <returns>The session.</returns>
    private async Task<Session> OpenSessionAsync(bool withAppConnection = true)
    {
        var db = Path.Combine(_tempDir, "test.db");
        using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE _test(val TEXT); INSERT INTO _test VALUES('base')";
            cmd.ExecuteNonQuery();
        }

        var lockSvc = new WriteLockService();
        var svc = new CheckoutService(
            lockService: lockSvc, logger: new QuietLogger(), dispatch: action => action(), workingDir: _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        SqliteConnection? app = null;
        if (withAppConnection)
        {
            app = new SqliteConnection($"Data Source={svc.WorkingPath};Mode=ReadWrite;Pooling=False");
            app.Open();
            using var wal = app.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode=WAL";
            wal.ExecuteScalar();
        }
        return new Session { Svc = svc, Lock = lockSvc, Db = db, App = app };
    }

    /// <summary>Reads every <c>_test</c> value of the database at <paramref name="path"/>, in insertion order.</summary>
    /// <param name="path">A SQLite file.</param>
    private static List<string> Values(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>A seam body that runs <paramref name="action"/> on the first <paramref name="times"/> calls only.</summary>
    /// <param name="action">What to do during the upload.</param>
    /// <param name="times">How many saves the action applies to (default one).</param>
    /// <returns>A function to assign to <see cref="CheckoutService.BeforeTmpVerifyAsync"/>.</returns>
    private static Func<string, Task> DuringUpload(Action action, int times = 1)
    {
        var calls = 0;
        return _ =>
        {
            if (++calls <= times) action();
            return Task.CompletedTask;
        };
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

    // ── The save's two edges ──────────────────────────────────────────────────

    /// <summary>An edit made BEFORE the snapshot is in the snapshot: today's behaviour, the marker is deleted.</summary>
    [Fact]
    public async Task EditBeforeTheSnapshot_IsSaved_AndTheMarkerIsDeleted()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        Assert.True(File.Exists(s.MarkerPath));

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());

        Assert.Contains("e1", Values(s.Db));
        Assert.False(File.Exists(s.MarkerPath));
        Assert.False(s.Svc.SessionDirty);
        Assert.False(s.Svc.LastSaveKeptMarker);
    }

    /// <summary>
    /// An edit made DURING the upload (after the snapshot) is not in D. The save still lands, but keeps the marker,
    /// rewritten with the new <c>HashAtCheckout</c> and no pending hash; the indicator comes back on right after
    /// <c>SaveCompleted</c>; and the next save puts the edit in D and deletes the marker.
    /// </summary>
    [Fact]
    public async Task EditDuringTheUpload_KeepsTheMarker_AndTheNextSaveCarriesIt()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        var events = new List<string>();
        s.Svc.SaveCompleted += () => events.Add("completed");
        s.Svc.BecameDirty   += () => events.Add("dirty");
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.Edit("e2"));

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());

        Assert.Contains("e1", Values(s.Db));
        Assert.DoesNotContain("e2", Values(s.Db));
        Assert.True(File.Exists(s.MarkerPath));
        Assert.True(s.Svc.SessionDirty);
        Assert.True(s.Svc.LastSaveKeptMarker);
        var marker = CheckoutService.TryReadMarker(s.MarkerPath);
        Assert.NotNull(marker);
        Assert.Equal(s.Svc.HashAtCheckout, marker!.HashAtCheckout);
        Assert.Equal(DbFileTestHelpers.HashOf(s.Db), marker.HashAtCheckout);
        Assert.Null(marker.PendingSaveHash);
        Assert.Equal(new[] { "dirty", "completed", "dirty" }, events.Skip(events.Count - 3));

        s.Svc.BeforeTmpVerifyAsync = null;
        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());

        Assert.Contains("e2", Values(s.Db));
        Assert.False(File.Exists(s.MarkerPath));
        Assert.False(s.Svc.SessionDirty);
        Assert.False(s.Svc.LastSaveKeptMarker);
    }

    /// <summary>
    /// A write committed on another connection after the snapshot began, with NO <c>NoteEdit</c>, is still noticed:
    /// SQLite's <c>data_version</c> changed on the save's own connection. Proves the second counter works by itself.
    /// </summary>
    [Fact]
    public async Task CommitOnly_WithoutNoteEdit_IsNoticedByDataVersion_AndKeepsTheMarker()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        var generation = s.Svc.EditGeneration;
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.RawWrite("silent"));

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());

        Assert.Equal(generation, s.Svc.EditGeneration);   // nothing counted: only data_version can have seen it
        Assert.True(s.Svc.LastSaveKeptMarker);
        Assert.True(File.Exists(s.MarkerPath));
        Assert.True(s.Svc.SessionDirty);
        Assert.DoesNotContain("silent", Values(s.Db));
    }

    /// <summary>
    /// Pins that nothing else bumps <c>data_version</c>: a database with real WAL content (thousands of rows, so the
    /// auto-checkpoint has run), a checkpoint by the app's connection DURING the upload, and no edit at all. The marker is
    /// deleted, twice in a row. If SQLite ever counted a checkpoint (or a WAL reset) as a commit this would fail.
    /// </summary>
    [Fact]
    public async Task NoEdit_WithWalContentAndACheckpointDuringTheUpload_DeletesTheMarker()
    {
        using var s = await OpenSessionAsync();
        using (var tx = s.App!.BeginTransaction())
        {
            for (var i = 0; i < 3000; i++)
            {
                using var cmd = s.App.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO _test VALUES($v)";
                cmd.Parameters.AddWithValue("$v", new string('x', 1000) + i);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        s.Svc.NoteEdit();
        s.Svc.MarkDirty();   // the bulk write above counts as the session's one edit, made before the save
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() =>
        {
            using var cp = s.App.CreateCommand();
            cp.CommandText = "PRAGMA wal_checkpoint(PASSIVE)";
            cp.ExecuteNonQuery();
        });

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());
        Assert.False(s.Svc.LastSaveKeptMarker, "a checkpoint is not an edit");
        Assert.False(File.Exists(s.MarkerPath));

        s.Svc.BeforeTmpVerifyAsync = null;
        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());
        Assert.False(s.Svc.LastSaveKeptMarker);
        Assert.False(File.Exists(s.MarkerPath));
        Assert.Equal(3001, Values(s.Db).Count);
    }

    // ── The exit: the lock is kept until the edit is saved ────────────────────

    /// <summary>
    /// A save with <c>releaseLockAfter</c> that kept its marker does NOT release the lock; the exit's extra pass
    /// (<see cref="CheckoutService.ReleaseAsync"/>) then saves the edit and releases.
    /// </summary>
    [Fact]
    public async Task SaveWithReleaseLock_AndAKeptMarker_KeepsTheLock_UntilTheExitSavesAgain()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.Edit("e2"));

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync(releaseLockAfter: true));

        Assert.True(s.Svc.LastSaveKeptMarker);
        Assert.True(s.Lock.IsWriter, "the lock must stay ours while an edit is unsaved");
        Assert.True(File.Exists(s.MarkerPath));

        // The seam fired once and is spent: ReleaseAsync's first pass carries e2 and releases.
        Assert.Equal(SaveOutcome.Success, await s.Svc.ReleaseAsync(saveFirst: true));

        Assert.Contains("e2", Values(s.Db));
        Assert.False(File.Exists(s.MarkerPath));
        Assert.False(s.Lock.IsWriter);
    }

    /// <summary>
    /// An edit during the exit save's first pass: the helper saves once more, that pass carries the edit and releases the
    /// lock, the marker is gone, and the working copy may be deleted.
    /// </summary>
    [Fact]
    public async Task Release_WithAnEditDuringTheFirstExitSave_SavesAgain_AndReleases()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.Edit("e2"));

        var outcome = await s.Svc.ReleaseAsync(saveFirst: true);

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Contains("e1", Values(s.Db));
        Assert.Contains("e2", Values(s.Db));
        Assert.False(File.Exists(s.MarkerPath));
        Assert.False(s.Lock.IsWriter);
    }

    /// <summary>
    /// An edit during BOTH exit saves (practically impossible behind the "Saving and closing" curtain): the lock is
    /// released anyway so the exit can finish, and <see cref="CheckoutService.CleanupWorkingCopy"/> keeps D' because its
    /// marker is still there: the edit is offered back at the next open.
    /// </summary>
    [Fact]
    public async Task Release_WithAnEditDuringBothExitSaves_ReleasesTheLock_AndKeepsTheWorkingCopy()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        var n = 1;
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.Edit($"late-{++n}"), times: 2);

        var outcome = await s.Svc.ReleaseAsync(saveFirst: true);

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.False(s.Lock.IsWriter);
        Assert.True(File.Exists(s.MarkerPath));
        Assert.Contains("late-2", Values(s.Db));
        Assert.DoesNotContain("late-3", Values(s.Db));

        s.App!.Dispose();
        SqliteConnection.ClearAllPools();
        s.Svc.CleanupWorkingCopy();
        Assert.True(File.Exists(s.Svc.WorkingPath), "the working copy with an unsaved edit must be kept");
    }

    // ── The source connection never outlives the save ─────────────────────────

    /// <summary>
    /// Opens D' exclusively (no other handle may be open), proving the save's source connection is closed.
    /// </summary>
    /// <param name="path">The working copy.</param>
    private static void AssertNoHandleOpenOn(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>A save that fails after the snapshot (D.tmp vanishes) must close its source connection: D' can be opened exclusively and deleted.</summary>
    [Fact]
    public async Task FailedSave_UploadError_DisposesTheSourceConnection()
    {
        using var s = await OpenSessionAsync(withAppConnection: false);
        s.Svc.NoteEdit();
        s.Svc.MarkDirty();
        s.Svc.BeforeTmpVerifyAsync = path => { File.Delete(path); return Task.CompletedTask; };

        Assert.Equal(SaveOutcome.CopyError, await s.Svc.SaveAsync());

        AssertNoHandleOpenOn(s.Svc.WorkingPath);
        File.Delete(s.Svc.WorkingPath);
        Assert.False(File.Exists(s.Svc.WorkingPath));
    }

    /// <summary>An unexpected exception inside the save (caught by SaveAsync's safety net) also closes the source connection.</summary>
    [Fact]
    public async Task FailedSave_UnexpectedException_DisposesTheSourceConnection()
    {
        using var s = await OpenSessionAsync(withAppConnection: false);
        s.Svc.NoteEdit();
        s.Svc.MarkDirty();   // a marker, so the save is a real one (an unchanged save is skipped before the upload, item 3.4)
        s.Svc.BeforeTmpVerifyAsync = _ => throw new InvalidOperationException("unexpected, on purpose");

        Assert.Equal(SaveOutcome.CopyError, await s.Svc.SaveAsync());

        AssertNoHandleOpenOn(s.Svc.WorkingPath);
    }

    /// <summary>A save refused at the final check (D changed meanwhile) closes the source connection.</summary>
    [Fact]
    public async Task RefusedSave_FinalCheck_DisposesTheSourceConnection()
    {
        using var s = await OpenSessionAsync(withAppConnection: false);
        s.Svc.NoteEdit();
        s.Svc.MarkDirty();   // a marker, so the save is a real one (an unchanged save is skipped before the upload, item 3.4)
        s.Svc.BeforeTmpVerifyAsync = _ =>
        {
            using var conn = new SqliteConnection($"Data Source={s.Db};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO _test VALUES('colleague')";
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        };

        var outcome = await s.Svc.SaveAsync();

        Assert.NotEqual(SaveOutcome.Success, outcome);
        AssertNoHandleOpenOn(s.Svc.WorkingPath);
    }

    /// <summary>A successful save closes it too, whether or not the marker was kept.</summary>
    [Fact]
    public async Task SuccessfulSave_DisposesTheSourceConnection()
    {
        using var s = await OpenSessionAsync(withAppConnection: false);

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());

        AssertNoHandleOpenOn(s.Svc.WorkingPath);
    }

    // ── Restore from backup ───────────────────────────────────────────────────

    /// <summary>Creates a backup file the restore routine accepts as a TermPoint database.</summary>
    private string NewBackup()
    {
        var path = Path.Combine(_tempDir, $"backup_{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Concat(DatabaseRestore.RequiredTables.Select(t => $"CREATE TABLE {t} (key TEXT, data TEXT); "))
                          + "CREATE TABLE _test(val TEXT); INSERT INTO _test VALUES('from-backup')";
        cmd.ExecuteNonQuery();
        return path;
    }

    /// <summary>
    /// The backstop: while the dirty marker exists the restore refuses (the confirmed restore would end the session and
    /// delete D' and the marker with the edit in it). Reported as Failed (text "Something went wrong, so the restore was
    /// stopped. Your database is unchanged."), D untouched, nothing created.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackup_RefusesWhileTheDirtyMarkerExists()
    {
        using var s = await OpenSessionAsync();
        s.Edit("unsaved");
        var backup = NewBackup();
        var before = DbFileTestHelpers.HashOf(s.Db);

        var result = await s.Svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal(before, DbFileTestHelpers.HashOf(s.Db));
        Assert.True(File.Exists(s.MarkerPath));
    }

    /// <summary>With no marker (everything saved) the same restore goes ahead.</summary>
    [Fact]
    public async Task RestoreFromBackup_ProceedsOnceTheMarkerIsGone()
    {
        using var s = await OpenSessionAsync();
        s.Edit("unsaved");
        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveAsync());
        Assert.False(File.Exists(s.MarkerPath));

        var result = await s.Svc.RestoreFromBackupAsync(NewBackup());

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
    }

    /// <summary>
    /// The shared helper: an edit during the first save gets a second save, which carries it, so "everything is saved" is
    /// true when it returns; an edit during both leaves the marker kept for the caller to see.
    /// </summary>
    [Fact]
    public async Task SaveUntilMarkerClear_SavesOnceMore_AndReportsAMarkerThatIsStillKept()
    {
        using var s = await OpenSessionAsync();
        s.Edit("e1");
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.Edit("e2"));

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveUntilMarkerClearAsync(releaseLockAfter: false));
        Assert.False(s.Svc.LastSaveKeptMarker);
        Assert.False(File.Exists(s.MarkerPath));
        Assert.Contains("e2", Values(s.Db));

        s.Edit("e3");
        var n = 3;
        s.Svc.BeforeTmpVerifyAsync = DuringUpload(() => s.Edit($"e{++n}"), times: 2);

        Assert.Equal(SaveOutcome.Success, await s.Svc.SaveUntilMarkerClearAsync(releaseLockAfter: false));
        Assert.True(s.Svc.LastSaveKeptMarker);
        Assert.True(File.Exists(s.MarkerPath));
        Assert.True(s.Lock.IsWriter);
    }
}
