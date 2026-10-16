using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the service side of the save-conflict banner (spec item 10, step 10a): a save refused with
/// <see cref="SaveOutcome.SourceModified"/> sets the sticky <see cref="CheckoutService.HasSaveConflict"/> and raises
/// <see cref="CheckoutService.SaveConflictDetected"/> (not <see cref="CheckoutService.SaveFailed"/>), and the data path
/// the banner's two choices rely on keeps the user's changes safe: end the session without saving, close the
/// connection, export D' with <see cref="CheckoutService.ExportRecoveredCopyAsync"/>, and delete D' and its marker
/// only afterwards (<see cref="CheckoutService.DiscardCrash"/>).
///
/// <para>Real files and real SQLite throughout: <c>_shareDir</c> stands in for the shared folder holding D and the
/// exported copy, <c>_workingDir</c> is where D' and its <c>.dirty</c> marker live.</para>
/// </summary>
public sealed class SaveConflictTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Stands in for the shared folder holding D and the exported copy.</summary>
    private readonly string _shareDir;

    /// <summary>Holds D' and its dirty marker (the checkout service's working folder).</summary>
    private readonly string _workingDir;

    /// <summary>Creates isolated folders for this test instance.</summary>
    public SaveConflictTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"sc_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"sc_work_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Removes both folders (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The value the session writes into D' (its unsaved change).</summary>
    private const string UnsavedValue = "unsaved-edit";

    /// <summary>Creates a checkout service on this fixture's working folder with a synchronous dispatcher.</summary>
    private CheckoutService NewService() => new(
        lockService: new WriteLockService(),
        logger:      new RecordingAppLogger(),
        dispatch:    action => action(),
        workingDir:  _workingDir);

    /// <summary>Creates a valid database at <paramref name="path"/> holding an empty <c>_test</c> table.</summary>
    private static void CreateDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        Exec(conn, "CREATE TABLE _test(val TEXT)");
    }

    /// <summary>Executes a statement on an open connection.</summary>
    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Writes <paramref name="value"/> into the <c>_test</c> table of the database at <paramref name="dbPath"/>.</summary>
    private static void InsertTestValue(string dbPath, string value)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the newest value of the <c>_test</c> table, or null when it is empty.</summary>
    private static string? ReadTestValue(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Builds the state the banner appears in: a writer has checked D out, holds an open WAL-mode connection to D'
    /// (as <c>DatabaseContext</c> does in production), has made an unsaved edit and marked the session dirty; then
    /// someone else changes D, and the writer's save is refused.
    /// </summary>
    /// <returns>The writer's service, its open D' connection (the caller disposes it), D's path, and the save outcome.</returns>
    private async Task<(CheckoutService Svc, SqliteConnection WorkingConn, string Db, SaveOutcome Outcome)> ConflictedSessionAsync()
    {
        var db = Path.Combine(_shareDir, "dept.db");
        CreateDatabase(db);

        var svc = NewService();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        var conn = new SqliteConnection($"Data Source={svc.WorkingPath};Mode=ReadWrite;Pooling=False");
        conn.Open();
        Exec(conn, "PRAGMA journal_mode=WAL");
        Exec(conn, "PRAGMA wal_autocheckpoint=0");   // the edit stays in the -wal, as it can in production
        Exec(conn, $"INSERT INTO _test VALUES('{UnsavedValue}')");
        svc.MarkDirty();

        // Someone else saves to D after our checkout.
        InsertTestValue(db, "someone-elses-edit");

        var outcome = await svc.SaveAsync();
        return (svc, conn, db, outcome);
    }

    // ── The state and the event ───────────────────────────────────────────────

    /// <summary>
    /// A refused save sets <see cref="CheckoutService.HasSaveConflict"/>, raises
    /// <see cref="CheckoutService.SaveConflictDetected"/> and does NOT raise <see cref="CheckoutService.SaveFailed"/>
    /// (the old "Save aborted" banner is gone). The event fires again on every refused save.
    /// </summary>
    [Fact]
    public async Task RefusedSave_SetsConflict_RaisesTheEventEachTime_AndNotSaveFailed()
    {
        var (svc, conn, _, outcome) = await ConflictedSessionAsync();
        using var workingConn = conn;
        var detected = 0;
        var failed   = 0;
        svc.SaveConflictDetected += () => detected++;
        svc.SaveFailed           += (_, _) => failed++;

        Assert.Equal(SaveOutcome.SourceModified, outcome);   // the first refusal, before we subscribed
        Assert.True(svc.HasSaveConflict);

        Assert.Equal(SaveOutcome.SourceModified, await svc.SaveAsync());
        Assert.Equal(SaveOutcome.SourceModified, await svc.SaveAsync());

        Assert.Equal(2, detected);
        Assert.Equal(0, failed);
        Assert.True(svc.HasSaveConflict);
    }

    /// <summary>A service that has not been refused has no conflict.</summary>
    [Fact]
    public async Task NoConflict_BeforeAnyRefusedSave()
    {
        var db = Path.Combine(_shareDir, "dept.db");
        CreateDatabase(db);
        var svc = NewService();

        Assert.False(svc.HasSaveConflict);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.False(svc.HasSaveConflict);
    }

    /// <summary>
    /// The state is sticky: it is cleared by a fresh checkout (the reopen after the user's choice) and by nothing
    /// before it.
    /// </summary>
    [Fact]
    public async Task Conflict_IsClearedByAFreshCheckout_AndNotBeforeIt()
    {
        var (svc, conn, db, _) = await ConflictedSessionAsync();
        Assert.True(svc.HasSaveConflict);

        // Ending the session does not clear it.
        await svc.ReleaseAsync(saveFirst: false);
        Assert.True(svc.HasSaveConflict);
        conn.Dispose();
        SqliteConnection.ClearAllPools();

        // A new checkout does (discard the kept changes first so the checkout is an ordinary one).
        svc.DiscardCrash(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.False(svc.HasSaveConflict);
    }

    /// <summary>
    /// <see cref="CheckoutService.ResetAfterFailedOpenAsync"/> forgets the session, so it clears the conflict too —
    /// while the kept changes (D' with a marker) stay on disk.
    /// </summary>
    [Fact]
    public async Task Conflict_IsClearedByResetAfterFailedOpen_AndTheKeptChangesStay()
    {
        var (svc, conn, _, _) = await ConflictedSessionAsync();
        var workingPath = svc.WorkingPath;
        Assert.True(svc.HasSaveConflict);

        conn.Dispose();
        SqliteConnection.ClearAllPools();
        await svc.ResetAfterFailedOpenAsync();

        Assert.False(svc.HasSaveConflict);
        Assert.True(File.Exists(workingPath));
        Assert.True(File.Exists(workingPath + ".dirty"));
    }

    // ── The data path of the banner's choices ─────────────────────────────────

    /// <summary>
    /// Save a Copy…: the session ends WITHOUT saving (D is byte-identical to what the other user left), the
    /// connection is closed, the export holds the user's edit, D' and its marker stay until
    /// <see cref="CheckoutService.DiscardCrash"/>, and are gone after it.
    /// </summary>
    [Fact]
    public async Task SaveACopy_ExportsTheEdit_NeverWritesD_AndTheKeptChangesGoOnlyOnDiscardCrash()
    {
        var (svc, conn, db, outcome) = await ConflictedSessionAsync();
        Assert.Equal(SaveOutcome.SourceModified, outcome);
        var workingPath = svc.WorkingPath;
        var dBefore     = DbFileTestHelpers.HashOf(db);

        // End the session without saving, then close the connection (what App.ReleaseServices does).
        Assert.Equal(SaveOutcome.Success, await svc.ReleaseAsync(saveFirst: false));
        conn.Dispose();
        SqliteConnection.ClearAllPools();

        // The export holds the user's edit; D is exactly as the other user left it.
        var dest = Path.Combine(_shareDir, "dept (recovered).db");
        var result = await svc.ExportRecoveredCopyAsync(db, dest);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.Equal(UnsavedValue, ReadTestValue(dest));
        Assert.Equal(dBefore, DbFileTestHelpers.HashOf(db));
        Assert.Equal("someone-elses-edit", ReadTestValue(db));

        // The kept changes are still there after a successful export...
        Assert.True(File.Exists(workingPath));
        Assert.True(File.Exists(workingPath + ".dirty"));

        // ...and gone only when the caller discards them.
        svc.DiscardCrash(db);
        Assert.False(File.Exists(workingPath));
        Assert.False(File.Exists(workingPath + ".dirty"));
        Assert.False(File.Exists(workingPath + "-wal"));
        Assert.False(File.Exists(workingPath + "-shm"));
        Assert.Equal(dBefore, DbFileTestHelpers.HashOf(db));
    }

    /// <summary>
    /// A name that is taken: the export is refused (<see cref="NewDatabaseOutcome.AlreadyExists"/>), the existing
    /// file is untouched, and D' with its marker are still there for crash recovery to offer again.
    /// </summary>
    [Fact]
    public async Task SaveACopy_ToAnExistingFile_IsRefused_AndTheKeptChangesStay()
    {
        var (svc, conn, db, _) = await ConflictedSessionAsync();
        var workingPath = svc.WorkingPath;

        await svc.ReleaseAsync(saveFirst: false);
        conn.Dispose();
        SqliteConnection.ClearAllPools();

        var taken = Path.Combine(_shareDir, "taken.db");
        var original = "precious file"u8.ToArray();
        File.WriteAllBytes(taken, original);

        var result = await svc.ExportRecoveredCopyAsync(db, taken);

        Assert.Equal(NewDatabaseOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(original, File.ReadAllBytes(taken));
        Assert.True(File.Exists(workingPath));
        Assert.True(File.Exists(workingPath + ".dirty"));
    }
}
