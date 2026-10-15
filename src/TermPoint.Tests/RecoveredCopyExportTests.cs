using Microsoft.Data.Sqlite;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="CheckoutService.ExportRecoveredCopyAsync"/> — saving the unsaved changes of a crashed
/// session (D') as a separate database after a recovery conflict (spec item 16, step 16a).
///
/// <para>Three temp folders stand in for the three places involved: <c>_shareDir</c> is the shared folder holding D
/// and the exported copy, <c>_workingDir</c> is where D' and its <c>.dirty</c> marker live, and <c>_localDir</c> is
/// the scratch folder the snapshot is built in (so the tests can prove nothing is left behind there).</para>
///
/// <para>What is proved: the export is complete (including changes that live only in D's <c>-wal</c>), is in
/// rollback mode with no sidecars, never overwrites an existing file (D included), leaves D' and its marker alone,
/// and never leaves anything in the scratch folder, whether it succeeds, is refused or fails.</para>
/// </summary>
public sealed class RecoveredCopyExportTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Stands in for the shared folder holding D and the exported copy.</summary>
    private readonly string _shareDir;

    /// <summary>Holds D' and its dirty marker (the checkout service's working folder).</summary>
    private readonly string _workingDir;

    /// <summary>The local scratch folder the snapshot is built in; must be empty after every export.</summary>
    private readonly string _localDir;

    /// <summary>Creates isolated folders for this test instance.</summary>
    public RecoveredCopyExportTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"rce_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"rce_work_{id}");
        _localDir   = Path.Combine(Path.GetTempPath(), $"rce_local_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
        Directory.CreateDirectory(_localDir);
    }

    /// <summary>Removes all three folders (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_localDir,   recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The value the crashed session wrote into D' (its unsaved change).</summary>
    private const string UnsavedValue = "unsaved-edit";

    /// <summary>A path inside the "share" folder.</summary>
    private string SharePath(string name) => Path.Combine(_shareDir, name);

    /// <summary>Creates a checkout service on this fixture's working folder (a stand-in for the next launch).</summary>
    private CheckoutService NewService() => new(
        lockService: new WriteLockService(),
        logger:      new QuietLogger(),
        dispatch:    action => action(),
        workingDir:  _workingDir);

    /// <summary>Exports the recovered copy of <paramref name="db"/> to <paramref name="dest"/> using this fixture's scratch folder.</summary>
    private Task<NewDatabaseResult> ExportAsync(string db, string dest)
        => NewService().ExportRecoveredCopyAsync(db, dest, _localDir);

    /// <summary>
    /// Reproduces the state a recovery conflict is offered in: a session checked D out, made an unsaved change in D'
    /// (with its dirty marker), and crashed (its lock is gone); then a colleague changed D. Returns D's path and D''s
    /// path. Nothing here is opened afterwards, so D' and D are plain files on disk.
    /// </summary>
    private async Task<(string Db, string WorkingPath)> CrashedSessionAsync()
    {
        var db = SharePath("dept.db");
        using (var conn = new SqliteConnection($"Data Source={db};Pooling=False"))
            conn.Open();   // SQLite creates a valid empty database on first open

        var lockSvc = new WriteLockService();
        var svc = new CheckoutService(
            lockService: lockSvc,
            logger:      new QuietLogger(),
            dispatch:    action => action(),
            workingDir:  _workingDir);
        await svc.CheckoutAsync(db);
        InsertTestValue(svc.WorkingPath, UnsavedValue);
        svc.MarkDirty();

        // Crash: no more heartbeats, and the lock file goes (as the next launch's stale-lock takeover would do).
        lockSvc.Suspend();
        File.Delete(Path.ChangeExtension(db, ".lock"));

        // A colleague saves to D while the unsaved change is stranded.
        InsertTestValue(db, "someone-elses-edit");
        return (db, svc.WorkingPath);
    }

    /// <summary>Writes <paramref name="value"/> into a <c>_test</c> table of the database at <paramref name="dbPath"/>.</summary>
    private static void InsertTestValue(string dbPath, string value)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the newest value of the <c>_test</c> table, or null when the table is empty.</summary>
    private static string? ReadTestValue(string dbPath)
        => ScalarOf(dbPath, "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1") as string;

    /// <summary>Runs a single-value query on <paramref name="dbPath"/> (pooling off, so the file is released afterwards).</summary>
    private static object? ScalarOf(string dbPath, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>Executes a statement (or several) on an already-open connection.</summary>
    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads byte 18 of the SQLite header (the file-format WRITE version): 1 means rollback journal, 2 means WAL.
    /// Reads through a shared handle so it works while SQLite has the file open.
    /// </summary>
    private static byte WriteVersionByte(string path)
    {
        var header = new byte[20];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.ReadExactly(header);
        return header[18];
    }

    /// <summary>Reads all bytes of a file through a shared handle.</summary>
    private static byte[] BytesOf(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Asserts the scratch folder holds nothing.</summary>
    private void AssertLocalFolderEmpty() => Assert.Empty(Directory.GetFileSystemEntries(_localDir));

    // ── The export itself ─────────────────────────────────────────────────────

    /// <summary>
    /// The export is created with D''s data, is in rollback mode even though D' is in WAL mode (byte 18 is 1, not 2),
    /// and nothing but D and the export is in the shared folder: no -wal, -shm, -journal or .tmp. The scratch folder
    /// is clean.
    /// </summary>
    [Fact]
    public async Task Export_CreatesRollbackModeCopyWithTheUnsavedData_AndNoSidecars()
    {
        var (db, wp) = await CrashedSessionAsync();

        // D' is a WAL-mode database in production (DatabaseContext opens it that way); put it in WAL mode here too.
        using (var conn = new SqliteConnection($"Data Source={wp};Pooling=False"))
        {
            conn.Open();
            Exec(conn, "PRAGMA journal_mode=WAL");
        }
        Assert.Equal(2, WriteVersionByte(wp));   // precondition: D' really is WAL

        var dest = SharePath("dept (recovered).db");
        var result = await ExportAsync(db, dest);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(UnsavedValue, ReadTestValue(dest));
        Assert.Equal(1, WriteVersionByte(dest));
        Assert.Equal("ok", ScalarOf(dest, "PRAGMA integrity_check"));
        Assert.Equal(
            new[] { db, dest }.OrderBy(p => p, StringComparer.Ordinal),
            Directory.GetFileSystemEntries(_shareDir).OrderBy(p => p, StringComparer.Ordinal));
        AssertLocalFolderEmpty();
    }

    /// <summary>
    /// Changes that exist ONLY in D''s -wal file (committed, never checkpointed) reach the export. A raw copy of the
    /// .db would have missed them, which the test proves first by checking that a plain copy of D''s main file lacks
    /// the table.
    /// </summary>
    [Fact]
    public async Task Export_IncludesChangesThatLiveOnlyInTheWal()
    {
        var (db, wp) = await CrashedSessionAsync();

        // Keep one connection open (pooling off) so the commits stay in the -wal: autocheckpoint is disabled
        // and a checkpoint only happens when the LAST connection closes.
        using var holder = new SqliteConnection($"Data Source={wp};Pooling=False");
        holder.Open();
        Exec(holder, "PRAGMA journal_mode=WAL");
        Exec(holder, "PRAGMA wal_autocheckpoint=0");
        Exec(holder, "CREATE TABLE wal_only(v TEXT); INSERT INTO wal_only VALUES('only-in-the-wal');");
        Assert.True(new FileInfo(wp + "-wal").Length > 0, "precondition: the changes are in the -wal");

        // Proof that a raw copy of the main file would have lost them.
        var rawCopy = Path.Combine(_workingDir, "raw-copy.db");
        File.Copy(wp, rawCopy);
        Assert.Equal(0L, ScalarOf(rawCopy, "SELECT COUNT(*) FROM sqlite_master WHERE name='wal_only'"));

        var dest = SharePath("dept (recovered).db");
        var result = await ExportAsync(db, dest);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.Equal("only-in-the-wal", ScalarOf(dest, "SELECT v FROM wal_only"));
        Assert.Equal(UnsavedValue, ReadTestValue(dest));
        Assert.Equal(1, WriteVersionByte(dest));
        AssertLocalFolderEmpty();
    }

    // ── Never overwrite ───────────────────────────────────────────────────────

    /// <summary>
    /// An existing file at the destination — of any kind — is refused (AlreadyExists), its bytes are unchanged, and
    /// no dest.tmp is left beside it. The scratch folder is clean.
    /// </summary>
    [Fact]
    public async Task Export_ExistingDestination_IsRefusedAndLeftUntouched()
    {
        var (db, _) = await CrashedSessionAsync();
        var dest = SharePath("taken.db");
        var original = "precious file that is not even a database"u8.ToArray();
        File.WriteAllBytes(dest, original);

        var result = await ExportAsync(db, dest);

        Assert.Equal(NewDatabaseOutcome.AlreadyExists, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(original, File.ReadAllBytes(dest));
        Assert.False(File.Exists(dest + ".tmp"));
        Assert.Equal(
            new[] { db, dest }.OrderBy(p => p, StringComparer.Ordinal),
            Directory.GetFileSystemEntries(_shareDir).OrderBy(p => p, StringComparer.Ordinal));
        AssertLocalFolderEmpty();
    }

    /// <summary>
    /// Choosing D itself — the hazard this step closes — is refused like any existing file: D's bytes are unchanged,
    /// so a colleague's save is never erased by the user's stale version.
    /// </summary>
    [Fact]
    public async Task Export_DestinationIsTheSharedDatabase_IsRefusedAndDIsUnchanged()
    {
        var (db, _) = await CrashedSessionAsync();
        var before = BytesOf(db);

        var result = await ExportAsync(db, db);

        Assert.Equal(NewDatabaseOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(before, BytesOf(db));
        Assert.Equal("someone-elses-edit", ReadTestValue(db));
        Assert.False(File.Exists(db + ".tmp"));
        AssertLocalFolderEmpty();
    }

    /// <summary>
    /// When D no longer exists, creating the export at D's own path is allowed (nothing can be lost): it is created
    /// there, complete, with no .tmp left behind.
    /// </summary>
    [Fact]
    public async Task Export_SharedDatabaseMissing_CanBeCreatedAtItsPath()
    {
        var (db, _) = await CrashedSessionAsync();
        File.Delete(db);   // D deleted or renamed while the app was closed

        var result = await ExportAsync(db, db);

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.Equal(UnsavedValue, ReadTestValue(db));
        Assert.False(File.Exists(db + ".tmp"));
        Assert.Equal(new[] { db }, Directory.GetFileSystemEntries(_shareDir));
        AssertLocalFolderEmpty();
    }

    // ── The recovery file is only read ────────────────────────────────────────

    /// <summary>
    /// After a successful export D' still exists and still holds the unsaved data, its .dirty marker is
    /// byte-identical, and D is untouched. (D''s raw bytes are not compared: opening it read-write may fold a WAL in,
    /// which changes bytes but not data.)
    /// </summary>
    [Fact]
    public async Task Export_LeavesRecoveryFileMarkerAndSharedDatabaseAlone()
    {
        var (db, wp) = await CrashedSessionAsync();
        var markerBefore = BytesOf(wp + ".dirty");
        var dBefore = BytesOf(db);

        var result = await ExportAsync(db, SharePath("dept (recovered).db"));

        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
        Assert.True(File.Exists(wp));
        Assert.Equal(UnsavedValue, ReadTestValue(wp));
        Assert.True(File.Exists(wp + ".dirty"));
        Assert.Equal(markerBefore, BytesOf(wp + ".dirty"));
        Assert.Equal(dBefore, BytesOf(db));
        AssertLocalFolderEmpty();
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// With no recovery file there is nothing to export: Failed with a FileNotFoundException, the destination is not
    /// created, and nothing is created in the working or scratch folders either.
    /// </summary>
    [Fact]
    public async Task Export_RecoveryFileMissing_FailsAndCreatesNothing()
    {
        var db = SharePath("dept.db");
        File.WriteAllText(db, "a shared database");   // D exists; D' was never made
        var dest = SharePath("dept (recovered).db");

        var result = await ExportAsync(db, dest);

        Assert.Equal(NewDatabaseOutcome.Failed, result.Outcome);
        Assert.IsType<FileNotFoundException>(result.Error);
        Assert.False(File.Exists(dest));
        Assert.Equal(new[] { db }, Directory.GetFileSystemEntries(_shareDir));
        Assert.Empty(Directory.GetFileSystemEntries(_workingDir));
        AssertLocalFolderEmpty();
    }

    /// <summary>
    /// A recovery file that cannot be read as a database (the snapshot throws) is reported as Failed with the
    /// exception, never thrown; nothing is created at the destination and the scratch folder is cleaned up even
    /// though the snapshot file had been started.
    /// </summary>
    [Fact]
    public async Task Export_RecoveryFileUnreadable_FailsAndLeavesNothingBehind()
    {
        var (db, wp) = await CrashedSessionAsync();
        File.WriteAllText(wp, "this is not a sqlite database, just text that is long enough to be read as a header");
        var dest = SharePath("dept (recovered).db");

        var result = await ExportAsync(db, dest);

        Assert.Equal(NewDatabaseOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".tmp"));
        Assert.Equal(new[] { db }, Directory.GetFileSystemEntries(_shareDir));
        Assert.True(File.Exists(wp), "D' is never deleted by the export, even when it cannot be read");
        AssertLocalFolderEmpty();
    }

    // ── Logger ────────────────────────────────────────────────────────────────

    /// <summary>No-op logger so the tests have no file-system side effects from logging.</summary>
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
}
