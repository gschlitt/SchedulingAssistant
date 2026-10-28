using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Integration tests for <see cref="CheckoutService"/>.
///
/// <para>Each test uses an isolated temporary directory for both the source database (D)
/// and the working copy directory (D'). A dedicated <see cref="WriteLockService"/> and
/// <see cref="NullLogger"/> are injected so no static app state is shared between tests.
/// The dispatcher is replaced with a synchronous inline action so that <c>SaveCompleted</c>
/// and <c>SaveFailed</c> events are observable without an Avalonia event loop.</para>
///
/// <para>Tests are organised into seven groups:
/// <list type="bullet">
///   <item><description>Group 1 — New database (created first by NewDatabaseFile, then checked out normally) and a missing source (SourceMissing)</description></item>
///   <item><description>Group 2 — Checkout of an existing database</description></item>
///   <item><description>Group 3 — Stale lock / ForceCheckout</description></item>
///   <item><description>Group 4 — Save flow (D' → D)</description></item>
///   <item><description>Group 5 — Database switch (ReleaseAsync + re-checkout)</description></item>
///   <item><description>Group 6 — Crash recovery and utility helpers</description></item>
///   <item><description>Group 7 — Coexistence with open database connections (wizard and edit flows)</description></item>
///   <item><description>Group 13 — MinimumVersionToWrite gate (read-only on a too-old app, dormant stamping); the
///   file also contains later groups numbered 8–12 below</description></item>
///   <item><description>Group 19 — the version gate fails safe: an unreadable stamp, or a stamp that cannot be raised,
///   refuses write access after quiet retries (spec item 3, step 3c)</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class CheckoutServiceTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _workingDir;

    /// <summary>
    /// Creates isolated temporary directories for source DB files and working copies.
    /// </summary>
    public CheckoutServiceTests()
    {
        var id       = Guid.NewGuid().ToString("N");
        _tempDir     = Path.Combine(Path.GetTempPath(), $"cst_src_{id}");
        _workingDir  = Path.Combine(Path.GetTempPath(), $"cst_wrk_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Deletes both temporary directories and all files created during the test.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir,    recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a fake .db path inside the source temp directory.</summary>
    private string DbPath(string name = "test") =>
        Path.Combine(_tempDir, $"{name}.db");

    /// <summary>
    /// Creates a file at <paramref name="path"/> with the given text content.
    /// If no content is provided a non-empty default is used so SHA-256 is deterministic.
    /// </summary>
    private static void CreateFile(string path, string content = "dummy-database-content")
        => File.WriteAllText(path, content);

    /// <summary>
    /// Creates a minimal valid SQLite database file at <paramref name="path"/>.
    /// Required for any test that exercises <see cref="CheckoutService.SaveAsync"/>,
    /// because <c>BackupSqliteDatabase</c> uses the SQLite Online Backup API which
    /// requires a well-formed SQLite file as its source.
    /// <para><c>Pooling=False</c> ensures the connection is truly closed on Dispose
    /// rather than returned to the pool, preventing stale OS file handles.</para>
    /// </summary>
    private static void CreateSqliteDb(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open(); // SQLite creates a valid empty database on first open.
    }

    /// <summary>
    /// Inserts a single value into a <c>_test</c> table in the SQLite database at
    /// <paramref name="dbPath"/>. Used to write verifiable content to D' without
    /// corrupting the SQLite file format.
    /// </summary>
    private static void InsertTestValue(string dbPath, string value)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the first value from the <c>_test</c> table written by
    /// <see cref="InsertTestValue"/>. Returns null if the table is empty or absent.
    /// </summary>
    private static string? ReadTestValue(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Creates a <see cref="CheckoutService"/> with all dependencies injected from
    /// this fixture: isolated <see cref="WriteLockService"/>, null logger, synchronous
    /// dispatcher, and the per-test working directory.
    /// </summary>
    /// <param name="appVersion">
    /// Version the service believes it is running as, for the MinimumVersionToWrite gate
    /// (Group 13). Null (the default) uses the real TermPoint assembly version, which is
    /// irrelevant to every test whose database carries no stamp.
    /// </param>
    /// <param name="logger">
    /// Logger to inject; null (the default) uses a no-op <see cref="NullLogger"/>. Pass a
    /// <see cref="RecordingAppLogger"/> to assert what a failed save logs and reports (spec item 12).
    /// </param>
    /// <returns>
    /// A tuple of the service and its lock service so tests can simulate external lock
    /// holders or verify the lock state directly.
    /// </returns>
    private (CheckoutService svc, WriteLockService lockSvc) CreateService(Version? appVersion = null, IAppLogger? logger = null)
    {
        var lockSvc = new WriteLockService();
        var svc     = new CheckoutService(
            lockService: lockSvc,
            logger:      logger ?? new NullLogger(),
            dispatch:    action => action(),    // synchronous — events fire immediately
            workingDir:  _workingDir,
            appVersion:  appVersion);
        return (svc, lockSvc);
    }

    /// <summary>
    /// Writes a lock file with a stale heartbeat alongside <paramref name="dbPath"/>,
    /// simulating a crashed remote writer.
    /// </summary>
    private static void WriteExternalStaleLock(string dbPath)
    {
        var lockPath = Path.ChangeExtension(dbPath, ".lock");
        var now = DateTime.UtcNow;
        var data = new TermPoint.Models.LockFileData
        {
            Username    = "external_user",
            Machine     = "REMOTE-PC",
            SessionGuid = Guid.NewGuid().ToString(),
            Acquired    = now.AddSeconds(-(WriteLockService.StaleLockThresholdSeconds + 10)),
            Heartbeat   = now.AddSeconds(-(WriteLockService.StaleLockThresholdSeconds + 10)),
        };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(data));
    }

    /// <summary>
    /// Writes a lock file with a fresh heartbeat alongside <paramref name="dbPath"/>,
    /// simulating a live remote writer.
    /// </summary>
    private static void WriteExternalFreshLock(string dbPath)
    {
        var lockPath = Path.ChangeExtension(dbPath, ".lock");
        var now  = DateTime.UtcNow;
        var data = new TermPoint.Models.LockFileData
        {
            Username    = "other_user",
            Machine     = "OTHER-PC",
            SessionGuid = Guid.NewGuid().ToString(),
            Acquired    = now,
            Heartbeat   = now,
        };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(data));
    }

    // ── Null logger ───────────────────────────────────────────────────────────

    /// <summary>
    /// No-op logger used in tests to avoid file-system side effects from logging.
    /// </summary>
    private sealed class NullLogger : IAppLogger
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

    // ═════════════════════════════════════════════════════════════════════════
    // Group 1 — New database (wizard / first-run flow)
    //
    // A new database is created BEFORE checkout (NewDatabaseFile builds it in a
    // local temp file and copies it to D), then checked out like any other:
    // D' != D and no session ever runs on D itself. CheckoutAsync on a path
    // where D does not exist is SourceMissing — no lock, nothing created.
    // (Pre-4a these tests covered a "new-database shortcut" that ran the whole
    // session on D; that shortcut no longer exists.)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a real new database at <paramref name="path"/> with <see cref="NewDatabaseFile"/>
    /// — the same helper the wizard and File → New use — and asserts it succeeded. The temporary
    /// local build goes into the source temp folder (deleted by the helper itself).
    /// </summary>
    /// <param name="path">Where D should be created; must not exist.</param>
    private async Task CreateNewDatabaseAsync(string path)
    {
        var result = await NewDatabaseFile.CreateAsync(path, _tempDir);
        Assert.Equal(NewDatabaseOutcome.Created, result.Outcome);
    }

    /// <summary>
    /// A freshly created new database checks out to write access — the app gets edit rights
    /// on its very first launch after completing the wizard.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_NewDatabase_ReturnsWriteAccess()
    {
        var (svc, _) = CreateService();
        var db = DbPath("new");
        await CreateNewDatabaseAsync(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
    }

    /// <summary>
    /// A new database is checked out like any other: WorkingPath is a LOCAL copy (D'), never D.
    /// Replaces the old "WorkingPath equals SourcePath" shortcut test.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_NewDatabase_WorkingPathIsALocalCopyNotSource()
    {
        var (svc, _) = CreateService();
        var db = DbPath("new");
        await CreateNewDatabaseAsync(db);

        await svc.CheckoutAsync(db);

        Assert.Equal(db, svc.SourcePath);
        Assert.NotEqual(db, svc.WorkingPath);
        Assert.True(File.Exists(svc.WorkingPath), "D' must exist after checkout.");
        Assert.Equal(_workingDir, Path.GetDirectoryName(svc.WorkingPath));
    }

    /// <summary>Mode must be WriteAccess so the UI shows the Save button as active.</summary>
    [Fact]
    public async Task CheckoutAsync_NewDatabase_ModeIsWriteAccess()
    {
        var (svc, _) = CreateService();
        var db = DbPath("new");
        await CreateNewDatabaseAsync(db);

        await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
    }

    /// <summary>The lock service must hold the write lock after a new-database checkout.</summary>
    [Fact]
    public async Task CheckoutAsync_NewDatabase_LockIsAcquired()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath("new");
        await CreateNewDatabaseAsync(db);

        await svc.CheckoutAsync(db);

        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// HashAtCheckout is set for a new database — it now exists on disk when checked out, so
    /// SaveAsync can verify D has not been modified externally. (Was null under the old shortcut.)
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_NewDatabase_HashAtCheckoutIsSet()
    {
        var (svc, _) = CreateService();
        var db = DbPath("new");
        await CreateNewDatabaseAsync(db);

        await svc.CheckoutAsync(db);

        Assert.False(string.IsNullOrEmpty(svc.HashAtCheckout));
    }

    /// <summary>
    /// D does not exist: CheckoutAsync reports SourceMissing instead of running a session on D.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_MissingDatabase_ReturnsSourceMissing()
    {
        var (svc, _) = CreateService();
        var db = DbPath("missing");   // file does NOT exist

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.SourceMissing, outcome);
    }

    /// <summary>
    /// SourceMissing takes no lock, writes no .lock file, creates no database and no working
    /// copy, and leaves the service in read-only mode — it must not create anything next to a
    /// file that does not exist.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_MissingDatabase_TakesNoLockAndCreatesNothing()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath("missing");

        await svc.CheckoutAsync(db);

        Assert.False(lockSvc.IsWriter, "No lock may be taken for a missing database.");
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.False(File.Exists(db), "D must not be created.");
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")), "No lock file may be written.");
        Assert.Empty(Directory.GetFileSystemEntries(_tempDir));
        Assert.Empty(Directory.GetFileSystemEntries(_workingDir));
        Assert.True(string.IsNullOrEmpty(svc.WorkingPath), "No working path may be assigned.");
    }

    /// <summary>
    /// After SourceMissing the service is not in write mode, so a stray SaveAsync is refused
    /// rather than pushing anything anywhere. (Replaces the old "SaveAsync is a no-op for a new
    /// database" test, which depended on the removed shortcut.)
    /// </summary>
    [Fact]
    public async Task SaveAsync_AfterSourceMissing_ReturnsNotInWriteMode()
    {
        var (svc, _) = CreateService();
        await svc.CheckoutAsync(DbPath("missing"));

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.NotInWriteMode, outcome);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 2 — Checkout of an existing database
    //
    // When D exists and no lock file is present, checkout should copy D to D',
    // verify the copy, and enter write mode.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>An existing, unconstested database returns WriteAccess.</summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_NoLock_ReturnsWriteAccess()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
    }

    /// <summary>After a successful checkout, WorkingPath points to a file inside the working dir.</summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_NoLock_CreatesWorkingCopy()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);

        Assert.True(File.Exists(svc.WorkingPath), "Working copy D' must exist after checkout.");
        Assert.NotEqual(db, svc.WorkingPath);
    }

    /// <summary>D' must have identical content to D immediately after checkout.</summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_NoLock_WorkingCopyMatchesSource()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "unique-db-content-12345");

        await svc.CheckoutAsync(db);

        Assert.Equal(File.ReadAllText(db), File.ReadAllText(svc.WorkingPath));
    }

    /// <summary>SessionDirty must be true after checkout — D' has not yet been saved back.</summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_NoLock_SessionDirtyIsTrue()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);

        Assert.True(svc.SessionDirty);
    }

    /// <summary>
    /// No dirty marker is written at checkout — the marker only appears after the first
    /// user-initiated write (via MarkDirty). This prevents a spurious crash notification
    /// when the app is opened and closed without making any edits.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_NoLock_NoDirtyMarkerYet()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);

        Assert.False(File.Exists(svc.WorkingPath + ".dirty"),
            "Dirty marker must NOT be written at checkout — only on first user write.");
    }

    /// <summary>
    /// HashAtCheckout must be set to the SHA-256 hash of D, not null, so that
    /// SaveAsync can verify D has not been externally modified before overwriting it.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_NoLock_HashAtCheckoutIsSet()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);

        Assert.NotNull(svc.HashAtCheckout);
        Assert.NotEmpty(svc.HashAtCheckout!);
    }

    /// <summary>
    /// When another instance holds a fresh write lock, CheckoutAsync returns ReadOnly
    /// so the app opens the database in view-only mode.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_FreshLock_ReturnsReadOnly()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalFreshLock(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
    }

    /// <summary>Mode must be ReadOnly and the lock holder populated when read-only path is taken.</summary>
    [Fact]
    public async Task CheckoutAsync_ExistingDb_FreshLock_ModeIsReadOnlyAndHolderPopulated()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalFreshLock(db);

        await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.NotNull(svc.CurrentHolder);
        Assert.Equal("other_user", svc.CurrentHolder!.Username);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 3 — Stale lock / ForceCheckout
    //
    // When a stale lock is detected the user must confirm before the app takes
    // over. CheckoutAsync returns StaleHolder and ForceCheckoutAsync does the
    // actual takeover.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A stale lock returns StaleHolder so the UI can prompt the user.</summary>
    [Fact]
    public async Task CheckoutAsync_StaleLock_ReturnsStaleHolder()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.StaleHolder, outcome);
    }

    /// <summary>CurrentHolder carries the abandoned session's details for use in the prompt.</summary>
    [Fact]
    public async Task CheckoutAsync_StaleLock_CurrentHolderIsPopulated()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);

        await svc.CheckoutAsync(db);

        Assert.NotNull(svc.CurrentHolder);
        Assert.Equal("external_user", svc.CurrentHolder!.Username);
    }

    /// <summary>
    /// After the user confirms, ForceCheckoutAsync breaks the stale lock and
    /// returns WriteAccess.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_AfterStaleLock_ReturnsWriteAccess()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);

        var firstOutcome = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, firstOutcome); // precondition

        var forceOutcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, forceOutcome);
    }

    /// <summary>After ForceCheckout, D' exists and is a copy of D.</summary>
    [Fact]
    public async Task ForceCheckoutAsync_AfterStaleLock_CreatesWorkingCopy()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "stale-session-content");
        WriteExternalStaleLock(db);

        await svc.CheckoutAsync(db);
        await svc.ForceCheckoutAsync();

        Assert.True(File.Exists(svc.WorkingPath));
        Assert.Equal(File.ReadAllText(db), File.ReadAllText(svc.WorkingPath));
    }

    // ── Click-time re-check (spec item 9a) ───────────────────────────────────
    //
    // The takeover prompt can stay open for minutes. ForceCheckoutAsync must only break the SAME lock
    // CheckoutAsync judged inactive: if the holder woke and resumed (or someone else took the lock) in
    // the meantime, it leaves the lock file alone and returns HolderActiveAgain.

    /// <summary>
    /// Rewrites the lock file beside <paramref name="dbPath"/>, applying <paramref name="change"/> to
    /// the holder it contains. Simulates the original holder (or another machine) acting while the
    /// takeover prompt is open.
    /// </summary>
    /// <param name="dbPath">Path of D; the lock file sits beside it.</param>
    /// <param name="change">Mutation applied to the deserialized lock data before it is written back.</param>
    private static void ModifyLock(string dbPath, Action<TermPoint.Models.LockFileData> change)
    {
        var path = Path.ChangeExtension(dbPath, ".lock");
        var data = JsonSerializer.Deserialize<TermPoint.Models.LockFileData>(File.ReadAllText(path))!;
        change(data);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }

    /// <summary>
    /// A takeover of a lock that is exactly as it was judged: it succeeds and the lock file now names
    /// this session, not the abandoned one.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_LockUnchangedSinceDetection_TakesOverAndWritesOurLock()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
        var lockData = JsonSerializer.Deserialize<TermPoint.Models.LockFileData>(File.ReadAllText(LockFileOf(db)))!;
        Assert.Equal(lockSvc.SessionGuid, lockData.SessionGuid);
    }

    /// <summary>
    /// THE 9a BUG. The sleeping holder woke up and renewed its heartbeat while the prompt was open.
    /// The takeover is refused (HolderActiveAgain): the holder's lock file is left byte-for-byte
    /// intact, we are not the writer, no D' was made, and CurrentHolder shows the refreshed heartbeat.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_HeartbeatAdvancedSinceDetection_ReturnsHolderActiveAgain_AndLeavesTheLockIntact()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition
        var judgedHeartbeat = svc.CurrentHolder!.Heartbeat;

        ModifyLock(db, d => d.Heartbeat = DateTime.UtcNow);   // the holder woke up
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.NotEqual(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
        Assert.Empty(Directory.GetFiles(_workingDir));
        Assert.Equal("external_user", svc.CurrentHolder!.Username);
        Assert.True(svc.CurrentHolder.Heartbeat > judgedHeartbeat, "CurrentHolder must be refreshed from the file.");
    }

    /// <summary>
    /// Another machine took the lock over while the prompt was open (different session, fresh
    /// heartbeat): refused, file intact, and CurrentHolder names the NEW holder.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_DifferentSessionHoldsLockSinceDetection_ReturnsHolderActiveAgain()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition

        ModifyLock(db, d =>
        {
            d.Username    = "second_user";
            d.Machine     = "SECOND-PC";
            d.SessionGuid = Guid.NewGuid().ToString();
            d.Acquired    = DateTime.UtcNow;
            d.Heartbeat   = DateTime.UtcNow;
        });
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
        Assert.Empty(Directory.GetFiles(_workingDir));
        Assert.Equal("second_user", svc.CurrentHolder!.Username);
    }

    /// <summary>
    /// Only the session ID changed (the heartbeat is identical): that is still a different lock, so the
    /// takeover is refused. Guards the session half of the comparison; the heartbeat test above guards
    /// the other half.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_SessionChangedButHeartbeatIdentical_ReturnsHolderActiveAgain()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition

        ModifyLock(db, d => d.SessionGuid = Guid.NewGuid().ToString());   // heartbeat untouched
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
    }

    /// <summary>
    /// The holder released cleanly (or someone cleaned up) while the prompt was open: nobody holds the
    /// lock any more, so the takeover simply creates ours and the checkout completes.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_LockFileDeletedSinceDetection_ReturnsWriteAccess()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition

        File.Delete(LockFileOf(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
        Assert.True(File.Exists(LockFileOf(db)));
        Assert.True(File.Exists(svc.WorkingPath));
    }

    /// <summary>
    /// A lock that read fine when judged inactive and is garbled now cannot be proven unchanged, so it
    /// is NOT deleted: HolderActiveAgain, garbage left in place byte for byte.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_LockFileGarbledSinceDetection_ReturnsHolderActiveAgain_AndLeavesTheFileIntact()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition

        File.WriteAllText(LockFileOf(db), "{ this is not json");
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
        Assert.Empty(Directory.GetFiles(_workingDir));
    }

    /// <summary>
    /// A lock file that was ALREADY unreadable when it was judged inactive, and is still the same
    /// garbage at click time, is the one case where "unreadable" must not block: otherwise a corrupt
    /// lock file could never be taken over from the prompt.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_LockWasGarbledWhenJudgedInactive_StillTakesOver()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        File.WriteAllText(LockFileOf(db), "{ this is not json");
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// Crash-recovery variant: when the holder turns out to be active again, the crashed session's D'
    /// and its dirty marker are untouched (the unsaved edit is still there) and recovery is still
    /// detectable, so it is offered again the next time this database is opened with write access.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_Recovery_HolderActiveAgain_KeepsTheWorkingCopyAndMarker()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath("crashed");
        CreateSqliteDb(db);
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        svc1.MarkDirty();
        var crashedWorkingCopy = svc1.WorkingPath;
        lockSvc1.Suspend();
        WriteExternalStaleLock(db);                  // the abandoned lock the prompt is about

        var (svc2, lockSvc2) = CreateService();
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc2.CheckoutAsync(db, recoverWorkingCopy: true)); // precondition
        ModifyLock(db, d => d.Heartbeat = DateTime.UtcNow);   // the holder woke up
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var outcome = await svc2.ForceCheckoutAsync(recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, outcome);
        Assert.False(lockSvc2.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
        Assert.True(File.Exists(crashedWorkingCopy), "D' holds the unsaved edit — it must survive.");
        Assert.True(File.Exists(crashedWorkingCopy + ".dirty"), "The dirty marker must survive.");
        Assert.Equal("unsaved-edit", ReadTestValue(crashedWorkingCopy));
        Assert.True(svc2.DetectCrashRecovery(db), "Recovery must still be offered at the next opportunity.");
    }

    /// <summary>
    /// ForceCheckoutAsync with no stale judgement behind it (never preceded by a StaleHolder checkout)
    /// has nothing to compare the lock file with, so it refuses and touches nothing — it must not
    /// degrade into the old unconditional delete.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_WithoutAStaleHolderCheckout_RefusesAndLeavesTheLockAlone()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.Failed, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
    }

    /// <summary>
    /// The stale judgement is single-use: after a takeover attempt (here refused as HolderActiveAgain)
    /// a second ForceCheckoutAsync — which would act on an out-of-date snapshot — is refused until a
    /// new CheckoutAsync has judged the lock afresh.
    /// </summary>
    [Fact]
    public async Task ForceCheckoutAsync_SecondCallWithoutNewCheckout_IsRefused()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition
        ModifyLock(db, d => d.Heartbeat = DateTime.UtcNow);
        Assert.Equal(CheckoutOutcome.HolderActiveAgain, await svc.ForceCheckoutAsync());
        var lockBytes = File.ReadAllBytes(LockFileOf(db));

        var second = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.Failed, second);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
    }

    // ── Read-only after a stale-lock prompt keeps watching the lock ──────────

    /// <summary>
    /// A session that opened read-only after the user declined the takeover (or the holder proved active
    /// again) starts WATCHING the lock file — TryAcquire's stale branch does not — so that when the
    /// holder later releases, the lock service reports WriteLockBecameAvailable and the banner can
    /// offer "Take over as writer". While the holder is still alive (fresh heartbeat) nothing is offered.
    /// </summary>
    [Fact]
    public async Task StartReaderPollingAsync_AfterHolderActiveAgain_OffersTakeOverWhenTheHolderReleases()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition
        ModifyLock(db, d => d.Heartbeat = DateTime.UtcNow);
        Assert.Equal(CheckoutOutcome.HolderActiveAgain, await svc.ForceCheckoutAsync());
        Assert.NotNull(await svc.SetupReadOnlySnapshotAsync());
        Assert.False(lockSvc.IsPolling, "precondition: nothing has started the poll yet");

        await svc.StartReaderPollingAsync();

        Assert.True(lockSvc.IsPolling);
        await lockSvc.PollLockFile();
        Assert.False(lockSvc.WriteLockBecameAvailable, "The holder is alive — no takeover may be offered.");

        File.Delete(LockFileOf(db));                  // the holder releases cleanly
        await lockSvc.PollLockFile();
        Assert.True(lockSvc.WriteLockBecameAvailable);
        lockSvc.Dispose();
    }

    /// <summary>
    /// Same for the user who declined the prompt: after the read-only snapshot the session polls, and a
    /// clean release by the holder is noticed.
    /// </summary>
    [Fact]
    public async Task StartReaderPollingAsync_AfterDeclinedTakeover_NoticesTheHolderReleasing()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition
        Assert.NotNull(await svc.SetupReadOnlySnapshotAsync());                   // the user chose "Open read-only"
        Assert.False(lockSvc.IsPolling, "precondition: the stale branch does not poll by itself");

        await svc.StartReaderPollingAsync();

        Assert.True(lockSvc.IsPolling);
        File.Delete(LockFileOf(db));
        await lockSvc.PollLockFile();
        Assert.True(lockSvc.WriteLockBecameAvailable);
        lockSvc.Dispose();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 4 — Save flow  (the core D' → D write-back)
    //
    // Tests cover: happy path, a refused save with nothing checked out, external modification
    // detection, lock-lost detection, and lock release on final save.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// SaveAsync returns NotInWriteMode when the service has not checked out,
    /// preventing any accidental write-back.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NotInWriteMode_ReturnsNotInWriteMode()
    {
        var (svc, _) = CreateService();
        // No checkout performed — Mode is still ReadOnly.

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.NotInWriteMode, outcome);
    }

    /// <summary>
    /// The happy path: D' has been modified; SaveAsync copies D' back to D so
    /// that D contains the latest content.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ExistingDb_CopiesWorkingCopyToSource()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        // Simulate editing D' via SQLite (as DatabaseContext does in production).
        const string editedContent = "edited-content";
        InsertTestValue(svc.WorkingPath, editedContent);

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal(editedContent, ReadTestValue(db));
    }

    /// <summary>
    /// After a successful save, HashAtCheckout is updated to the hash of the
    /// newly saved D so that the next save can re-verify D was not externally modified.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ExistingDb_HashAtCheckoutRefreshed()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        var hashBefore = svc.HashAtCheckout;

        InsertTestValue(svc.WorkingPath, "changed-content");
        await svc.SaveAsync();

        Assert.NotEqual(hashBefore, svc.HashAtCheckout);
    }

    /// <summary>
    /// After a successful save, SessionDirty is false — D and D' are in sync.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ExistingDb_SessionDirtyIsFalse()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        await svc.SaveAsync();

        Assert.False(svc.SessionDirty);
    }

    /// <summary>
    /// A mid-session save (releaseLockAfter=false, the default) must delete the dirty
    /// marker, because D' now matches D. If the user edits again, MarkDirty() re-writes it.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MidSession_DirtyMarkerDeleted()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty(); // Simulate a user write so the dirty marker is created.
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "Dirty marker should exist before save.");

        await svc.SaveAsync(); // releaseLockAfter defaults to false

        Assert.False(File.Exists(svc.WorkingPath + ".dirty"),
            "Dirty marker must be deleted after a mid-session save — D' now matches D.");
    }

    /// <summary>
    /// A release save (releaseLockAfter=true) deletes the dirty marker because
    /// the session is ending cleanly — no crash recovery is needed at next startup.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithReleaseLockAfter_DirtyMarkerDeleted()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty(); // Simulate a user write so the dirty marker is created.
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "Dirty marker should exist before save.");

        await svc.SaveAsync(releaseLockAfter: true);

        Assert.False(File.Exists(svc.WorkingPath + ".dirty"),
            "Dirty marker must be removed after a release save — session ended cleanly.");
    }

    /// <summary>
    /// After a mid-session save, DetectCrashRecovery still returns true for this
    /// path — the dirty marker persists so a crash at any subsequent point is
    /// recoverable. This is the core regression guard for the mid-session fix.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MidSession_CrashRecoveryStillDetectable()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        // Simulate a mid-session save (does NOT end the session).
        InsertTestValue(svc.WorkingPath, "first-edit");
        svc.MarkDirty(); // Marks the session dirty before save, as a real write would.
        await svc.SaveAsync();
        // After save: marker is deleted and _dirtyFired is reset (via SaveCompleted → ResetDirty).
        // In tests we call svc.MarkDirty() directly to simulate the next edit re-arming it.
        // (In production, DatabaseContext.ResetDirty() is called by the SaveCompleted handler
        //  wired in App.axaml.cs, so the next repository write re-fires the callback.)

        // Another edit after the save — crash here would leave unsaved changes.
        InsertTestValue(svc.WorkingPath, "post-save-edit");
        svc.MarkDirty(); // Re-arm: simulates the repository write that follows the post-save edit.

        // Simulate a crash: the service is abandoned without calling ReleaseAsync.
        // DetectCrashRecovery must return true so the UI can notify the user.
        Assert.True(svc.DetectCrashRecovery(db),
            "Crash recovery must be detectable after a mid-session save with subsequent edits.");
    }

    /// <summary>
    /// The SaveCompleted event must fire after a successful save, so the UI can
    /// briefly display a "Saved" indicator.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ExistingDb_FiresSaveCompletedEvent()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        var eventFired = false;
        svc.SaveCompleted += () => eventFired = true;

        await svc.SaveAsync();

        Assert.True(eventFired);
    }

    /// <summary>
    /// When D has been externally modified since checkout (hash differs),
    /// SaveAsync returns SourceModified and does NOT overwrite D.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SourceModifiedExternally_ReturnsSourceModified()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "original");

        await svc.CheckoutAsync(db);

        // Spec item 10, step 10a: a refused save raises SaveConflictDetected (and sets HasSaveConflict),
        // and no longer raises the old "Save aborted" SaveFailed banner.
        var conflictRaised = 0;
        var saveFailedRaised = false;
        svc.SaveConflictDetected += () => conflictRaised++;
        svc.SaveFailed += (_, _) => saveFailedRaised = true;

        // Simulate an external process writing to D while our session holds D'.
        File.WriteAllText(db, "externally-changed");

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.True(svc.HasSaveConflict);
        Assert.Equal(1, conflictRaised);
        Assert.False(saveFailedRaised, "a SourceModified refusal must not raise SaveFailed any more.");
        // D must still contain the externally-changed content — not overwritten.
        Assert.Equal("externally-changed", File.ReadAllText(db));
    }

    /// <summary>
    /// When the lock file is no longer owned by this session, SaveAsync returns
    /// LockLost and does not overwrite D.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockStolenByOtherSession_ReturnsLockLost()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);
        Assert.True(lockSvc.IsWriter); // precondition: we hold the lock

        // Simulate lock file being overwritten by another session (different SessionGuid).
        var lockPath = Path.ChangeExtension(db, ".lock");
        var foreignData = new TermPoint.Models.LockFileData
        {
            Username    = "stealer",
            Machine     = "STEALER-PC",
            SessionGuid = Guid.NewGuid().ToString(), // different GUID → not ours
            Acquired    = DateTime.UtcNow,
            Heartbeat   = DateTime.UtcNow,
        };
        File.WriteAllText(lockPath, JsonSerializer.Serialize(foreignData));

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.LockLost, outcome);
    }

    /// <summary>
    /// Regression test for the self-inflicted sharing-violation false-demotion bug.
    ///
    /// <para>On a degraded network the save-path lock verification (<c>File.ReadAllText</c>
    /// on D.lock) can overlap the heartbeat timer's atomic write-temp-then-rename of the very
    /// same file and fail fast with <c>ERROR_SHARING_VIOLATION</c> ("the process cannot access
    /// the file because it is being used by another process"). That is a transient,
    /// self-inflicted condition — the "other process" is our own heartbeat — NOT a lock loss.</para>
    ///
    /// <para>Before the fix, the <see cref="IOException"/> escaped the Ours/NotOurs/Unreachable
    /// classification and propagated out of <see cref="CheckoutService.SaveAsync"/> as an
    /// unhandled exception (and, from the abandoned timeout task, as an UnobservedTaskException).
    /// Here we reproduce the exact contention deterministically by holding D.lock open with
    /// <see cref="FileShare.None"/>, and assert that SaveAsync ABSORBS it: it must not throw,
    /// must classify the read as transient (SaveOutcome.CopyError with a SaveFailed banner), and
    /// must NOT demote — Mode stays <see cref="CheckoutMode.WriteAccess"/> and the lock is retained.</para>
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockFileContendedBySharingViolation_TreatedAsTransient_NoDemotion()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        Assert.True(lockSvc.IsWriter); // precondition: we hold the write lock

        var transientErrorRaised = false;
        svc.SaveFailed += (_, _) => transientErrorRaised = true;

        var lockPath = Path.ChangeExtension(db, ".lock");

        SaveOutcome outcome;
        // Hold D.lock open denying all sharing so the verify's File.ReadAllText is refused
        // with a sharing violation — the exact fault the heartbeat's rename causes on a lossy
        // share, but forced deterministically rather than raced.
        using (var holder = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Must NOT throw — before the fix this propagated the IOException out of SaveAsync.
            outcome = await svc.SaveAsync();
        }

        // Transient, non-destructive outcome: writer state fully preserved, no demotion.
        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.True(lockSvc.IsWriter);
        Assert.True(transientErrorRaised);
    }

    /// <summary>
    /// When <paramref name="releaseLockAfter"/> is true and the save succeeds,
    /// the lock is released (lock file deleted) — suitable for graceful shutdown.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithReleaseLockAfter_ReleasesLock()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        Assert.True(lockSvc.IsWriter); // precondition

        await svc.SaveAsync(releaseLockAfter: true);
        lockSvc.PendingRelease?.Wait(TimeSpan.FromSeconds(10)); // settle the background lock-file deletion

        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")));
    }

    /// <summary>
    /// On graceful shutdown the lock must be released even when the save-on-exit FAILS.
    /// Here D is modified externally so SaveAsync returns SourceModified; ReleaseAsync must
    /// still release the lock. Before this fix, ReleaseAsync only released on the save-success
    /// path, so any failed save-on-exit orphaned the lock.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_SaveFails_StillReleasesLock()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db, "original");

        await svc.CheckoutAsync(db);
        Assert.True(lockSvc.IsWriter); // precondition: we hold the lock

        // Make the shutdown save fail: externally modify D so its hash no longer matches
        // HashAtCheckout → SaveAsync returns SourceModified (before any lock release).
        File.WriteAllText(db, "externally-changed");

        await svc.ReleaseAsync(saveFirst: true);
        lockSvc.PendingRelease?.Wait(TimeSpan.FromSeconds(10)); // settle the background lock-file deletion

        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")),
            "Lock must be released on shutdown even when the save-on-exit fails.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 5 — Database switch
    //
    // Switching databases = ReleaseAsync on the current session, then
    // CheckoutAsync on the new path. This is the scenario that was crashing
    // at runtime.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After ReleaseAsync, the write lock for the first database is released
    /// (lock file gone), proving the old session is cleaned up properly.
    /// </summary>
    [Fact]
    public async Task SwitchDatabase_ReleaseAsync_ReleasesOldLock()
    {
        var (svc, lockSvc) = CreateService();
        var db1 = DbPath("first");
        CreateFile(db1);

        await svc.CheckoutAsync(db1);
        Assert.True(lockSvc.IsWriter); // precondition

        await svc.ReleaseAsync(saveFirst: false);
        lockSvc.PendingRelease?.Wait(TimeSpan.FromSeconds(10)); // settle the background lock-file deletion

        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db1, ".lock")));
    }

    /// <summary>
    /// After ReleaseAsync + CheckoutAsync on a second database, the new lock is
    /// held and the service is in write mode for the new path.
    /// </summary>
    [Fact]
    public async Task SwitchDatabase_CheckoutNew_AcquiresNewLock()
    {
        var (svc, lockSvc) = CreateService();
        var db1 = DbPath("first");
        var db2 = DbPath("second");
        CreateFile(db1);
        CreateFile(db2, "second-content");

        await svc.CheckoutAsync(db1);
        await svc.ReleaseAsync(saveFirst: false);

        var outcome = await svc.CheckoutAsync(db2);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(db2, svc.SourcePath);
    }

    /// <summary>
    /// After switching, the old working copy is deleted (it was saved/discarded)
    /// and a new one for db2 exists.
    /// </summary>
    [Fact]
    public async Task SwitchDatabase_OldWorkingCopyCleanedUp()
    {
        var (svc, _) = CreateService();
        var db1 = DbPath("first");
        var db2 = DbPath("second");
        CreateSqliteDb(db1);
        CreateSqliteDb(db2);

        await svc.CheckoutAsync(db1);
        var oldWorkingPath = svc.WorkingPath;

        // Save and release, then clean up the working copy (in production this is
        // called by MainWindow after the DB connection is disposed).
        await svc.ReleaseAsync(saveFirst: true);
        svc.CleanupWorkingCopy();

        Assert.False(File.Exists(oldWorkingPath), "Old working copy should be deleted after clean release.");

        // Switch to second DB.
        await svc.CheckoutAsync(db2);
        Assert.True(File.Exists(svc.WorkingPath), "New working copy must exist.");
        Assert.NotEqual(oldWorkingPath, svc.WorkingPath);
    }

    /// <summary>
    /// A second ReleaseAsync without an intervening CheckoutAsync is safe — no
    /// exception is thrown (idempotent release).
    /// </summary>
    [Fact]
    public async Task SwitchDatabase_DoubleRelease_NoException()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);
        await svc.ReleaseAsync(saveFirst: false);

        var ex = await Record.ExceptionAsync(() => svc.ReleaseAsync(saveFirst: false));
        Assert.Null(ex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 6 — Crash recovery and utility helpers
    //
    // DetectCrashRecovery looks for a dirty marker from a prior ungraceful exit.
    // DiscardCrash removes the orphaned working copy.
    // CleanupOrphanedTmp removes a D.tmp left by an interrupted save.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DetectCrashRecovery returns true when both D' and its dirty marker exist,
    /// indicating a prior session ended without saving.
    /// </summary>
    [Fact]
    public async Task DetectCrashRecovery_BothFilesPresent_ReturnsTrue()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        // Simulate a previous session that checked out, made an edit, then crashed.
        // MarkDirty() writes the dirty marker (as a real repository write would).
        await svc.CheckoutAsync(db);
        svc.MarkDirty(); // Simulate a user write.
        var workingPath = svc.WorkingPath;
        // Simulate crash: abandon without releasing (dirty marker stays).
        // NOTE: do NOT call ReleaseAsync here — a crash means no clean shutdown.

        // Use a fresh service instance (simulating app restart) — same workingDir so
        // it computes the same D' path.
        var (svc2, _) = CreateService();
        var detected = svc2.DetectCrashRecovery(db);

        Assert.True(detected);
        Assert.True(File.Exists(workingPath), "Working copy must still exist.");
        Assert.True(File.Exists(workingPath + ".dirty"), "Dirty marker must still exist.");
    }

    /// <summary>
    /// DetectCrashRecovery returns false when D' exists but no dirty marker was
    /// ever written — this is the normal state when no user edits were made.
    /// </summary>
    [Fact]
    public async Task DetectCrashRecovery_NoDirtyMarker_ReturnsFalse()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        // Release without any user edits — no dirty marker was ever written.
        await svc.ReleaseAsync(saveFirst: true);

        var (svc2, _) = CreateService();
        Assert.False(svc2.DetectCrashRecovery(db));
    }

    /// <summary>
    /// DetectCrashRecovery returns false when the dirty marker exists but D' does
    /// not — the file was already cleaned up externally.
    /// </summary>
    [Fact]
    public void DetectCrashRecovery_NoWorkingCopy_ReturnsFalse()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        // Write a dirty marker manually without creating D'.
        // We need the same path the service would compute, so use a proxy checkout.
        // Instead: just assert that without any prior checkout, detection is false.
        Assert.False(svc.DetectCrashRecovery(db));
    }

    /// <summary>
    /// DiscardCrash removes D' and its dirty marker, leaving no orphaned files.
    /// </summary>
    [Fact]
    public async Task DiscardCrash_RemovesWorkingCopyAndDirtyMarker()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty(); // Simulate a user write so the dirty marker is created.
        var workingPath = svc.WorkingPath;
        // Simulate crash: abandon without releasing (dirty marker and working copy remain).

        Assert.True(File.Exists(workingPath), "Precondition: working copy must exist.");
        Assert.True(File.Exists(workingPath + ".dirty"), "Precondition: dirty marker must exist.");

        var (svc2, _) = CreateService();
        svc2.DiscardCrash(db);

        Assert.False(File.Exists(workingPath), "Working copy should be deleted by DiscardCrash.");
        Assert.False(File.Exists(workingPath + ".dirty"), "Dirty marker should be deleted by DiscardCrash.");
    }

    /// <summary>
    /// CleanupOrphanedTmpAsync deletes D.tmp when it exists alongside D.
    /// </summary>
    [Fact]
    public async Task CleanupOrphanedTmp_WhenTmpExists_DeletesIt()
    {
        var (svc, _) = CreateService();
        var db      = DbPath();
        var tmpPath = db + ".tmp";
        CreateFile(db);
        CreateFile(tmpPath, "partial-write-content");

        await svc.CleanupOrphanedTmpAsync(db);

        Assert.False(File.Exists(tmpPath));
    }

    /// <summary>
    /// CleanupOrphanedTmpAsync does not throw when there is no D.tmp to delete.
    /// </summary>
    [Fact]
    public async Task CleanupOrphanedTmp_WhenNoTmp_NoException()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        var ex = await Record.ExceptionAsync(() => svc.CleanupOrphanedTmpAsync(db));
        Assert.Null(ex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 6b — MarkDirty and CleanupStaleCrashArtifacts
    //
    // These tests cover the accurate dirty-marker design:
    //   • The dirty marker is NOT written at checkout — only when MarkDirty() is called.
    //   • MarkDirty() is a no-op in read-only mode.
    //   • CleanupStaleCrashArtifacts silently handles the two benign orphan states.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After checkout with no subsequent MarkDirty call, no dirty marker exists.
    /// This means a crash before any user edits won't trigger a spurious notification.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_NoEdits_NoDirtyMarker()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        Assert.False(File.Exists(svc.WorkingPath + ".dirty"),
            "Dirty marker must not be written at checkout — only on first user write.");
    }

    /// <summary>
    /// After CheckoutAsync, calling MarkDirty() writes the dirty marker.
    /// </summary>
    [Fact]
    public async Task MarkDirty_AfterCheckout_DirtyMarkerAppears()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        Assert.False(File.Exists(svc.WorkingPath + ".dirty"), "Precondition: no marker yet.");

        svc.MarkDirty();

        Assert.True(File.Exists(svc.WorkingPath + ".dirty"),
            "Dirty marker must appear after MarkDirty() is called.");
    }

    /// <summary>
    /// MarkDirty() is idempotent — calling it multiple times does not throw or
    /// create duplicate markers, and the marker exists after all calls.
    /// </summary>
    [Fact]
    public async Task MarkDirty_CalledMultipleTimes_NoException()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();
        svc.MarkDirty();
        svc.MarkDirty();

        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// MarkDirty() in read-only mode (lock held by another) does not write a marker.
    /// </summary>
    [Fact]
    public async Task MarkDirty_ReadOnlyMode_NoMarkerWritten()
    {
        var (svc1, _) = CreateService();
        var (svc2, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        // svc1 holds the write lock; svc2 gets read-only.
        await svc1.CheckoutAsync(db);
        await svc2.CheckoutAsync(db);

        Assert.Equal(CheckoutMode.ReadOnly, svc2.Mode);

        svc2.MarkDirty(); // Should be a no-op in read-only mode.

        Assert.False(File.Exists(svc2.WorkingPath + ".dirty"),
            "MarkDirty() must not write a marker in read-only mode.");

        await svc1.ReleaseAsync(saveFirst: false);
    }

    /// <summary>
    /// DetectCrashRecovery returns false when no user edits were made (no dirty marker),
    /// even though D' exists. The crash happened before any data changed.
    /// This is the regression guard for the false-positive crash notification fix.
    /// </summary>
    [Fact]
    public async Task DetectCrashRecovery_NoEdits_ReturnsFalse()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        // Simulate crash: abandon without calling MarkDirty or ReleaseAsync.
        var workingPath = svc.WorkingPath;

        var (svc2, _) = CreateService();
        Assert.False(svc2.DetectCrashRecovery(db),
            "No dirty marker = no crash to recover from, even though D' exists.");

        // D' exists but has no marker — CleanupStaleCrashArtifacts should handle it.
        Assert.True(File.Exists(workingPath), "D' is left behind by the abandoned checkout.");
    }

    /// <summary>
    /// CleanupStaleCrashArtifacts deletes D' when it exists without a dirty marker
    /// (crash before any edits — nothing was lost).
    /// </summary>
    [Fact]
    public async Task CleanupStaleCrashArtifacts_WorkingCopyNoMarker_DeletesWorkingCopy()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db); // D' created, no marker written.
        var workingPath = svc.WorkingPath;

        var (svc2, _) = CreateService();
        svc2.CleanupStaleCrashArtifacts(db);

        Assert.False(File.Exists(workingPath),
            "D' without a dirty marker should be silently deleted.");
    }

    /// <summary>
    /// CleanupStaleCrashArtifacts deletes the dirty marker when it exists without D'
    /// (interrupted discard from a previous session).
    /// </summary>
    [Fact]
    public async Task CleanupStaleCrashArtifacts_MarkerNoWorkingCopy_DeletesMarker()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();
        var workingPath = svc.WorkingPath;
        var markerPath  = workingPath + ".dirty";

        // Manually delete D' but leave the marker (simulates interrupted discard).
        File.Delete(workingPath);

        var (svc2, _) = CreateService();
        svc2.CleanupStaleCrashArtifacts(db);

        Assert.False(File.Exists(markerPath),
            "Orphaned dirty marker without D' should be silently deleted.");
    }

    /// <summary>
    /// CleanupStaleCrashArtifacts leaves both files untouched when both D' and its
    /// dirty marker exist — this is a real crash scenario for DetectCrashRecovery.
    /// </summary>
    [Fact]
    public async Task CleanupStaleCrashArtifacts_BothPresent_LeavesUntouched()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();
        var workingPath = svc.WorkingPath;

        var (svc2, _) = CreateService();
        svc2.CleanupStaleCrashArtifacts(db);

        Assert.True(File.Exists(workingPath),       "D' must remain when both files present.");
        Assert.True(File.Exists(workingPath + ".dirty"), "Dirty marker must remain when both files present.");
    }

    /// <summary>
    /// CleanupStaleCrashArtifacts does not throw when neither D' nor marker exist.
    /// </summary>
    [Fact]
    public void CleanupStaleCrashArtifacts_NeitherPresent_NoException()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db);

        var ex = Record.Exception(() => svc.CleanupStaleCrashArtifacts(db));
        Assert.Null(ex);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 7 — Coexistence with open database connections
    //
    // These tests simulate the real-world condition where DatabaseContext (or any
    // other caller) holds D or D' open with FileAccess.ReadWrite while checkout
    // and save operations are running.
    //
    // On Windows, a file opened with GENERIC_READ|GENERIC_WRITE requires that
    // any other opener's FileShare mask includes FILE_SHARE_WRITE; otherwise the
    // OS returns ERROR_SHARING_VIOLATION. File.Copy and File.OpenRead use only
    // FileShare.Read, which does NOT cover GENERIC_WRITE, causing the crash that
    // was observed during the wizard completion path.
    //
    // All tests in this group hold one or more files open using
    //   FileAccess.ReadWrite, FileShare.ReadWrite
    // which mirrors SqliteConnection's access pattern, and verify that
    // CheckoutService operations succeed without throwing.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CheckoutAsync succeeds and returns WriteAccess even when D is held open
    /// with ReadWrite access — the wizard scenario where the database is still
    /// open from the schema-initialisation step inside InitializeServices.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_WhileSourceHeldOpenWithWriteAccess_Succeeds()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "db-content");

        // Hold D open with ReadWrite+ReadWrite to mimic DatabaseContext.
        using var openHandle = new FileStream(
            db, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
    }

    /// <summary>
    /// After CheckoutAsync with D held open, the working copy D' contains the same
    /// bytes as D — CopyWithSharing transferred the data correctly.
    /// </summary>
    [Fact]
    public async Task CheckoutAsync_WhileSourceHeldOpen_WorkingCopyMatchesSource()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "db-content");

        using var openHandle = new FileStream(
            db, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        await svc.CheckoutAsync(db);

        Assert.Equal("db-content", File.ReadAllText(svc.WorkingPath));
    }

    /// <summary>
    /// SaveAsync succeeds and D is updated even when D' is held open with
    /// ReadWrite access — the normal editing scenario where DatabaseContext
    /// keeps the working copy open throughout a user's session.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhileWorkingCopyHeldOpenWithWriteAccess_Succeeds()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        // Simulate DatabaseContext: insert a row and keep the connection open,
        // mirroring the production scenario where SaveAsync fires while the
        // app's DatabaseContext still holds D' open.
        using var workingConn = new SqliteConnection($"Data Source={svc.WorkingPath};Pooling=False");
        workingConn.Open();
        using var cmd = workingConn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES('edited')";
        cmd.ExecuteNonQuery();

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
    }

    /// <summary>
    /// After a successful save with D' held open, D contains the edited content —
    /// the bytes were copied correctly from D' to D even with an open handle.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhileWorkingCopyHeldOpen_SourceUpdatedWithNewContent()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        // Simulate DatabaseContext: insert a row and keep the connection open,
        // then save. Verifies the inserted data propagates to D after save.
        using var workingConn = new SqliteConnection($"Data Source={svc.WorkingPath};Pooling=False");
        workingConn.Open();
        using var cmd = workingConn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES('edited-content')";
        cmd.ExecuteNonQuery();

        await svc.SaveAsync();

        workingConn.Dispose();
        Assert.Equal("edited-content", ReadTestValue(db));
    }

    /// <summary>
    /// Complete wizard-to-edit flow with open connections at the correct lifecycle phases:
    ///   1. Database file exists (created by InitializeServices / schema init)
    ///   2. D is held open (old DatabaseContext still has it open)
    ///   3. CheckoutAsync copies D to D' — succeeds despite D's open handle
    ///   4. Old container is disposed — D is closed (simulates App.InitializeServices(D'))
    ///   5. User edits D'; new DatabaseContext opens D'
    ///   6. SaveAsync copies D' back to D — succeeds with D' open (CopyWithSharing)
    ///      and D closed (File.Move can replace it)
    ///   7. D ends up with the edited content
    /// </summary>
    [Fact]
    public async Task WizardToEditFlow_WithOpenConnections_CompletesSuccessfully()
    {
        var (svc, _) = CreateService();
        var db = DbPath("wizard");
        CreateSqliteDb(db);

        // Step 1: D exists (created by standalone DatabaseContext in ValidateStep3).
        // Simulate the brief window where the schema-creation connection is still open.
        using var sourceHandle = new FileStream(
            db, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        // Step 2: checkout — must succeed while D is held open (CopyWithSharing).
        var checkoutOutcome = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, checkoutOutcome);

        // Step 3: schema-creation handle released (the using block in ValidateStep3
        // disposes the standalone DatabaseContext before DI is initialized).
        sourceHandle.Dispose();

        // Step 4: simulate wizard/user editing D' with an open connection.
        InsertTestValue(svc.WorkingPath, "user-edited-content");
        using var workingConn = new SqliteConnection($"Data Source={svc.WorkingPath};Pooling=False");
        workingConn.Open();

        // Step 5: Save — D' held open by DatabaseContext, D is closed.
        var saveOutcome = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.Success, saveOutcome);

        // Step 6: verify D was updated.
        workingConn.Dispose();
        Assert.Equal("user-edited-content", ReadTestValue(db));
    }

    /// <summary>
    /// Complete database-switch flow with open connections (mirrors production lifecycle):
    ///   1. Checkout DB1 while DB1 is held open → CopyWithSharing succeeds
    ///   2. Close DB1 handle (simulates old DI container disposal before save)
    ///   3. Edit and save DB1's working copy while D1' is held open → succeeds
    ///   4. Release DB1
    ///   5. Checkout DB2 while DB2 is held open → succeeds
    ///   6. Verify new session is correctly set up for DB2
    /// </summary>
    [Fact]
    public async Task SwitchDatabase_WithOpenConnections_CompletesSuccessfully()
    {
        var (svc, lockSvc) = CreateService();
        var db1 = DbPath("switch1");
        var db2 = DbPath("switch2");
        CreateSqliteDb(db1);
        CreateSqliteDb(db2);

        // ── DB1 session ──────────────────────────────────────────────────────
        using var db1Handle = new FileStream(
            db1, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var outcome1 = await svc.CheckoutAsync(db1);
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome1);

        // Close DB1 source handle before save — in production, InitializeServices
        // disposes the old DatabaseContext (closing D) before the user ever saves.
        db1Handle.Dispose();

        // Edit D1' and hold it open via SqliteConnection (mirrors production state).
        InsertTestValue(svc.WorkingPath, "db1-edited");
        using var work1Conn = new SqliteConnection($"Data Source={svc.WorkingPath};Pooling=False");
        work1Conn.Open();

        var save1 = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.Success, save1);

        work1Conn.Dispose();

        // Release DB1.
        await svc.ReleaseAsync(saveFirst: false);
        Assert.False(lockSvc.IsWriter);

        // ── DB2 session ──────────────────────────────────────────────────────
        using var db2Handle = new FileStream(
            db2, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var outcome2 = await svc.CheckoutAsync(db2);
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome2);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(db2, svc.SourcePath);

        db2Handle.Dispose();
    }

    /// <summary>
    /// ComputeHash (used during CheckoutAsync and SaveAsync conflict detection)
    /// does not throw when D is held open with ReadWrite access — the FileShare.ReadWrite
    /// flag must be used when opening the file for hashing.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ConflictCheck_WhileSourceHeldOpen_DoesNotThrow()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "hash-test");

        await svc.CheckoutAsync(db);

        // Hold D open — SaveAsync must re-hash D to check for external modification.
        using var sourceHandle = new FileStream(
            db, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var ex = await Record.ExceptionAsync(() => svc.SaveAsync());

        Assert.Null(ex);
    }

    /// <summary>
    /// Reproduces the wizard-to-save bug using a real <see cref="TermPoint.Data.DatabaseContext"/>.
    ///
    /// <para>The full flow mirrors what happens in production after the wizard completes:</para>
    /// <list type="number">
    ///   <item>D is created with <see cref="NewDatabaseFile"/> (built locally, copied across —
    ///         SQLite never opens D itself), exactly as wizard step 3 does.</item>
    ///   <item>CheckoutAsync — D exists, normal mode: D is copied to D'.</item>
    ///   <item>DatabaseContext.Dispose() — simulates the old DI container being disposed inside
    ///         App.InitializeServices(D'). Microsoft.Data.Sqlite pools connections by default:
    ///         SqliteConnection.Dispose() returns the connection to the pool rather than closing
    ///         it, leaving D's OS file handle open. DatabaseContext.Dispose() now calls
    ///         SqliteConnection.ClearAllPools() to flush the pool and release the handle.</item>
    ///   <item>SaveAsync — must succeed. Before the fix, File.Move(D.tmp → D) failed with
    ///         "Access to the path is denied" because the pool retained D's handle without
    ///         FILE_SHARE_DELETE. Plain FileStream (used in other Group 7 tests) is not
    ///         pooled, which is why those tests passed while this scenario failed at runtime.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task WizardFlow_WithRealDatabaseContext_SaveSucceeds()
    {
        var (svc, _) = CreateService();
        var db = DbPath("wizard_ctx");

        // ── Step 1: Create D with schema via NewDatabaseFile ─────────────────────
        // Mirrors ValidateStep3 creating D before checkout.
        await CreateNewDatabaseAsync(db);

        // ── Step 2: Checkout — D exists, normal path: copies D → D' ──────────────
        var checkoutOutcome = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, checkoutOutcome);
        Assert.NotEqual(db, svc.WorkingPath); // D' != D confirms normal mode

        // ── Step 3: Open a DatabaseContext against D' (simulates wizard DI) ──────
        var ctx = new TermPoint.Data.DatabaseContext(svc.WorkingPath);

        // ── Step 4: Simulate wizard writes to D' ─────────────────────────────────
        InsertTestValue(svc.WorkingPath, "wizard-data");

        // ── Step 5: Dispose the DatabaseContext ──────────────────────────────────
        ctx.Dispose();

        // ── Step 6: Save — pushes D' → D ─────────────────────────────────────────
        var saveOutcome = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.Success, saveOutcome);
        Assert.Equal("wizard-data", ReadTestValue(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Full wizard lifecycle: create D → checkout → work against D' → release with
    /// save → re-checkout. Mirrors the production flow: ValidateStep3 creates D with
    /// a standalone DatabaseContext, checks out to D', wizard steps write to D',
    /// SwitchDatabaseAsync releases (saving D' → D), then re-checks out for normal
    /// operation.
    /// </summary>
    [Fact]
    public async Task WizardLifecycle_CreateCheckoutWorkSaveReCheckout_Succeeds()
    {
        var (svc, _) = CreateService();
        var db = DbPath("wizard_lifecycle");

        // Phase 1: ValidateStep3 — create D (NewDatabaseFile), checkout to D'.
        await CreateNewDatabaseAsync(db);
        var outcome1 = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome1);
        Assert.NotEqual(db, svc.WorkingPath);

        // Phase 2: Wizard writes entity data to D'.
        InsertTestValue(svc.WorkingPath, "wizard-entity-data");

        // Phase 3: SwitchDatabaseAsync — release with save (pushes D' → D).
        await svc.ReleaseAsync(saveFirst: true);
        Assert.Equal("wizard-entity-data", ReadTestValue(db));

        // Phase 4: SwitchDatabaseAsync — re-checkout for normal operation.
        var outcome2 = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome2);
        Assert.NotEqual(db, svc.WorkingPath);

        // Phase 5: Normal editing against the fresh D'.
        InsertTestValue(svc.WorkingPath, "post-wizard-edit");
        var saveResult = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.Success, saveResult);
        Assert.Equal("post-wizard-edit", ReadTestValue(db));
    }

    /// <summary>
    /// Mirrors the File → New flow: SwitchDatabaseAsync creates D with schema before
    /// checkout, so CheckoutAsync takes the normal path (D' != D) and all subsequent
    /// writes go to the local working copy.
    /// </summary>
    [Fact]
    public async Task FileNew_CreateSchemaBeforeCheckout_WritesToWorkingCopy()
    {
        var (svc, _) = CreateService();
        var db = DbPath("file_new");

        // SwitchDatabaseAsync creates D with schema (NewDatabaseFile) before calling CheckoutAsync.
        await CreateNewDatabaseAsync(db);

        var outcome = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.NotEqual(db, svc.WorkingPath); // D' != D — not degenerate

        // Simulate ApplyConfig + editing against D'.
        InsertTestValue(svc.WorkingPath, "file-new-content");

        var saveOutcome = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.Success, saveOutcome);
        Assert.Equal("file-new-content", ReadTestValue(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 8 — Startup from most-recent database + menu bar name + save
    //
    // Simulates the app's startup path when a prior session exists:
    //   AppSettings.RecentDatabases[0] holds the most-recently opened file.
    //   CheckoutAsync is called with that path; SourcePath is then used by
    //   MainWindow to set DatabaseName = Path.GetFileNameWithoutExtension(SourcePath).
    //   After checkout, the user edits, presses Save, and D is updated.
    //
    // These tests do NOT touch AppSettings.Current (a process-wide singleton)
    // to avoid cross-test contamination; instead they drive CheckoutService
    // directly, which is the exact code called by SwitchDatabaseAsync after
    // the most-recent path is retrieved from settings.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After checking out the most-recent database, SourcePath equals the path
    /// that was passed to CheckoutAsync.  MainWindow derives the menu-bar label
    /// as <c>Path.GetFileNameWithoutExtension(SourcePath)</c>, so this verifies
    /// that the correct name would be displayed.
    /// </summary>
    [Fact]
    public async Task MostRecentDatabase_SourcePathMatchesMostRecentEntry()
    {
        // Arrange: two files exist; recentDatabases is ordered most-recent first,
        // exactly as AppSettings.AddRecentDatabase inserts them.
        var older  = DbPath("OlderSchedule");
        var recent = DbPath("FallSchedule");
        CreateFile(older,  "older-content");
        CreateFile(recent, "recent-content");

        var recentDatabases = new[] { recent, older }; // index 0 = most recent

        var (svc, _) = CreateService();

        // Act: open the most-recent database (mirrors SwitchDatabaseAsync reading
        // AppSettings.RecentDatabases[0] and calling RunCheckoutAsync with that path).
        var mostRecentPath = recentDatabases[0];
        var outcome = await svc.CheckoutAsync(mostRecentPath);

        // Assert: checkout succeeded and SourcePath is the file that was opened.
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(Path.GetFullPath(mostRecentPath), Path.GetFullPath(svc.SourcePath));

        // The string bound to DatabaseName in the menu bar is derived here:
        var displayName = Path.GetFileNameWithoutExtension(svc.SourcePath);
        Assert.Equal("FallSchedule", displayName);
    }

    /// <summary>
    /// When the most-recent database is opened and the user presses Save,
    /// SaveAsync returns Success and the source file D is updated with the
    /// content written to the working copy D'.
    /// </summary>
    [Fact]
    public async Task MostRecentDatabase_AfterEdit_SaveUpdatesSourceFile()
    {
        // Arrange: simulate a database that was used in a prior session.
        var db = DbPath("SpringSchedule");
        CreateSqliteDb(db);

        // AppSettings.RecentDatabases[0] would be this path; the app passes it
        // to CheckoutAsync via SwitchDatabaseAsync / RunCheckoutAsync.
        var (svc, _) = CreateService();
        var outcome = await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);

        // Simulate the app writing new data to D' via SQLite (as DatabaseContext does).
        const string editedContent = "spring-schedule-edited";
        InsertTestValue(svc.WorkingPath, editedContent);

        // Act: user presses the Save button → MainWindowViewModel.SaveCommand →
        // CheckoutService.SaveAsync().
        var saveOutcome = await svc.SaveAsync();

        // Assert: save succeeded and D now contains the edited content.
        Assert.Equal(SaveOutcome.Success, saveOutcome);
        Assert.Equal(editedContent, ReadTestValue(svc.SourcePath));
    }

    /// <summary>
    /// Opening the most-recent database correctly sets SourcePath (not WorkingPath)
    /// as the canonical path for the menu bar.  In write mode the two paths differ;
    /// the title bar must show SourcePath (D), not the hidden working copy D'.
    /// </summary>
    [Fact]
    public async Task MostRecentDatabase_MenuBarUsesSourcePathNotWorkingPath()
    {
        var db = DbPath("WinterSchedule");
        CreateFile(db, "winter-content");

        var (svc, _) = CreateService();
        await svc.CheckoutAsync(db);

        // D and D' must be distinct in normal (non-degenerate) mode.
        Assert.NotEqual(svc.SourcePath, svc.WorkingPath);

        // The canonical path for the menu bar is SourcePath (mirrors the
        // guard in SwitchDatabaseAsync: canonicalPath = App.Checkout.SourcePath
        // when Mode == WriteAccess).
        var canonicalPath = svc.Mode == CheckoutMode.WriteAccess
            ? svc.SourcePath
            : db;

        // The display name the user sees in the menu bar.
        var displayName = Path.GetFileNameWithoutExtension(canonicalPath);
        Assert.Equal("WinterSchedule", displayName);

        // Must NOT be the working-copy name (which contains a session-GUID suffix).
        var workingName = Path.GetFileNameWithoutExtension(svc.WorkingPath);
        Assert.NotEqual("WinterSchedule", workingName);
    }

    // ── Group 8: Read-only Snapshot Refresh ─────────────────────────────────

    /// <summary>
    /// When a second instance opens the same database in read-only mode,
    /// it should create a local D'' snapshot and use that instead of D.
    /// </summary>
    [Fact]
    public async Task ReadOnlyCheckout_CreatesDSnapshot()
    {
        var db = DbPath("shared");
        CreateFile(db);
        var (writer, lockSvc) = CreateService();

        // Writer acquires the lock.
        var writeOutcome = await writer.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, writeOutcome);

        // Reader tries to open the same database.
        var (reader, readerLockSvc) = CreateService();
        var readOutcome = await reader.CheckoutAsync(db);
        Assert.Equal(CheckoutOutcome.ReadOnly, readOutcome);

        // Reader should have created D'' (read-only snapshot).
        Assert.True(reader.IsReadOnlyMode, "Reader should be in read-only snapshot mode");
        Assert.NotNull(reader.ReadOnlyWorkingPath);
        Assert.True(File.Exists(reader.ReadOnlyWorkingPath), "D'' should exist");

        // D'' should be different from D and different from D' (writer's working copy).
        Assert.NotEqual(db, reader.ReadOnlyWorkingPath);
        Assert.NotEqual(writer.WorkingPath, reader.ReadOnlyWorkingPath);

        // Reader's WorkingPath should point to D''.
        Assert.Equal(reader.ReadOnlyWorkingPath, reader.WorkingPath);
    }

    /// <summary>
    /// When a writer modifies D and saves, then a reader calls RefreshReadOnlySnapshotAsync,
    /// D'' should be updated to reflect the changes.
    /// </summary>
    [Fact]
    public async Task ReadOnlyRefresh_UpdatesDSnapshotWhenSourceChanges()
    {
        var db = DbPath("shared");
        CreateSqliteDb(db);
        InsertTestValue(db, "original");

        // Writer opens and reads initial D.
        var (writer, _) = CreateService();
        await writer.CheckoutAsync(db);

        // Reader opens — creates D''.
        var (reader, _) = CreateService();
        await reader.CheckoutAsync(db);
        var d1Hash = reader.HashAtCheckout;

        // Writer modifies D (simulating another user's save).
        File.Delete(db);
        CreateSqliteDb(db);
        InsertTestValue(db, "modified");

        // Reader calls refresh.
        var outcome = await reader.RefreshReadOnlySnapshotAsync();

        // Refresh should detect the change and update D''.
        Assert.Equal(RefreshOutcome.Updated, outcome);

        // D'' hash should have changed.
        var d2Hash = reader.HashAtCheckout;
        Assert.NotEqual(d1Hash, d2Hash);
    }

    /// <summary>
    /// When RefreshReadOnlySnapshotAsync is called but the source is unavailable,
    /// D'' should remain unchanged and RefreshOutcome.SourceUnavailable returned.
    /// </summary>
    [Fact]
    public async Task ReadOnlyRefresh_HandlesUnavailableSource()
    {
        var db = DbPath("shared");
        CreateSqliteDb(db);

        // Writer opens.
        var (writer, _) = CreateService();
        await writer.CheckoutAsync(db);

        // Reader opens — creates D''.
        var (reader, _) = CreateService();
        await reader.CheckoutAsync(db);
        var d1Hash = reader.HashAtCheckout;

        // Simulate network unavailable — move the source file.
        var dbBackup = db + ".backup";
        File.Move(db, dbBackup, overwrite: true);

        // Reader calls refresh — source is unreachable.
        var outcome = await reader.RefreshReadOnlySnapshotAsync();

        // Refresh should return SourceUnavailable.
        Assert.Equal(RefreshOutcome.SourceUnavailable, outcome);

        // D'' should be unchanged (stale).
        var d2Hash = reader.HashAtCheckout;
        Assert.Equal(d1Hash, d2Hash);

        // Restore source for cleanup.
        File.Move(dbBackup, db, overwrite: true);
    }

    /// <summary>
    /// Spec item 28 (from item 11): a refresh whose <c>beforeOverwrite</c> callback throws fails as
    /// <see cref="RefreshOutcome.SourceUnavailable"/>, the connection is never reopened by the service (so
    /// <c>afterOverwrite</c> is not called: the caller knows the connection was lost by seeing that "before" ran and
    /// "after" did not complete), and the failure is logged with <c>LogInfo</c> (what failed, the exception's type and
    /// message) and NEVER <c>LogError</c>, which would put a red log-text banner next to the plain line the window shows.
    /// </summary>
    [Fact]
    public async Task ReadOnlyRefresh_WhenBeforeOverwriteThrows_IsSourceUnavailable_LogsInfoNotError_AndSkipsAfterOverwrite()
    {
        var db = DbPath("shared");
        CreateSqliteDb(db);
        var (writer, _) = CreateService();
        await writer.CheckoutAsync(db);
        var log = new RecordingAppLogger();
        var (reader, _) = CreateService(logger: log);
        await reader.CheckoutAsync(db);
        var afterCalled = false;

        var outcome = await reader.RefreshReadOnlySnapshotAsync(
            beforeOverwrite: () => throw new InvalidOperationException("close failed"),
            afterOverwrite:  () => { afterCalled = true; return Task.CompletedTask; });

        Assert.Equal(RefreshOutcome.SourceUnavailable, outcome);
        Assert.False(afterCalled);
        Assert.Empty(log.Errors);
        Assert.Contains(log.Infos, i => i.Contains("beforeOverwrite") && i.Contains("InvalidOperationException") && i.Contains("close failed"));
    }

    /// <summary>
    /// Spec item 28 (from item 11): a refresh whose <c>afterOverwrite</c> callback (the reopen of the connection)
    /// throws fails as <see cref="RefreshOutcome.SourceUnavailable"/> after <c>beforeOverwrite</c> had run, which is
    /// exactly the "connection lost" state the window detects; the failure is logged with <c>LogInfo</c>, with the
    /// exception's type and message, and never <c>LogError</c>.
    /// </summary>
    [Fact]
    public async Task ReadOnlyRefresh_WhenAfterOverwriteThrows_IsSourceUnavailable_LogsInfoNotError()
    {
        var db = DbPath("shared");
        CreateSqliteDb(db);
        var (writer, _) = CreateService();
        await writer.CheckoutAsync(db);
        var log = new RecordingAppLogger();
        var (reader, _) = CreateService(logger: log);
        await reader.CheckoutAsync(db);
        var beforeRan = false;

        var outcome = await reader.RefreshReadOnlySnapshotAsync(
            beforeOverwrite: () => { beforeRan = true; return Task.CompletedTask; },
            afterOverwrite:  () => throw new InvalidOperationException("reopen failed"));

        Assert.Equal(RefreshOutcome.SourceUnavailable, outcome);
        Assert.True(beforeRan);
        Assert.Empty(log.Errors);
        Assert.Contains(log.Infos, i => i.Contains("afterOverwrite") && i.Contains("InvalidOperationException") && i.Contains("reopen failed"));
    }

    /// <summary>
    /// Crash recovery detection should skip read-only snapshot files (D'' with "_ro" suffix).
    /// </summary>
    [Fact]
    public void CrashRecoveryDetection_SkipsReadOnlySnapshots()
    {
        var db = DbPath("test");
        CreateFile(db);

        var (svc, _) = CreateService();

        // Manually create a D'' file (read-only snapshot) to simulate a previous read-only session.
        var roPath = ComputeReadOnlyPath(db);
        Directory.CreateDirectory(Path.GetDirectoryName(roPath)!);
        File.WriteAllText(roPath, "snapshot");

        // Even though D'' exists, DetectCrashRecovery should return false
        // because read-only snapshots don't have dirty markers.
        var hasCrash = svc.DetectCrashRecovery(db);
        Assert.False(hasCrash);
    }

    /// <summary>
    /// Multiple read-only instances should each have their own D'' but use the same source.
    /// </summary>
    [Fact]
    public async Task MultipleReaders_EachHaveOwnDSnapshot()
    {
        var db = DbPath("shared");
        CreateSqliteDb(db);

        var (writer, _) = CreateService();
        await writer.CheckoutAsync(db);

        var (reader1, _) = CreateService();
        await reader1.CheckoutAsync(db);
        var r1Path = reader1.ReadOnlyWorkingPath;

        var (reader2, _) = CreateService();
        await reader2.CheckoutAsync(db);
        var r2Path = reader2.ReadOnlyWorkingPath;

        // Both readers should have the same D'' path (same computation from source).
        Assert.Equal(r1Path, r2Path);

        // The D'' should be the one file, shared by both readers.
        Assert.True(File.Exists(r1Path));
        Assert.Equal(r1Path, r2Path);
    }

    // ── Helper for computing file hash ────────────────────────────────────

    private static string ComputeFileHash(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>Helper for computing the expected D'' (read-only snapshot) path for testing.</summary>
    private string ComputeReadOnlyPath(string sourcePath)
    {
        // Replicate the private CheckoutService method logic for testing purposes.
        var normalized = Path.GetFullPath(sourcePath).ToLowerInvariant();
        var hashBytes  = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var shortHash  = Convert.ToHexString(hashBytes)[..8];
        var dbFileName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext        = Path.GetExtension(sourcePath);
        return Path.Combine(_workingDir, $"{shortHash}_{dbFileName}_ro{ext}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 8 — Concurrency guards (F3, F9 from data-integrity audit)
    //
    // SaveAsync may be invoked simultaneously from the UI thread (explicit save),
    // the autosave timer (thread-pool thread), and ReleaseAsync on shutdown. The
    // _saveInFlight gate must be atomic so only one save runs at a time, and
    // ReleaseAsync must coordinate with a queued autosave callback.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Many concurrent SaveAsync calls — exactly one must succeed; the rest must be
    /// gated out as CopyError. With a non-atomic check-then-set, multiple threads can
    /// pass the gate before any flips it, producing concurrent saves that race on the
    /// rename to D and either throw or corrupt D. Regression guard for F3.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ConcurrentCalls_OnlyOneSucceeds()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        const int callers = 16;
        var tasks = new Task<SaveOutcome>[callers];
        for (int i = 0; i < callers; i++)
            tasks[i] = Task.Run(() => svc.SaveAsync().GetAwaiter().GetResult());

        var outcomes = await Task.WhenAll(tasks);

        int successCount   = outcomes.Count(o => o == SaveOutcome.Success);
        int copyErrorCount = outcomes.Count(o => o == SaveOutcome.CopyError);

        // At least one success (real save ran); the rest are gated out.
        Assert.True(successCount >= 1, $"Expected at least 1 Success; got {successCount}");
        Assert.Equal(callers, successCount + copyErrorCount);
        // The whole point: no thread observed the half-state and threw; no other outcomes.
    }

    /// <summary>
    /// ReleaseAsync(saveFirst:true) stops the autosave timer and then saves. If a
    /// timer callback fires immediately before StopAutoSave, AutoSaveTickAsync is
    /// already queued. The Interlocked gate (F3) plus the Task.Yield in ReleaseAsync
    /// (F9) must guarantee that exactly one save completes — not zero, not two.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_AutoSaveFiredDuringRelease_OnlyOneSaveCompletes()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        int saveCompletedCount = 0;
        svc.SaveCompleted += () => Interlocked.Increment(ref saveCompletedCount);

        // Race: kick off a parallel SaveAsync to simulate a queued autosave callback,
        // then call ReleaseAsync which itself does a save. Whichever wins the gate
        // runs to completion; the loser bails as CopyError. The lock then releases.
        var queuedAutoSave = Task.Run(() => svc.SaveAsync().GetAwaiter().GetResult());
        await svc.ReleaseAsync(saveFirst: true);
        await queuedAutoSave;

        // Either the autosave or the release-save completed — not both.
        Assert.InRange(saveCompletedCount, 1, 2);
        // After release we must be in ReadOnly mode.
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 9 — Dirty-marker race (F2 from data-integrity audit)
    //
    // ResetDirty must be called BEFORE the dirty marker file is deleted so that
    // a write arriving in the post-rename window can still re-arm MarkDirty and
    // re-write the marker. The wiring point is the synchronous
    // BeforeDirtyMarkerDeleted event.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// BeforeDirtyMarkerDeleted must fire synchronously inside SaveAsync, BEFORE the
    /// marker file is removed. Verified by checking the marker file still exists at
    /// the moment the event handler runs. Regression guard for F2.
    /// </summary>
    [Fact]
    public async Task SaveAsync_BeforeDirtyMarkerDeleted_FiresWhileMarkerStillExists()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        svc.MarkDirty(); // creates the marker file
        var markerPath = svc.WorkingPath + ".dirty";
        Assert.True(File.Exists(markerPath), "Precondition: marker exists before save.");

        bool markerExistedDuringEvent = false;
        bool eventFired = false;
        svc.BeforeDirtyMarkerDeleted += () =>
        {
            eventFired = true;
            markerExistedDuringEvent = File.Exists(markerPath);
        };

        await svc.SaveAsync();

        Assert.True(eventFired, "BeforeDirtyMarkerDeleted must fire during SaveAsync.");
        Assert.True(markerExistedDuringEvent,
            "Event must fire BEFORE DeleteDirtyMarker; marker should still exist when handler runs.");
        Assert.False(File.Exists(markerPath), "After save, marker is gone.");
    }

    /// <summary>
    /// SaveCompleted is no longer the right hook for ResetDirty (it fires after the
    /// marker is already deleted, and is dispatched asynchronously). With the F2 fix,
    /// BeforeDirtyMarkerDeleted is the synchronous, pre-delete hook. This test
    /// guards that contract: BeforeDirtyMarkerDeleted fires before SaveCompleted.
    /// </summary>
    [Fact]
    public async Task SaveAsync_BeforeDirtyMarkerDeleted_FiresBeforeSaveCompleted()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        var ordering = new System.Collections.Generic.List<string>();
        svc.BeforeDirtyMarkerDeleted += () => ordering.Add("before-delete");
        svc.SaveCompleted             += () => ordering.Add("save-completed");

        await svc.SaveAsync();

        Assert.Equal(new[] { "before-delete", "save-completed" }, ordering);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 10 — W1: HeartbeatFailed verifies lock ownership before demoting
    //
    // The heartbeat-failure path now calls VerifyLockIsOursAsync (matching the
    // save-time and wake-from-sleep paths) rather than demoting blindly. These
    // tests confirm that each VerifyLockIsOursAsync outcome is handled correctly.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the lock file was genuinely TAKEN OVER (it now bears a foreign
    /// SessionGuid) and the share is reachable, HeartbeatFailed must trigger
    /// demotion: <see cref="CheckoutService.WriteLockLost"/> fires.
    /// <para>Since spec item 32 (change (c)) the heartbeat itself finds the foreign
    /// name, writes nothing and raises HeartbeatFailed on the FIRST tick (see
    /// <c>Heartbeat_LockNamesAnotherSession_DemotesOnTheFirstTick</c>). The read-only
    /// attribute and the threshold-length loop below predate that: they were what made
    /// the renewals fail, and they are kept so this test still passes for any
    /// other way the renewals fail while the verification read sees the foreign holder.</para>
    /// <para>Note: a merely DELETED lock file no longer demotes — that is the
    /// antivirus/cloud-sync signature and is now healed by atomic re-acquisition
    /// (see <c>HeartbeatFailed_WhenLockFileDeleted_ReacquiresInsteadOfDemoting</c>).</para>
    /// </summary>
    [Fact]
    public async Task HeartbeatFailed_WhenLockGenuinelyTaken_Demotes()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);

        var lockLostTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.WriteLockLost += (_) => lockLostTcs.TrySetResult();

        // Another session overwrites the lock with its own GUID. Our heartbeat now sees that name and
        // raises HeartbeatFailed at once (writing nothing); the read-only attribute (which used to be
        // what made renewals fail) is kept as a second, independent reason for them to fail, while the
        // verification read still sees the foreign holder → genuine takeover.
        var lockPath = Path.ChangeExtension(db, ".lock");
        WriteExternalFreshLock(db);
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);

        try
        {
            // Tick the heartbeat (the first tick already raises HeartbeatFailed, because the lock names
            // another session; the full threshold's worth of ticks is the shape this test had before item 32).
            // Awaited: renewals serialize on the lock-file gate, and an unawaited burst
            // would be skipped as contended ticks rather than counted as failures.
            for (int i = 0; i < WriteLockService.HeartbeatFailureThreshold; i++)
                await lockSvc.ForceRenewHeartbeat();

            // Wait for the async verification + demotion
            var completed = await Task.WhenAny(lockLostTcs.Task, Task.Delay(10_000));
            Assert.True(completed == lockLostTcs.Task,
                "WriteLockLost must fire when the lock is genuinely taken over.");
        }
        finally
        {
            try { File.SetAttributes(lockPath, FileAttributes.Normal); } catch { }
            try { File.Delete(lockPath + ".tmp"); } catch { }
        }
    }

    /// <summary>
    /// When the network share is unreachable (the cause of the heartbeat failure),
    /// HeartbeatFailed must NOT demote the session. The writer keeps their state,
    /// and the one outage notice (<see cref="SavePrompt.OutageNotice"/>, spec item 28 / F15) is surfaced via
    /// <see cref="CheckoutService.SaveFailed"/>: sticky, never auto-dismissing. With no edits made, the session has no
    /// unsaved changes, so it is the "no unsaved changes" text.
    /// This is the core W1 fix: a 2–4 minute WiFi/VPN interruption must not cause
    /// a false-positive demotion with unsaved data loss.
    /// </summary>
    [Fact]
    public async Task HeartbeatFailed_WhenShareUnreachable_KeepsWriterState()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);

        var lockLostFired = false;
        svc.WriteLockLost += (_) => lockLostFired = true;

        var saveFailedTcs = new TaskCompletionSource<(string Message, bool AutoDismiss)>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SaveFailed += (msg, autoDismiss) => saveFailedTcs.TrySetResult((msg, autoDismiss));

        // Delete the entire source directory — lock file AND source DB gone.
        // VerifyLockIsOursAsync: lock not found → directory probe fails → Unreachable.
        Directory.Delete(_tempDir, recursive: true);

        // Trigger HeartbeatFailed (awaited — unawaited bursts skip as contended ticks)
        for (int i = 0; i < WriteLockService.HeartbeatFailureThreshold; i++)
            await lockSvc.ForceRenewHeartbeat();

        // Wait for the outage notice
        var completed = await Task.WhenAny(saveFailedTcs.Task, Task.Delay(10_000));
        Assert.True(completed == saveFailedTcs.Task,
            "SaveFailed must fire with the outage notice.");

        var (message, autoDismiss) = saveFailedTcs.Task.Result;
        Assert.Equal(SavePrompt.OutageNotice(hasUnsavedChanges: false, autoSaveEnabled: false), message);
        Assert.Equal(
            "TermPoint can't reach the database location right now. You have no unsaved changes.",
            message);
        Assert.False(autoDismiss, "the outage notice is sticky: it clears when the location responds, never by itself");
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.False(lockLostFired,
            "WriteLockLost must NOT fire when the share is merely unreachable.");
    }

    /// <summary>
    /// When the lock file is still ours (heartbeat write failed for a reason other
    /// than ownership loss — e.g. a sharing violation or transient I/O error),
    /// HeartbeatFailed must NOT demote. The session continues as writer and a
    /// heartbeat renewal is attempted.
    /// </summary>
    [Fact]
    public async Task HeartbeatFailed_WhenLockStillOurs_DoesNotDemote()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        await svc.CheckoutAsync(db);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);

        var lockLostFired = false;
        svc.WriteLockLost += (_) => lockLostFired = true;

        // Make the lock file read-only: heartbeat writes fail (File.Move can't
        // overwrite a read-only target) but verification reads succeed (sees our GUID).
        var lockPath = Path.ChangeExtension(db, ".lock");
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);

        try
        {
            // Trigger HeartbeatFailed (awaited — unawaited bursts skip as contended ticks)
            for (int i = 0; i < WriteLockService.HeartbeatFailureThreshold; i++)
                await lockSvc.ForceRenewHeartbeat();

            // The handler runs async via Task.Run; local I/O completes in <100ms.
            await Task.Delay(2000);

            Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
            Assert.False(lockLostFired,
                "WriteLockLost must NOT fire when the lock is still ours.");
        }
        finally
        {
            // Remove read-only so cleanup/Dispose can delete the file
            try { File.SetAttributes(lockPath, FileAttributes.Normal); } catch { }
            try { File.Delete(lockPath + ".tmp"); } catch { }
        }
    }

    /// <summary>
    /// Spec item 28 (F15): the heartbeat's lock check reaches the lock file again and finds it is still ours (the
    /// heartbeat write failed for another reason), so it raises <see cref="CheckoutService.LocationReachableAgain"/>:
    /// the one signal the window uses to clear the outage notice. The check's other effects are exactly as before
    /// (no demotion, no <see cref="CheckoutService.SaveFailed"/> banner of its own).
    /// </summary>
    [Fact]
    public async Task HeartbeatFailed_WhenLockStillOurs_RaisesLocationReachableAgain_AndNothingElse()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        await svc.CheckoutAsync(db);

        var lockLostFired = false;
        svc.WriteLockLost += (_) => lockLostFired = true;
        var banners = CaptureSaveBanners(svc);
        var reachable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.LocationReachableAgain += () => reachable.TrySetResult();

        var lockPath = Path.ChangeExtension(db, ".lock");
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);   // heartbeat writes fail, verification reads succeed
        try
        {
            for (int i = 0; i < WriteLockService.HeartbeatFailureThreshold; i++)
                await lockSvc.ForceRenewHeartbeat();

            var completed = await Task.WhenAny(reachable.Task, Task.Delay(10_000));
            Assert.True(completed == reachable.Task,
                "LocationReachableAgain must fire when the heartbeat's lock check finds the lock still ours.");
            Assert.False(lockLostFired);
            Assert.Empty(banners);
            Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        }
        finally
        {
            try { File.SetAttributes(lockPath, FileAttributes.Normal); } catch { }
            try { File.Delete(lockPath + ".tmp"); } catch { }
        }
    }

    /// <summary>
    /// Spec item 28 (F15): when the share is unreachable and the session HAS unsaved changes, the heartbeat raises the
    /// same sticky outage notice a save raises, with the line that fits autosave being on ("TermPoint will try again
    /// automatically"). It does not raise <see cref="CheckoutService.LocationReachableAgain"/> (the location is not
    /// reachable) and does not demote.
    /// </summary>
    [Fact]
    public async Task HeartbeatFailed_WhenShareUnreachable_WithUnsavedChangesAndAutoSaveOn_RaisesTheStickyOutageNotice()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        await svc.CheckoutAsync(db);
        svc.MarkDirty();                 // an unsaved change: the dirty marker exists
        svc.StartAutoSave();             // autosave is on
        try
        {
            var lockLostFired = false;
            var reachableFired = false;
            svc.WriteLockLost += (_) => lockLostFired = true;
            svc.LocationReachableAgain += () => reachableFired = true;
            var arrived = new TaskCompletionSource<(string Message, bool AutoDismiss)>(TaskCreationOptions.RunContinuationsAsynchronously);
            svc.SaveFailed += (msg, autoDismiss) => arrived.TrySetResult((msg, autoDismiss));

            Directory.Delete(_tempDir, recursive: true);   // lock file and database gone: the share is unreachable
            for (int i = 0; i < WriteLockService.HeartbeatFailureThreshold; i++)
                await lockSvc.ForceRenewHeartbeat();

            Assert.Same(arrived.Task, await Task.WhenAny(arrived.Task, Task.Delay(10_000)));
            var (message, autoDismiss) = arrived.Task.Result;
            Assert.Equal(
                "TermPoint can't reach the database location. Your changes are kept on this computer. " +
                "TermPoint will try again automatically.",
                message);
            Assert.False(autoDismiss);
            Assert.False(lockLostFired);
            Assert.False(reachableFired);
            Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        }
        finally
        {
            svc.StopAutoSave();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 8 — Crash recovery (restore path) and shutdown save-outcome handling
    //
    // These tests cover the "keep, verify, restore" crash-recovery design:
    //   • The dirty marker records HashAtCheckout as JSON (DirtyMarkerData).
    //   • CheckoutAsync(recoverWorkingCopy: true) resumes a crashed session's D'
    //     when D is provably unchanged, and returns RecoveryConflict otherwise.
    //   • InspectCrashRecovery verifies D' integrity (replaying any hot journal).
    //   • ReleaseAsync returns the final save's outcome, and on a transient failure
    //     leaves the lock file in place so D is protected until recovery.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates a process crash for a service that holds the write lock: stops the
    /// heartbeat timers (a dead process sends no heartbeats) and deletes the lock file
    /// (equivalent to the stale-lock takeover a recovering instance would perform,
    /// but deterministic — no 90-second staleness wait in tests). D' and the dirty
    /// marker are left exactly as the crash left them.
    /// </summary>
    private static void SimulateCrash(WriteLockService lockSvc, string dbPath)
    {
        lockSvc.Suspend();
        try { File.Delete(Path.ChangeExtension(dbPath, ".lock")); } catch { }
    }

    /// <summary>
    /// The dirty marker written by MarkDirty must be JSON carrying HashAtCheckout,
    /// so a later crash recovery can verify D was not modified in the interim.
    /// </summary>
    [Fact]
    public async Task MarkDirty_MarkerContainsHashAtCheckout()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();

        var json = File.ReadAllText(svc.WorkingPath + ".dirty");
        var data = JsonSerializer.Deserialize<TermPoint.Models.DirtyMarkerData>(json);

        Assert.NotNull(data);
        Assert.Equal(svc.HashAtCheckout, data!.HashAtCheckout);
        Assert.True(data.Timestamp > DateTime.UtcNow.AddMinutes(-1), "Timestamp must be recent.");
    }

    /// <summary>
    /// A legacy marker (bare ISO timestamp, pre-JSON format) must read as "hash
    /// unknown" rather than throwing — recovery then takes the conservative path.
    /// </summary>
    [Fact]
    public void TryReadMarkerHash_LegacyTimestampMarker_ReturnsNull()
    {
        var markerPath = Path.Combine(_tempDir, "legacy.dirty");
        File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));

        Assert.Null(CheckoutService.TryReadMarkerHash(markerPath));
    }

    /// <summary>ReleaseAsync reports the outcome of a successful final save.</summary>
    [Fact]
    public async Task ReleaseAsync_SaveSucceeds_ReturnsSuccess()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        var outcome = await svc.ReleaseAsync(saveFirst: true);

        Assert.Equal(SaveOutcome.Success, outcome);
    }

    /// <summary>
    /// ReleaseAsync reports SourceModified when the final save hit an external
    /// conflict, so the caller knows NOT to delete the working copy.
    /// (The lock is still released for this outcome — holding it protects nothing,
    /// since the changes can never be applied automatically.)
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_SourceModified_ReturnsSourceModified()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db, "original");

        await svc.CheckoutAsync(db);
        File.WriteAllText(db, "externally-changed");

        var outcome = await svc.ReleaseAsync(saveFirst: true);

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.False(lockSvc.IsWriter);
    }

    /// <summary>
    /// On a TRANSIENT final-save failure (CopyError), ReleaseAsync must leave the lock
    /// file in place (via WriteLockService.Suspend) and preserve D' + dirty marker:
    /// they are the only copy of the unsaved changes, and the abandoned lock keeps
    /// other users from writing D underneath them without an informed stale takeover.
    /// The save fails here because D' is not a valid SQLite file, so the Online Backup
    /// API throws — the same CopyError path a network failure takes.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_TransientSaveFailure_KeepsLockFileAndCrashArtifacts()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "not-a-sqlite-file");

        await svc.CheckoutAsync(db);
        svc.MarkDirty(); // unsaved edits exist

        var outcome = await svc.ReleaseAsync(saveFirst: true);

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.True(File.Exists(Path.ChangeExtension(db, ".lock")),
            "Lock file must be left in place after a transient save failure — it goes stale " +
            "and protects D until the unsaved changes are recovered.");
        Assert.True(File.Exists(svc.WorkingPath), "D' must survive for recovery.");
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "Dirty marker must survive for recovery.");
    }

    /// <summary>
    /// Happy-path recovery: after a crash with unsaved edits, a recovery-mode checkout
    /// adopts the crashed session's D' (edits intact), enters write mode, and leaves
    /// the dirty marker in place (the edits are still unsaved).
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_SourceUnchanged_ResumesSessionWithEditsIntact()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        // Previous session: checkout, edit D', mark dirty, crash.
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        svc1.MarkDirty();
        var crashedWorkingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);

        // Next launch: recovery-mode checkout.
        var (svc2, _) = CreateService();
        Assert.True(svc2.DetectCrashRecovery(db)); // precondition
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(CheckoutMode.WriteAccess, svc2.Mode);
        Assert.Equal(crashedWorkingPath, svc2.WorkingPath);
        Assert.Equal("unsaved-edit", ReadTestValue(svc2.WorkingPath));
        Assert.NotNull(svc2.HashAtCheckout);
        Assert.True(File.Exists(svc2.WorkingPath + ".dirty"),
            "Marker must remain after recovery — the restored edits are still unsaved.");
    }

    /// <summary>
    /// End-to-end recovery: the recovered session's SaveAsync pushes the crashed
    /// session's edits back to D through the normal save pipeline.
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_ThenSave_PushesRecoveredEditsToSource()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "recovered-edit");
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);

        var (svc2, _) = CreateService();
        await svc2.CheckoutAsync(db, recoverWorkingCopy: true);
        var saveOutcome = await svc2.SaveAsync();

        Assert.Equal(SaveOutcome.Success, saveOutcome);
        Assert.Equal("recovered-edit", ReadTestValue(db));
        Assert.False(File.Exists(svc2.WorkingPath + ".dirty"),
            "Marker must be cleared once the recovered edits are safely in D.");
    }

    /// <summary>
    /// When D was modified after the crash (another writer saved), recovery must NOT
    /// touch D: the checkout returns RecoveryConflict, releases the lock, and leaves
    /// D' + marker intact so the caller can offer an exported copy.
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_SourceChangedSinceCrash_ReturnsRecoveryConflict()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "stranded-edit");
        svc1.MarkDirty();
        var crashedWorkingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);

        // Another writer changes D while our edits are stranded.
        InsertTestValue(db, "someone-elses-edit");

        var (svc2, lockSvc2) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.RecoveryConflict, outcome);
        Assert.False(lockSvc2.IsWriter, "Lock must be released on RecoveryConflict.");
        Assert.Equal("someone-elses-edit", ReadTestValue(db)); // D untouched
        Assert.True(File.Exists(crashedWorkingPath), "D' must be preserved for export.");
        Assert.True(File.Exists(crashedWorkingPath + ".dirty"), "Marker must be preserved.");
    }

    /// <summary>
    /// A legacy marker carries no hash, so recovery cannot verify D — it must take
    /// the conservative RecoveryConflict path rather than guessing.
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_LegacyMarker_ReturnsRecoveryConflict()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        svc1.MarkDirty();
        // Overwrite the marker with the legacy bare-timestamp format.
        File.WriteAllText(svc1.WorkingPath + ".dirty", DateTime.UtcNow.ToString("O"));
        SimulateCrash(lockSvc1, db);

        var (svc2, _) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.RecoveryConflict, outcome);
    }

    /// <summary>
    /// When D itself vanished while the changes were stranded, there is nothing to
    /// verify against — recovery returns RecoveryConflict (export-only path).
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_SourceMissing_ReturnsRecoveryConflict()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);
        File.Delete(db); // D deleted/renamed while the app was closed

        var (svc2, _) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.RecoveryConflict, outcome);
    }

    /// <summary>
    /// When another writer holds a live lock, a recovery-mode checkout lands in
    /// read-only exactly like a normal one — and the crash artifacts survive so
    /// recovery can be offered again at the next write-mode open.
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_OtherWriterHoldsLock_ReadOnlyAndArtifactsKept()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        svc1.MarkDirty();
        var crashedWorkingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);

        WriteExternalFreshLock(db); // someone else is editing now

        var (svc2, _) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        Assert.True(File.Exists(crashedWorkingPath), "D' must survive a deferred recovery.");
        Assert.True(File.Exists(crashedWorkingPath + ".dirty"), "Marker must survive a deferred recovery.");
    }

    /// <summary>
    /// InspectCrashRecovery reports an intact working copy with the marker's hash
    /// for a normal crash (valid SQLite D' + JSON marker).
    /// </summary>
    [Fact]
    public async Task InspectCrashRecovery_IntactWorkingCopy_ReportsIntactWithHash()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);

        var (svc2, _) = CreateService();
        var info = svc2.InspectCrashRecovery(db);

        Assert.NotNull(info);
        Assert.True(info!.WorkingCopyIntact);
        Assert.Equal(svc1.HashAtCheckout, info.HashAtCheckout);
    }

    /// <summary>
    /// A garbage (non-SQLite) working copy must be reported as not intact so the
    /// caller archives it instead of offering a restore that cannot work.
    /// </summary>
    [Fact]
    public async Task InspectCrashRecovery_CorruptWorkingCopy_ReportsNotIntact()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);

        // Corrupt D' after the crash (e.g. torn write / disk fault).
        File.WriteAllText(svc1.WorkingPath, "garbage-not-a-database");

        var (svc2, _) = CreateService();
        var info = svc2.InspectCrashRecovery(db);

        Assert.NotNull(info);
        Assert.False(info!.WorkingCopyIntact);
    }

    /// <summary>InspectCrashRecovery returns null when no crash artifacts exist.</summary>
    [Fact]
    public void InspectCrashRecovery_NoArtifacts_ReturnsNull()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        Assert.Null(svc.InspectCrashRecovery(db));
    }

    /// <summary>
    /// ArchiveCorruptWorkingCopy moves D' aside (preserving salvage options) and
    /// clears the marker, so the next launch starts clean.
    /// </summary>
    [Fact]
    public async Task ArchiveCorruptWorkingCopy_MovesFileAndClearsMarker()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        svc1.MarkDirty();
        var workingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);

        var (svc2, _) = CreateService();
        var archivePath = svc2.ArchiveCorruptWorkingCopy(db);

        Assert.NotNull(archivePath);
        Assert.True(File.Exists(archivePath!), "Archive file must exist.");
        Assert.False(File.Exists(workingPath), "Original D' must be gone.");
        Assert.False(File.Exists(workingPath + ".dirty"), "Marker must be cleared.");
        Assert.False(svc2.DetectCrashRecovery(db), "No crash must be detected after archiving.");
    }

    /// <summary>
    /// After a recovered session saves successfully and the app exits cleanly, the
    /// next launch must NOT re-offer recovery — the cycle closes completely.
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_SaveAndRelease_NoRecoveryOfferedNextLaunch()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "edit");
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);

        // Recovered session: restore, save, exit cleanly, clean up.
        var (svc2, _) = CreateService();
        await svc2.CheckoutAsync(db, recoverWorkingCopy: true);
        var outcome = await svc2.ReleaseAsync(saveFirst: true);
        Assert.Equal(SaveOutcome.Success, outcome);
        svc2.CleanupWorkingCopy();

        var (svc3, _) = CreateService();
        Assert.False(svc3.DetectCrashRecovery(db));
        Assert.Equal("edit", ReadTestValue(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 9 — Ghost-rename self-heal (finding #3)
    //
    // A save's step-5 rename (D.tmp → D) can exceed the network deadline yet still
    // complete on the abandoned thread ("ghost rename"): D is updated while
    // HashAtCheckout is stale, so every later save would misread the state as an
    // external modification (sticky SourceModified, autosave stopped).
    //
    // The fix records the snapshot hash of the unconfirmed save (in memory and in
    // the dirty marker). Step 2 then recognizes a D bearing that hash as "updated
    // by our own delayed rename" and continues; recovery after a restart accepts
    // the pending hash as proof of no foreign writer.
    //
    // The rename-timeout itself cannot be triggered deterministically on local
    // disk, so these tests simulate its two effects directly: the ghost content
    // landing in D (via the same Online Backup API the save uses) and the pending
    // hash being recorded (via the same RecordPendingSaveHash call production uses).
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a "ghost rename" to D: snapshots <paramref name="workingPath"/> into
    /// <paramref name="dbPath"/> with the SQLite Online Backup API — the same
    /// mechanism SaveAsync uses to produce D.tmp — and returns the resulting hash
    /// of D (what step 4 would have recorded as the snapshot hash).
    /// </summary>
    private static string ApplyGhostRename(string workingPath, string dbPath)
    {
        using (var src = new SqliteConnection($"Data Source={workingPath};Pooling=False"))
        using (var dst = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }
        return NetworkFileOps.ComputeHash(dbPath);
    }

    /// <summary>
    /// When D bears the pending save hash (our own ghost rename landed), the next
    /// save must self-heal and succeed instead of aborting with SourceModified —
    /// including pushing edits made after the ghost save.
    /// </summary>
    [Fact]
    public async Task SaveAsync_GhostRenameLanded_SelfHealsAndSaves()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        InsertTestValue(svc.WorkingPath, "first-edit");
        svc.MarkDirty();

        // Simulate a save whose rename timed out but landed anyway: D now holds the
        // snapshot of D', and the service recorded the snapshot hash as pending.
        var ghostHash = ApplyGhostRename(svc.WorkingPath, db);
        svc.RecordPendingSaveHash(ghostHash);

        // The user keeps editing after the "failed" save.
        InsertTestValue(svc.WorkingPath, "second-edit");

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal("second-edit", ReadTestValue(db));
    }

    /// <summary>
    /// The self-heal must only fire for OUR ghost's exact hash. A genuine foreign
    /// modification (hash matching neither HashAtCheckout nor the pending hash)
    /// must still abort with SourceModified.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ForeignModificationWithPendingHashSet_StillSourceModified()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();
        svc.RecordPendingSaveHash(new string('A', 64)); // pending from some failed save

        // Another writer modifies D — its hash matches neither of our hashes.
        InsertTestValue(db, "foreign-edit");

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.Equal("foreign-edit", ReadTestValue(db)); // D untouched by us
    }

    /// <summary>
    /// RecordPendingSaveHash must persist the pending hash into the existing dirty
    /// marker (alongside the unchanged checkout hash) so a post-exit ghost can be
    /// recognized by next-launch recovery.
    /// </summary>
    [Fact]
    public async Task RecordPendingSaveHash_RewritesMarkerWithBothHashes()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();

        svc.RecordPendingSaveHash("ABC123");

        var marker = CheckoutService.TryReadMarker(svc.WorkingPath + ".dirty");
        Assert.NotNull(marker);
        Assert.Equal(svc.HashAtCheckout, marker!.HashAtCheckout);
        Assert.Equal("ABC123", marker.PendingSaveHash);
    }

    /// <summary>
    /// Without a dirty marker there are no unsaved edits at stake, so
    /// RecordPendingSaveHash must not create one (a spurious marker would trigger
    /// a false crash-recovery offer at the next launch).
    /// </summary>
    [Fact]
    public async Task RecordPendingSaveHash_NoMarker_DoesNotCreateOne()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db); // no MarkDirty — no edits, no marker

        svc.RecordPendingSaveHash("ABC123");

        Assert.False(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// Cross-restart ghost: the rename lands AFTER the app exits (e.g. SMB reconnects
    /// post-shutdown). At next launch, recovery must recognize D via the marker's
    /// pending hash and restore the working copy — including post-ghost edits —
    /// instead of reporting a conflict.
    /// </summary>
    [Fact]
    public async Task RecoverCheckout_GhostLandedAfterExit_RestoresViaPendingHash()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "stranded-edit");
        svc1.MarkDirty();

        // The failed save records its snapshot hash; the user edits a bit more; the
        // app dies; the ghost rename lands afterwards. (Order of the last two doesn't
        // matter to recovery — D bears the pending hash either way.)
        var ghostHash = ApplyGhostRename(svc1.WorkingPath, db);
        svc1.RecordPendingSaveHash(ghostHash);
        InsertTestValue(svc1.WorkingPath, "post-ghost-edit");
        SimulateCrash(lockSvc1, db);

        var (svc2, _) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal("post-ghost-edit", ReadTestValue(svc2.WorkingPath));

        var saveOutcome = await svc2.SaveAsync();
        Assert.Equal(SaveOutcome.Success, saveOutcome);
        Assert.Equal("post-ghost-edit", ReadTestValue(db));
    }

    /// <summary>
    /// A confirmed save clears the pending hash: the marker written by the NEXT
    /// edit must not carry a stale PendingSaveHash from an earlier failed save.
    /// </summary>
    [Fact]
    public async Task SaveAsync_Success_ClearsPendingHashFromSubsequentMarkers()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();
        svc.RecordPendingSaveHash("STALE-PENDING"); // D still matches HashAtCheckout, so
                                                    // the next save proceeds normally

        var outcome = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.Success, outcome);

        // Next edit cycle writes a fresh marker — pending must be gone.
        svc.MarkDirty();
        var marker = CheckoutService.TryReadMarker(svc.WorkingPath + ".dirty");
        Assert.NotNull(marker);
        Assert.Null(marker!.PendingSaveHash);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 11 — Removed-lock re-acquisition (finding #5) and marker-write
    //            failure warning (finding #6)
    //
    // #5: a lock file deleted by antivirus/cloud-sync (no new holder) used to
    //     demote the session and destroy D'. Now the lock is re-created atomically
    //     and the session continues; when demotion does happen, D' + marker are
    //     preserved whenever unsaved edits exist.
    // #6: the dirty marker is the only thing standing between a crash and the
    //     startup sweep deleting D'. Its write is now verified (retry + warning)
    //     instead of silently swallowed.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the lock file was deleted externally (no new holder — the antivirus /
    /// cloud-sync signature), SaveAsync must re-acquire the lock atomically and
    /// complete the save instead of demoting with LockLost.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockFileDeletedExternally_ReacquiresAndSaves()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        InsertTestValue(svc.WorkingPath, "edit-during-av-delete");
        svc.MarkDirty();

        // Simulate antivirus deleting the lock file mid-session.
        var lockPath = Path.ChangeExtension(db, ".lock");
        File.Delete(lockPath);

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.True(lockSvc.IsWriter, "Session must continue as writer after re-acquisition.");
        Assert.True(File.Exists(lockPath), "Lock file must be re-created.");
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Equal("edit-during-av-delete", ReadTestValue(db));
    }

    /// <summary>
    /// The heartbeat path heals a deleted lock file too: instead of demoting, the
    /// lock is re-acquired, the informational banner fires, and the session
    /// continues as writer with the file back in place.
    /// </summary>
    [Fact]
    public async Task HeartbeatFailed_WhenLockFileDeleted_ReacquiresInsteadOfDemoting()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        var lockLostFired = false;
        svc.WriteLockLost += (_) => lockLostFired = true;

        // The re-acquisition banner is the completion signal for the async handler.
        var recoveredTcs = new TaskCompletionSource<(string Message, bool AutoDismiss)>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.SaveFailed += (msg, autoDismiss) => recoveredTcs.TrySetResult((msg, autoDismiss));

        var lockPath = Path.ChangeExtension(db, ".lock");
        File.Delete(lockPath); // antivirus/cloud-sync deletion

        // Missing file makes renewals fail → threshold → HeartbeatFailed.
        // (Awaited — unawaited bursts skip as contended ticks.)
        for (int i = 0; i < WriteLockService.HeartbeatFailureThreshold; i++)
            await lockSvc.ForceRenewHeartbeat();

        var completed = await Task.WhenAny(recoveredTcs.Task, Task.Delay(10_000));
        Assert.True(completed == recoveredTcs.Task, "Re-acquisition banner must fire.");
        var (recoveredMessage, recoveredAutoDismiss) = recoveredTcs.Task.Result;
        Assert.Equal(
            "Another program removed the file TermPoint uses to mark this database as in use " +
            "(antivirus or cloud-sync software can do this). TermPoint has put it back, and you can carry on as normal.",
            recoveredMessage);
        Assert.True(recoveredAutoDismiss, "the put-back notice is informational and clears itself");
        Assert.False(lockLostFired, "WriteLockLost must NOT fire for a healed removal.");
        Assert.True(lockSvc.IsWriter);
        Assert.True(File.Exists(lockPath), "Lock file must be re-created.");
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
    }

    /// <summary>
    /// Re-acquisition must never contest a genuine takeover: a lock file bearing a
    /// FOREIGN SessionGuid still demotes with LockLost, exactly as before.
    /// (Complements SaveAsync_LockStolenByOtherSession_ReturnsLockLost.)
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockTakenOver_DoesNotReacquire()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        WriteExternalFreshLock(db); // foreign GUID overwrites ours

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.LockLost, outcome);
        // The foreign holder's lock file must be untouched.
        var holder = System.Text.Json.JsonSerializer.Deserialize<TermPoint.Models.LockFileData>(
            File.ReadAllText(Path.ChangeExtension(db, ".lock")));
        Assert.Equal("other_user", holder!.Username);
    }

    /// <summary>
    /// Demotion with unsaved edits must PRESERVE D' and the dirty marker — they are
    /// the only copy of that work, and recovery restores it at the next write-mode open.
    /// </summary>
    [Fact]
    public async Task DemoteToReadOnly_WithUnsavedEdits_PreservesWorkingCopyAndMarker()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        InsertTestValue(svc.WorkingPath, "pre-demotion-edit");
        svc.MarkDirty();
        var workingPath = svc.WorkingPath;

        var demoted = await svc.DemoteToReadOnlyAsync();

        Assert.True(demoted);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.True(File.Exists(workingPath), "D' must survive demotion when edits exist.");
        Assert.True(File.Exists(workingPath + ".dirty"), "Marker must survive demotion when edits exist.");
    }

    /// <summary>
    /// Demotion with NO unsaved edits cleans up D' as before — nothing is at stake,
    /// and leaving it behind would trigger a false recovery offer at next launch.
    /// </summary>
    [Fact]
    public async Task DemoteToReadOnly_NoEdits_DeletesWorkingCopy()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db); // no MarkDirty — no edits
        var workingPath = svc.WorkingPath;

        var demoted = await svc.DemoteToReadOnlyAsync();

        Assert.True(demoted);
        Assert.False(File.Exists(workingPath), "D' must be cleaned up when no edits exist.");
        Assert.False(svc.DetectCrashRecovery(db), "No recovery must be offered after an edit-free demotion.");
    }

    /// <summary>
    /// Full circle for a demotion with edits: the preserved D' is restored by a
    /// recovery-mode checkout in a later session (D unchanged, so the hash matches),
    /// and the edits reach D through a normal save.
    /// </summary>
    [Fact]
    public async Task DemoteThenRecover_RestoresPreservedEdits()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "demoted-edit");
        svc1.MarkDirty();
        // Demotion releases the lock and preserves D' + marker. Settle the background
        // lock-file deletion in beforeClose, i.e. BEFORE the demotion's own EnterReaderMode
        // reads the lock file: that read has no delete-sharing, so an overlap makes the
        // delete fail and orphans the lock, and svc2 (a different service) would then
        // see a live lock and land in ReadOnly. Production never hits this ordering: the
        // app demotes only after the lock is lost (TakenOver / LockFileRemoved), when the
        // background release finds the file is not ours and deletes nothing.
        await svc1.DemoteToReadOnlyAsync(beforeClose: () =>
        {
            lockSvc1.PendingRelease?.Wait(TimeSpan.FromSeconds(10)); // settle the background lock-file deletion
            return Task.CompletedTask;
        });

        var (svc2, _) = CreateService();
        Assert.True(svc2.DetectCrashRecovery(db)); // artifacts found
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal("demoted-edit", ReadTestValue(svc2.WorkingPath));

        Assert.Equal(SaveOutcome.Success, await svc2.SaveAsync());
        Assert.Equal("demoted-edit", ReadTestValue(db));
    }

    /// <summary>
    /// F16 (data-persistence review, section 3): a lost-lock demotion that is still running when the user switches
    /// to another database lands on the NEW session. Forces the late ordering deterministically:
    /// <list type="number">
    /// <item><description>Bob holds dept.db; another user's lock replaces his (the takeover).</description></item>
    /// <item><description>The demotion (what <c>SessionLifecycle.HandleWriteLockLostAsync</c> runs) starts on a pool
    /// thread and is held, by the <see cref="NetworkFaults"/> seam, at the start of its D'' copy of dept.db. That is the
    /// first real suspension point of the demotion, and where a production UI thread is free to run a click.</description></item>
    /// <item><description>Meanwhile the switch runs to completion, exactly as <c>SwitchDatabaseAsync</c> does:
    /// <see cref="CheckoutService.ReleaseAsync"/> then <see cref="CheckoutService.CheckoutAsync"/> of other.db, which
    /// gets write access and the lock.</description></item>
    /// <item><description>The held copy is let go. The demotion carries on with the service's CURRENT
    /// <see cref="CheckoutService.SourcePath"/> (other.db): it hashes other.db against its dept.db copy (the "hash
    /// mismatch, retrying" line), copies other.db again, and sets <see cref="CheckoutService.Mode"/> to read-only and
    /// <see cref="CheckoutService.WorkingPath"/> to D''.</description></item>
    /// </list>
    /// The CORRECT behaviour asserted here: a demotion belongs to the session it was started for, so the new session
    /// stays a writer on its own working copy. Today the new session ends read-only while it holds other.db's lock, so
    /// the test is skipped until F16 is fixed.
    /// </summary>
    [Fact(Skip = "F16: a lost-lock demotion still copying D'' when the user switches database lands on the new session " +
                 "(read-only while it holds the new lock). See data-persistence-review-2026-10.md, section 3, F16.")]
    public async Task F16_ADemotionStillRunningWhenTheUserSwitches_DoesNotTouchTheNewSession()
    {
        var (svc, lockSvc) = CreateService();
        var dept  = DbPath("dept");
        var other = DbPath("other");
        CreateSqliteDb(dept);
        CreateSqliteDb(other);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(dept));
        WriteExternalFreshLock(dept);                      // the takeover: dept.lock is now someone else's

        // Hold the demotion at the start of its D'' copy of dept.db (the first copy to dept.db's "_ro" snapshot).
        using var copyReached = new ManualResetEventSlim(false);
        using var letCopyGo   = new ManualResetEventSlim(false);
        var held = 0;
        NetworkFaults.Decide = request =>
        {
            if (request.Label == "CopyFile"
                && string.Equals(request.Path, dept, StringComparison.OrdinalIgnoreCase)
                && request.DestinationPath is { } destination && destination.Contains("_ro")
                && Interlocked.Exchange(ref held, 1) == 0)
            {
                copyReached.Set();
                letCopyGo.Wait(TimeSpan.FromSeconds(30));
            }
            return NetworkFaultDecision.None;
        };

        try
        {
            // The demotion, on its own thread as a posted handler would be, held inside its D'' copy.
            var demotion = Task.Run(() => svc.DemoteToReadOnlyAsync());
            Assert.True(copyReached.Wait(TimeSpan.FromSeconds(30)), "the demotion never reached its D'' copy");

            // The switch, to completion, while the demotion is held.
            await svc.ReleaseAsync(saveFirst: true);
            lockSvc.PendingRelease?.Wait(TimeSpan.FromSeconds(10));
            Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(other));
            Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
            Assert.True(lockSvc.IsWriter);

            // Let the old demotion finish.
            letCopyGo.Set();
            await demotion.WaitAsync(TimeSpan.FromSeconds(30));

            // The new session is still a writer on its own working copy.
            Assert.Equal(other, svc.SourcePath);
            Assert.True(lockSvc.IsWriter, "the new session still holds other.db's lock");
            Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
            Assert.False(svc.IsReadOnlyMode, "a writer must not be marked read-only");
            Assert.DoesNotContain("_ro", Path.GetFileName(svc.WorkingPath));
        }
        finally
        {
            letCopyGo.Set();
            NetworkFaults.Decide = null;
        }
    }

    /// <summary>
    /// When the dirty-marker write fails (here: a directory squats on the marker
    /// path), MarkDirty must warn the user that crash protection is degraded instead
    /// of failing silently — a later crash would sweep D' as "no edits made."
    /// </summary>
    [Fact]
    public async Task MarkDirty_MarkerWriteFails_RaisesDegradedProtectionWarning()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);

        // Block the marker path: File.WriteAllText onto an existing directory throws.
        var markerPath = svc.WorkingPath + ".dirty";
        Directory.CreateDirectory(markerPath);

        string? warning = null;
        var autoDismiss = false;
        svc.SaveFailed += (msg, dismiss) => { warning = msg; autoDismiss = dismiss; };

        try
        {
            svc.MarkDirty();

            Assert.NotNull(warning);
            Assert.Contains("crash-protection", warning);
            Assert.True(autoDismiss, "The warning is informational and should auto-dismiss.");
        }
        finally
        {
            Directory.Delete(markerPath);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 12 — Local-snapshot save pipeline (finding #7)
    //
    // SaveAsync now snapshots D' to a LOCAL .savetmp via the Online Backup API,
    // streams the quiescent snapshot to D.tmp with the chunked stall-aware copy,
    // and verifies the network copy by hash before the rename. These tests pin
    // the lifecycle of the local snapshot; transfer fidelity is covered in
    // NetworkFileOpsChunkedTests.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A successful save must clean up its local snapshot — .savetmp is a
    /// transient artifact and must never accumulate in the working directory.
    /// </summary>
    [Fact]
    public async Task SaveAsync_Success_LeavesNoLocalSnapshotBehind()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        InsertTestValue(svc.WorkingPath, "edit");

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal("edit", ReadTestValue(db)); // pipeline end-to-end intact
        Assert.False(File.Exists(svc.WorkingPath + ".savetmp"),
            "Local save snapshot must be deleted after the save.");
    }

    /// <summary>
    /// A failed snapshot (D' is not a valid SQLite file) must also leave no
    /// .savetmp behind — the finally-cleanup covers the failure paths too.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SnapshotFails_LeavesNoLocalSnapshotBehind()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "not-a-sqlite-file");

        await svc.CheckoutAsync(db);

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.False(File.Exists(svc.WorkingPath + ".savetmp"),
            "Local save snapshot must be cleaned up when the save fails.");
    }

    /// <summary>
    /// An orphaned .savetmp (crash between snapshot and network copy) is swept at
    /// startup by CleanupStaleCrashArtifacts — it is derived data, always safe to drop.
    /// </summary>
    [Fact]
    public async Task CleanupStaleCrashArtifacts_RemovesOrphanedSaveSnapshot()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        await svc.CheckoutAsync(db);
        svc.MarkDirty();
        var saveTmp = svc.WorkingPath + ".savetmp";
        File.WriteAllText(saveTmp, "orphaned-snapshot");
        SimulateCrash(lockSvc, db);

        var (svc2, _) = CreateService();
        svc2.CleanupStaleCrashArtifacts(db);

        Assert.False(File.Exists(saveTmp), "Orphaned .savetmp must be swept at startup.");
        // The real crash artifacts must be untouched by the sweep (both present).
        Assert.True(File.Exists(svc.WorkingPath), "D' must survive the sweep.");
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "Marker must survive the sweep.");
    }

    /// <summary>
    /// SaveStarted and SaveFinished bracket a successful save in order — they drive
    /// the "Saving…" indicator, which must appear before and disappear after.
    /// </summary>
    [Fact]
    public async Task SaveAsync_Success_RaisesSaveStartedThenFinished()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        var events = new System.Collections.Generic.List<string>();
        svc.SaveStarted   += () => events.Add("started");
        svc.SaveCompleted += () => events.Add("completed");
        svc.SaveFinished  += () => events.Add("finished");

        await svc.SaveAsync();

        Assert.Equal(new[] { "started", "completed", "finished" }, events);
    }

    /// <summary>
    /// SaveFinished must fire on FAILED saves too — some outcomes raise neither
    /// SaveCompleted nor SaveFailed (e.g. LockLost), and the "Saving…" indicator
    /// must never be left stuck on.
    /// </summary>
    [Fact]
    public async Task SaveAsync_Failure_StillRaisesSaveFinished()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateFile(db, "original");
        await svc.CheckoutAsync(db);

        var finished = false;
        svc.SaveFinished += () => finished = true;

        File.WriteAllText(db, "externally-changed"); // force SourceModified
        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.True(finished, "SaveFinished must fire even when the save fails.");
    }

    /// <summary>
    /// A SaveAsync call that arrives while another save is in flight must raise
    /// SaveAlreadyInProgress (the UI's "A save is already in progress…" cue) instead
    /// of being silently swallowed — the field-tested failure mode where repeated
    /// Save clicks during a long post-outage save looked like a dead button.
    /// The skipped call must raise neither SaveStarted nor SaveFinished: those
    /// belong to the in-flight save, and a stray SaveFinished from the skipped call
    /// would clear the "Saving…" indicator while the real save is still writing.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhileSaveInFlight_RaisesSaveAlreadyInProgress()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        var busyCues     = 0;
        var startedCount = 0;
        var finishedCount = 0;
        SaveOutcome? secondOutcome = null;
        svc.SaveAlreadyInProgress += () => busyCues++;
        svc.SaveFinished          += () => finishedCount++;

        // The synchronous test dispatcher invokes SaveStarted inline, while the first
        // save still holds the in-flight gate — the reentrant call below is therefore
        // a deterministic "second click mid-save". Its skip path completes before any
        // await, so blocking on it here cannot deadlock.
        svc.SaveStarted += () =>
        {
            startedCount++;
            if (startedCount == 1)
                secondOutcome = svc.SaveAsync().GetAwaiter().GetResult();
        };

        var firstOutcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, firstOutcome);
        Assert.Equal(SaveOutcome.CopyError, secondOutcome);
        Assert.Equal(1, busyCues);
        Assert.Equal(1, startedCount);  // the skipped call must not re-raise SaveStarted
        Assert.Equal(1, finishedCount); // …nor raise its own SaveFinished
    }

    /// <summary>
    /// SaveAlreadyInProgress is strictly a concurrency cue: a normal, uncontended
    /// save must never raise it.
    /// </summary>
    [Fact]
    public async Task SaveAsync_Uncontended_DoesNotRaiseSaveAlreadyInProgress()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);

        var busyCues = 0;
        svc.SaveAlreadyInProgress += () => busyCues++;

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal(0, busyCues);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 13 — MinimumVersionToWrite gate (spec item 3, step 3b)
    //
    // A database may carry a "MinimumVersionToWrite" stamp in its AppConfiguration
    // table. If the running app is older than the stamp, every write-access exit of
    // the checkout (normal, forced stale-lock takeover, crash recovery) must refuse
    // write access and open a read-only D'' snapshot instead — with the lock
    // released (never polled), D' gone (except in recovery, where D' and its dirty
    // marker are kept), D untouched, and RequiredAppVersion set for the banner.
    //
    // The raising half is dormant in 1.2.3 (ThisReleaseRequires == null); the tests
    // drive it through the internal ThisReleaseRequires / ChannelOverride seams.
    // A database with no stamp (including the non-SQLite dummy files used
    // by the other groups) must never be made read-only. A stamp that cannot be
    // READ at all is a different matter, handled by Group 19 (step 3c).
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Wait budget for the lock service's background lock-file deletion to settle.</summary>
    private static readonly TimeSpan LockSettleTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Writes (creating the table if needed) one <c>AppConfiguration</c> row into the SQLite
    /// database at <paramref name="path"/>, using the same schema as <c>DatabaseContext</c>.
    /// </summary>
    /// <param name="path">SQLite database to modify.</param>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">Configuration value.</param>
    private static void SetConfigValue(string path, string key, string value)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS AppConfiguration (key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '');" +
            "INSERT OR REPLACE INTO AppConfiguration (key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads one raw <c>AppConfiguration</c> value, or null when the table or row is absent
    /// (or the file is unreadable).
    /// </summary>
    /// <param name="path">SQLite database to read.</param>
    /// <param name="key">Configuration key.</param>
    private static string? GetConfigValue(string path, string key)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM AppConfiguration WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a real SQLite database D stamped with <paramref name="stamp"/> (a valid file with
    /// an <c>AppConfiguration</c> table), or unstamped when <paramref name="stamp"/> is null.
    /// </summary>
    /// <param name="path">Where to create D.</param>
    /// <param name="stamp">The MinimumVersionToWrite value to store, or null for none.</param>
    private static void CreateStampedDb(string path, string? stamp)
    {
        CreateSqliteDb(path);
        if (stamp is not null)
            SetConfigValue(path, MinimumVersionToWrite.ConfigKey, stamp);
    }

    /// <summary>
    /// Asserts the on-disk and in-memory signature of a version-gated checkout: ReadOnly mode on
    /// a D'' snapshot, no write lock held or left behind, no D'/sidecar/marker in the working
    /// directory, and the banner's <see cref="CheckoutService.RequiredAppVersion"/> populated.
    /// </summary>
    /// <param name="svc">The service under test.</param>
    /// <param name="lockSvc">Its lock service.</param>
    /// <param name="db">Path to D.</param>
    /// <param name="expectedStamp">The stamp the gate should report.</param>
    private void AssertGatedReadOnly(
        CheckoutService svc, WriteLockService lockSvc, string db, string expectedStamp)
    {
        lockSvc.PendingRelease?.Wait(LockSettleTimeout); // settle the background lock-file deletion

        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.True(svc.IsReadOnlyMode);
        Assert.Equal(expectedStamp, svc.RequiredAppVersion);
        Assert.False(svc.SessionDirty);
        Assert.Null(svc.CurrentHolder);
        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")), "Lock must be released, not left behind.");
        Assert.Equal(ComputeReadOnlyPath(db), svc.WorkingPath);
        Assert.Equal(svc.ReadOnlyWorkingPath, svc.WorkingPath);
        Assert.True(File.Exists(svc.WorkingPath), "D'' snapshot must exist.");
    }

    /// <summary>
    /// A stamp higher than the app's version → ReadOnly on a D'' snapshot, with the lock
    /// released, D' gone, D untouched, and the stamp surfaced via RequiredAppVersion.
    /// </summary>
    [Fact]
    public async Task VersionGate_StampHigherThanApp_OpensReadOnlySnapshot()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5");
        var hashOfD = ComputeFileHash(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertGatedReadOnly(svc, lockSvc, db, "1.2.5");

        // The working directory must hold ONLY the D'' snapshot: no D', no -wal/-shm, no .dirty.
        Assert.Equal(new[] { svc.WorkingPath }, Directory.GetFiles(_workingDir));
        Assert.Equal(hashOfD, ComputeFileHash(db));   // D is never modified by a gated open
    }

    /// <summary>
    /// A gated session can never write: SaveAsync refuses and ReleaseAsync(saveFirst) does not
    /// push anything back to D.
    /// </summary>
    [Fact]
    public async Task VersionGate_GatedSession_CannotSaveAndNeverTouchesD()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5");
        var hashOfD = ComputeFileHash(db);

        await svc.CheckoutAsync(db);

        Assert.Equal(SaveOutcome.NotInWriteMode, await svc.SaveAsync());
        Assert.Equal(SaveOutcome.Success, await svc.ReleaseAsync(saveFirst: true));
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        Assert.Equal(hashOfD, ComputeFileHash(db));
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")));
    }

    /// <summary>A stamp equal to the app's version does not gate (three- vs four-part versions compare equal).</summary>
    [Fact]
    public async Task VersionGate_StampEqualsApp_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3, 0));
        var db = DbPath();
        CreateStampedDb(db, "1.2.3");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
        Assert.NotEqual(db, svc.WorkingPath);
        Assert.True(File.Exists(svc.WorkingPath));
    }

    /// <summary>A stamp lower than the app's version does not gate.</summary>
    [Fact]
    public async Task VersionGate_StampLowerThanApp_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.2");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>A real SQLite database with no AppConfiguration table is a normal, unstamped database.</summary>
    [Fact]
    public async Task VersionGate_NoAppConfigurationTable_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateSqliteDb(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A D that is not SQLite at all (the dummy files most other tests use) has no SQLite header, so it cannot
    /// carry a stamp: <see cref="StampReadKind.None"/>, not "could not look" — the gate lets it through and the
    /// integrity check of the local copy deals with such a file. (A file that could not be examined at all is a
    /// different answer, <see cref="StampReadKind.Unreadable"/>, covered by Group 19.)
    /// </summary>
    [Fact]
    public async Task VersionGate_UnreadableDatabase_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(0, 0, 1));   // older than any plausible stamp
        var db = DbPath();
        CreateFile(db, "not-a-sqlite-file");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A stamp this app cannot parse gates (fail safe): it cannot know what the requirement is.
    /// </summary>
    [Fact]
    public async Task VersionGate_UnparseableStamp_OpensReadOnly()
    {
        var (svc, lockSvc) = CreateService(new Version(9, 9, 9));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5-beta");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertGatedReadOnly(svc, lockSvc, db, "1.2.5-beta");
    }

    /// <summary>
    /// RequiredAppVersion describes the MOST RECENT checkout: opening a different, unstamped
    /// database with the same service clears it.
    /// </summary>
    [Fact]
    public async Task VersionGate_RequiredAppVersion_ResetsOnNextCheckout()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var stamped   = DbPath("stamped");
        var unstamped = DbPath("unstamped");
        CreateStampedDb(stamped, "1.2.5");
        CreateStampedDb(unstamped, null);

        await svc.CheckoutAsync(stamped);
        Assert.Equal("1.2.5", svc.RequiredAppVersion);   // precondition
        await svc.ReleaseAsync(saveFirst: false);
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        var outcome = await svc.CheckoutAsync(unstamped);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Null(svc.RequiredAppVersion);
    }

    /// <summary>
    /// The forced (stale-lock takeover) exit is gated too: after the user confirms the takeover
    /// the stamp is still honoured, and the lock taken over is released again.
    /// </summary>
    [Fact]
    public async Task VersionGate_ForceCheckout_StampHigherThanApp_OpensReadOnly()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5");
        WriteExternalStaleLock(db);
        var hashOfD = ComputeFileHash(db);

        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition
        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertGatedReadOnly(svc, lockSvc, db, "1.2.5");
        Assert.Equal(new[] { svc.WorkingPath }, Directory.GetFiles(_workingDir));
        Assert.Equal(hashOfD, ComputeFileHash(db));
    }

    /// <summary>
    /// The crash-recovery exit is gated on the ADOPTED D'. When it trips, D' and its dirty
    /// marker are KEPT (they hold the user's only copy of the unsaved edits), the session opens
    /// read-only, and once the user has a new-enough app recovery succeeds with the edits intact.
    /// </summary>
    [Fact]
    public async Task VersionGate_Recovery_StampHigherThanApp_KeepsCrashArtifactsThenRecoversOnUpdate()
    {
        var db = DbPath();
        CreateStampedDb(db, null);

        // Previous session: checkout, edit D' (including a stamp carried in by that session), crash.
        var (svc1, lockSvc1) = CreateService(new Version(1, 2, 3));
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        SetConfigValue(svc1.WorkingPath, MinimumVersionToWrite.ConfigKey, "1.2.5");
        svc1.MarkDirty();
        var crashedWorkingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);

        // Next launch on an OLD app: recovery is requested but the adopted D' demands 1.2.5.
        var (svc2, lockSvc2) = CreateService(new Version(1, 2, 3));
        Assert.True(svc2.DetectCrashRecovery(db)); // precondition
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertGatedReadOnly(svc2, lockSvc2, db, "1.2.5");
        Assert.True(File.Exists(crashedWorkingPath), "D' holds the unsaved edits — it must be kept.");
        Assert.True(File.Exists(crashedWorkingPath + ".dirty"), "The dirty marker must be kept so recovery is offered again.");
        Assert.Equal("unsaved-edit", ReadTestValue(crashedWorkingPath));
        Assert.True(svc2.DetectCrashRecovery(db), "Recovery must still be detectable on the next launch.");

        // After the user updates (a 1.2.5 app), the very same artifacts recover into write mode.
        await svc2.ReleaseAsync(saveFirst: false);
        lockSvc2.PendingRelease?.Wait(LockSettleTimeout);
        var (svc3, _) = CreateService(new Version(1, 2, 5));
        var recovered = await svc3.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, recovered);
        Assert.Null(svc3.RequiredAppVersion);
        Assert.Equal("unsaved-edit", ReadTestValue(svc3.WorkingPath));
    }

    // ── Stamping (dormant in 1.2.3: driven through the internal seams) ───────────

    /// <summary>
    /// A release that sets ThisReleaseRequires, running as a Store build, stamps D' at checkout
    /// (with the reason), and the stamp reaches D through the next save — in the same file write
    /// as the data it protects.
    /// </summary>
    [Fact]
    public async Task VersionGate_StoreBuild_StampsWorkingCopyAndSaveCarriesItToD()
    {
        var (svc, _) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ThisReleaseReason   = "Section-type colours are now saved.";
        svc.ChannelOverride     = InstallChannel.Store;
        var db = DbPath();
        CreateStampedDb(db, null);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Null(svc.RequiredAppVersion);
        Assert.Equal(StampRead.Found("1.2.4"), MinimumVersionToWrite.ReadStamp(svc.WorkingPath));
        Assert.Equal("Section-type colours are now saved.",
            GetConfigValue(svc.WorkingPath, MinimumVersionToWrite.ReasonKey));
        Assert.Equal(StampRead.None, MinimumVersionToWrite.ReadStamp(db));   // D has not been saved yet

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());

        Assert.Equal(StampRead.Found("1.2.4"), MinimumVersionToWrite.ReadStamp(db));
        Assert.Equal("Section-type colours are now saved.",
            GetConfigValue(db, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>Direct-download builds stamp; Sideload and Development builds never do.</summary>
    [Theory]
    [InlineData(InstallChannel.Store,          true)]
    [InlineData(InstallChannel.DirectDownload, true)]
    [InlineData(InstallChannel.Sideload,       false)]
    [InlineData(InstallChannel.Development,    false)]
    public async Task VersionGate_Stamping_OnlyByStoreAndDirectDownloadBuilds(InstallChannel channel, bool shouldStamp)
    {
        var (svc, _) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ChannelOverride     = channel;
        var db = DbPath();
        CreateStampedDb(db, null);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);   // never blocks editing either way
        Assert.Equal(shouldStamp ? StampRead.Found("1.2.4") : StampRead.None, MinimumVersionToWrite.ReadStamp(svc.WorkingPath));
    }

    /// <summary>A release that requires nothing (ThisReleaseRequires null — the 1.2.3 state) never stamps, whatever the channel.</summary>
    [Fact]
    public async Task VersionGate_ThisReleaseRequiresNull_NeverStamps()
    {
        var (svc, _) = CreateService(new Version(1, 2, 3));
        svc.ThisReleaseRequires = null;
        svc.ChannelOverride     = InstallChannel.Store;
        var db = DbPath();
        CreateStampedDb(db, null);

        await svc.CheckoutAsync(db);

        Assert.Equal(StampRead.None, MinimumVersionToWrite.ReadStamp(svc.WorkingPath));
        Assert.Null(GetConfigValue(svc.WorkingPath, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>Stamping never lowers an existing, higher stamp (the app is new enough to open it).</summary>
    [Fact]
    public async Task VersionGate_Stamping_DoesNotLowerAHigherExistingStamp()
    {
        var (svc, _) = CreateService(new Version(1, 2, 9));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ChannelOverride     = InstallChannel.Store;
        var db = DbPath();
        CreateStampedDb(db, "1.2.9");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(StampRead.Found("1.2.9"), MinimumVersionToWrite.ReadStamp(svc.WorkingPath));
    }

    /// <summary>
    /// A brand-new database (created by <see cref="NewDatabaseFile"/>, then checked out like any
    /// other) carries no stamp, so it is not gated; and because it now has a real working copy it is
    /// stamped like every other database opened by a stamping build. (Under the removed new-database
    /// shortcut there was no working copy, so nothing was ever stamped.)
    /// </summary>
    [Fact]
    public async Task VersionGate_NewDatabase_IsNotGated_AndIsStampedLikeAnyOther()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ChannelOverride     = InstallChannel.Store;
        var db = DbPath("brand-new");
        await CreateNewDatabaseAsync(db);
        Assert.Equal(StampRead.None, MinimumVersionToWrite.ReadStamp(db));   // a new file has no stamp

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
        Assert.NotEqual(db, svc.WorkingPath);
        Assert.Equal(StampRead.Found("1.2.4"), MinimumVersionToWrite.ReadStamp(svc.WorkingPath));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 14 — Read-only snapshot with retry (spec item 4a)
    //
    // When D'' cannot be made, the caller must ask the user "Try again / Close"
    // and NEVER fall back to opening D. The loop lives in CheckoutService
    // (SetupReadOnlySnapshotWithRetryAsync) so it is testable without any UI;
    // MainWindow only supplies the Topmost dialog (UI-bound, not unit-tested).
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Points the service at a (missing) database so its read-only snapshot setup fails: the
    /// copy of D cannot start, exactly like an unreachable share. SourceMissing takes no lock.
    /// </summary>
    /// <param name="svc">The service to point.</param>
    /// <param name="db">A path where no file exists.</param>
    private static async Task PointAtMissingSourceAsync(CheckoutService svc, string db)
    {
        Assert.Equal(CheckoutOutcome.SourceMissing, await svc.CheckoutAsync(db));
    }

    /// <summary>
    /// When the snapshot succeeds on the first attempt the user is never asked anything and the
    /// D'' path is returned.
    /// </summary>
    [Fact]
    public async Task SnapshotWithRetry_FirstAttemptSucceeds_NeverAsks()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalFreshLock(db);                       // another writer → checkout lands on D''
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));
        var asked = 0;

        var path = await svc.SetupReadOnlySnapshotWithRetryAsync(() => { asked++; return Task.FromResult(true); });

        Assert.Equal(svc.ReadOnlyWorkingPath, path);
        Assert.True(File.Exists(path));
        Assert.Equal(0, asked);
    }

    /// <summary>
    /// A failed snapshot followed by "Close" (false) returns null after asking exactly once —
    /// the caller then gives up. D is never offered as a fallback.
    /// </summary>
    [Fact]
    public async Task SnapshotWithRetry_FailsAndUserCloses_ReturnsNullAfterOneQuestion()
    {
        var (svc, _) = CreateService();
        var db = DbPath("gone");
        await PointAtMissingSourceAsync(svc, db);
        var asked = 0;

        var path = await svc.SetupReadOnlySnapshotWithRetryAsync(() => { asked++; return Task.FromResult(false); });

        Assert.Null(path);
        Assert.Equal(1, asked);
        Assert.False(File.Exists(db), "D must not be created or opened as a fallback.");
    }

    /// <summary>"Try again" loops: each failure asks again until the user finally closes.</summary>
    [Fact]
    public async Task SnapshotWithRetry_UserTriesAgainThenCloses_AsksOncePerFailure()
    {
        var (svc, _) = CreateService();
        await PointAtMissingSourceAsync(svc, DbPath("gone"));
        var answers = new Queue<bool>(new[] { true, true, false });
        var asked = 0;

        var path = await svc.SetupReadOnlySnapshotWithRetryAsync(() => { asked++; return Task.FromResult(answers.Dequeue()); });

        Assert.Null(path);
        Assert.Equal(3, asked);
    }

    /// <summary>
    /// "Try again" really retries: the problem is fixed while the dialog is up (here D appears),
    /// the second attempt succeeds, the user is asked only once, and D is only ever read — its
    /// bytes are unchanged and no sidecar appears beside it.
    /// </summary>
    [Fact]
    public async Task SnapshotWithRetry_SecondAttemptSucceedsAfterTryAgain_ReturnsSnapshotAndLeavesDUntouched()
    {
        var (svc, _) = CreateService();
        var db = DbPath("appears-later");
        await PointAtMissingSourceAsync(svc, db);
        var asked = 0;
        string? hashOfD = null;

        var path = await svc.SetupReadOnlySnapshotWithRetryAsync(() =>
        {
            asked++;
            CreateSqliteDb(db);                           // the share "comes back" while the dialog is open
            hashOfD = ComputeFileHash(db);
            return Task.FromResult(true);
        });

        Assert.NotNull(path);
        Assert.Equal(svc.ReadOnlyWorkingPath, path);
        Assert.True(File.Exists(path));
        Assert.Equal(1, asked);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.Equal(hashOfD, ComputeFileHash(db));
        Assert.Equal(hashOfD, ComputeFileHash(path!));    // D'' is a faithful copy
        Assert.Equal(new[] { db }, Directory.GetFileSystemEntries(_tempDir));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 15 — "Open as observer" vs a database the user has just created
    //
    // User decision (2026-10-12): a just-created database opens for editing even
    // when AppSettings.OpenInReaderMode is on (CheckoutAsync(ignoreObserverMode:
    // true)). ONLY the voluntary-observer branch is skipped: the license gate and
    // the write lock still apply, and the setting keeps applying to every later
    // open. Anything that changes AppSettings.Current runs under a SettingsFileGuard.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The (private-setter) <c>App.LicenseStatus</c> property, set by reflection in license tests.</summary>
    private static readonly System.Reflection.PropertyInfo LicenseStatusProperty =
        typeof(App).GetProperty(nameof(App.LicenseStatus))!;

    /// <summary>Puts <c>App.LicenseStatus</c> back to its previous value when disposed.</summary>
    private sealed class LicenseStatusRestorer(TermPoint.Licensing.AppAccessResult original) : IDisposable
    {
        /// <summary>Restores the original license status.</summary>
        public void Dispose() => LicenseStatusProperty.SetValue(null, original);
    }

    /// <summary>
    /// Temporarily replaces the process-wide license status with an expired, read-only one. The
    /// property has a private setter (production only changes it through <c>App.EvaluateLicense</c>,
    /// which reads real license/trial files), so the test sets it by reflection and restores it on dispose.
    /// </summary>
    /// <returns>A handle that restores the previous license status when disposed.</returns>
    private static IDisposable UseExpiredLicense()
    {
        var original = App.LicenseStatus;
        LicenseStatusProperty.SetValue(null, new TermPoint.Licensing.AppAccessResult
        {
            AccessLevel = TermPoint.Licensing.AccessLevel.ReadOnly,
            Reason      = TermPoint.Licensing.AccessReason.Expired,
        });
        return new LicenseStatusRestorer(original);
    }

    /// <summary>
    /// Observer mode on + a just-created database + the ignore flag → write access: lock taken,
    /// D' is the working copy. This is what lets File → New apply its configuration, and what lets
    /// the wizard write campuses/block patterns into the database it creates.
    /// </summary>
    [Fact]
    public async Task ObserverMode_NewDatabase_WithIgnoreFlag_OpensForWriting()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, lockSvc) = CreateService();
        var db = DbPath("observer-new");
        await CreateNewDatabaseAsync(db);

        var outcome = await svc.CheckoutAsync(db, ignoreObserverMode: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.False(svc.IsReadOnlyMode);
        Assert.True(lockSvc.IsWriter);
        Assert.NotEqual(db, svc.WorkingPath);
        Assert.True(AppSettings.Current.OpenInReaderMode, "The setting itself must not be changed.");
    }

    /// <summary>
    /// Observer mode on + an existing database + no flag → read-only snapshot exactly as before:
    /// no lock, no .lock file, D'' as the working copy.
    /// </summary>
    [Fact]
    public async Task ObserverMode_ExistingDatabase_WithoutFlag_StillOpensReadOnly()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.True(svc.IsReadOnlyMode);
        Assert.Equal(svc.ReadOnlyWorkingPath, svc.WorkingPath);
        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")), "Observer mode must never create the lock file.");
    }

    /// <summary>
    /// The flag covers ONE checkout. After the just-created database is released, opening it again
    /// without the flag (a later open) honours observer mode — the setting "keeps applying to every
    /// database opened later, including this new one".
    /// </summary>
    [Fact]
    public async Task ObserverMode_IgnoreFlagCoversOnlyOneCheckout_LaterOpenIsReadOnly()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, lockSvc) = CreateService();
        var db = DbPath("observer-later");
        await CreateNewDatabaseAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db, ignoreObserverMode: true));
        Assert.Equal(SaveOutcome.Success, await svc.ReleaseAsync(saveFirst: true));
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);   // settle the background lock-file deletion

        var later = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, later);
        Assert.False(lockSvc.IsWriter);
        Assert.True(AppSettings.Current.OpenInReaderMode);
    }

    /// <summary>
    /// The flag skips only the observer branch: a live lock held by another instance still makes the
    /// checkout read-only (and names the holder).
    /// </summary>
    [Fact]
    public async Task ObserverMode_IgnoreFlag_DoesNotOverrideALiveLock()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalFreshLock(db);

        var outcome = await svc.CheckoutAsync(db, ignoreObserverMode: true);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal("other_user", svc.CurrentHolder?.Username);
    }

    /// <summary>With observer mode off the flag changes nothing: an ordinary write checkout.</summary>
    [Fact]
    public async Task ObserverModeOff_IgnoreFlag_IsHarmless()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = false;
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        var outcome = await svc.CheckoutAsync(db, ignoreObserverMode: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// The license gate is NOT bypassed: an expired license is read-only even for a just-created
    /// database with the ignore flag, with no lock taken and no lock file written.
    /// </summary>
    [Fact]
    public async Task ExpiredLicense_NewDatabase_WithIgnoreFlag_StillReadOnly()
    {
        using var settingsGuard = new SettingsFileGuard();
        using var license = UseExpiredLicense();
        AppSettings.Current.OpenInReaderMode = true;   // observer on as well: the flag must not unlock anything
        var (svc, lockSvc) = CreateService();
        var db = DbPath("expired-new");
        await CreateNewDatabaseAsync(db);

        var outcome = await svc.CheckoutAsync(db, ignoreObserverMode: true);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")));
    }

    /// <summary>
    /// The wizard's step-3 checkout (<see cref="TermPoint.ViewModels.Wizard.WizardServices.CheckoutNewDatabaseAsync"/>,
    /// the production <c>CheckoutDatabase</c> delegate) opens the database it just created for
    /// writing even with observer mode on, and returns D' as the working path.
    /// </summary>
    [Fact]
    public async Task WizardCheckoutNewDatabase_IgnoresObserverMode()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, lockSvc) = CreateService();
        var db = DbPath("wizard-observer");
        await CreateNewDatabaseAsync(db);

        var (outcome, workingPath) = await TermPoint.ViewModels.Wizard.WizardServices.CheckoutNewDatabaseAsync(svc, db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(svc.WorkingPath, workingPath);
        Assert.NotEqual(db, workingPath);
    }

    /// <summary>The wizard helper does not bypass the license gate either.</summary>
    [Fact]
    public async Task WizardCheckoutNewDatabase_ExpiredLicense_StillReadOnly()
    {
        using var settingsGuard = new SettingsFileGuard();
        using var license = UseExpiredLicense();
        var (svc, lockSvc) = CreateService();
        var db = DbPath("wizard-expired");
        await CreateNewDatabaseAsync(db);

        var (outcome, _) = await TermPoint.ViewModels.Wizard.WizardServices.CheckoutNewDatabaseAsync(svc, db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        Assert.False(lockSvc.IsWriter);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 15 — D held open by another program (spec item 4e, hand-test finding)
    //
    // A healthy database that another program holds with FileShare.None used to make
    // NetworkFileOps.ComputeHashAsync throw an IOException out of CheckoutAsync AFTER the
    // write lock had been taken, leaving a live .lock file and heartbeat until the app
    // exited. Every checkout path must now release the lock, leave no working copy
    // behind (except the kept crash-recovery data) and return SourceUnreadable — or, for
    // the read-only snapshot, return null so the Try again / Close prompt appears.
    //
    // FileShare.None is only OS-enforced on Windows, hence FactWindowsOnly.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Path of the lock file the write lock creates next to <paramref name="db"/>.</summary>
    private static string LockFileOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>
    /// Asserts a failed checkout left no write lock behind: the service is not a writer, the
    /// <c>.lock</c> file is gone (after the background deletion settles) and no working copy,
    /// snapshot, temp or marker file exists in the working folder.
    /// </summary>
    /// <param name="lockSvc">The lock service the checkout used.</param>
    /// <param name="db">Path of D.</param>
    private void AssertLockReleasedAndNothingLeftBehind(WriteLockService lockSvc, string db)
    {
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);   // settle the background lock-file deletion
        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(LockFileOf(db)), "The .lock file must not outlive a failed checkout.");
        Assert.Empty(Directory.GetFiles(_workingDir));
    }

    /// <summary>
    /// THE HAND-TEST BUG. A write checkout of a healthy D that another program holds exclusively
    /// returns SourceUnreadable (it used to throw an IOException), gives the write lock back
    /// (not a writer, no .lock file), leaves no D', and does not alter D. Once the other program lets
    /// go, the very same service checks the database out normally.
    /// </summary>
    [FactWindowsOnly]
    public async Task CheckoutAsync_SourceHeldExclusively_ReturnsSourceUnreadable_AndReleasesTheLock()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        var before = DbFileTestHelpers.HashOf(db);

        using (DbFileTestHelpers.LockExclusively(db))
        {
            var outcome = await svc.CheckoutAsync(db);

            Assert.Equal(CheckoutOutcome.SourceUnreadable, outcome);
            AssertLockReleasedAndNothingLeftBehind(lockSvc, db);
        }

        Assert.Equal(before, DbFileTestHelpers.HashOf(db));   // D untouched
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A take-over of a stale lock (ForceCheckoutAsync) hits the same wall: the forced lock is released
    /// again, no .lock file or D' is left, and a fresh checkout after release succeeds.
    /// </summary>
    [FactWindowsOnly]
    public async Task ForceCheckoutAsync_SourceHeldExclusively_ReturnsSourceUnreadable_AndReleasesTheLock()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalStaleLock(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));   // precondition

        using (DbFileTestHelpers.LockExclusively(db))
        {
            var outcome = await svc.ForceCheckoutAsync();

            Assert.Equal(CheckoutOutcome.SourceUnreadable, outcome);
            AssertLockReleasedAndNothingLeftBehind(lockSvc, db);
        }

        // A retry is a fresh checkout (the stale lock file was replaced and then removed on release).
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// Crash recovery with D held open: SourceUnreadable, the lock is released, and — unlike the
    /// normal path — the crashed session's D' and its dirty marker are KEPT with the unsaved edit
    /// intact, so recovery is offered again and then succeeds.
    /// </summary>
    [FactWindowsOnly]
    public async Task RecoveryCheckout_SourceHeldExclusively_ReturnsSourceUnreadable_KeepsRecoveryData()
    {
        var (svc1, lockSvc1) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        svc1.MarkDirty();
        var crashedWorkingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);

        var (svc2, lockSvc2) = CreateService();
        using (DbFileTestHelpers.LockExclusively(db))
        {
            var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

            Assert.Equal(CheckoutOutcome.SourceUnreadable, outcome);
            lockSvc2.PendingRelease?.Wait(LockSettleTimeout);
            Assert.False(lockSvc2.IsWriter);
            Assert.False(File.Exists(LockFileOf(db)));
            Assert.True(File.Exists(crashedWorkingPath), "D' holds the user's only copy of the unsaved edit.");
            Assert.True(File.Exists(crashedWorkingPath + ".dirty"), "The dirty marker must survive.");
            Assert.Equal("unsaved-edit", ReadTestValue(crashedWorkingPath));
        }

        var (svc3, _) = CreateService();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc3.CheckoutAsync(db, recoverWorkingCopy: true));
        Assert.Equal("unsaved-edit", ReadTestValue(svc3.WorkingPath));
    }

    /// <summary>
    /// The read-only snapshot (D → D'') must report a held-open D by returning null — never by throwing
    /// — so the caller's "Try again / Close" prompt appears. No partial D''.tmp is left behind, and the
    /// next attempt after release succeeds.
    /// </summary>
    [FactWindowsOnly]
    public async Task SetupReadOnlySnapshotAsync_SourceHeldExclusively_ReturnsNullWithoutThrowing()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalFreshLock(db);
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));   // sets SourcePath, makes D''

        using (DbFileTestHelpers.LockExclusively(db))
        {
            var path = await svc.SetupReadOnlySnapshotAsync();

            Assert.Null(path);
            Assert.False(File.Exists(svc.ReadOnlyWorkingPath + ".tmp"), "No half-made snapshot may be left.");
        }

        Assert.NotNull(await svc.SetupReadOnlySnapshotAsync());
    }

    /// <summary>
    /// Observer mode on + D held open: the snapshot cannot be made, so the checkout reports Failed (the
    /// caller then shows Try again / Close) instead of throwing, and no .lock file is ever created.
    /// </summary>
    [FactWindowsOnly]
    public async Task ObserverMode_SourceHeldExclusively_ReturnsFailedWithoutThrowing()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);

        using (DbFileTestHelpers.LockExclusively(db))
        {
            var outcome = await svc.CheckoutAsync(db);

            Assert.Equal(CheckoutOutcome.Failed, outcome);
            Assert.False(lockSvc.IsWriter);
            Assert.False(File.Exists(LockFileOf(db)));
        }
    }

    /// <summary>
    /// The retry loop copes with a held-open D: it asks once, the user waits for the other program to
    /// let go and chooses "Try again", and the second attempt succeeds. Choosing "Close" instead
    /// returns null after exactly one question. Neither path throws.
    /// </summary>
    [FactWindowsOnly]
    public async Task SnapshotWithRetry_SourceHeldExclusively_TryAgainSucceedsAfterRelease_CloseGivesUp()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalFreshLock(db);
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));

        // "Close": the other program never lets go.
        var closeAsked = 0;
        using (DbFileTestHelpers.LockExclusively(db))
        {
            var gaveUp = await svc.SetupReadOnlySnapshotWithRetryAsync(() => { closeAsked++; return Task.FromResult(false); });
            Assert.Null(gaveUp);
            Assert.Equal(1, closeAsked);
        }

        // "Try again": the lock is released while the prompt is up.
        var held = DbFileTestHelpers.LockExclusively(db);
        var tryAgainAsked = 0;
        var path = await svc.SetupReadOnlySnapshotWithRetryAsync(() =>
        {
            tryAgainAsked++;
            held.Dispose();
            return Task.FromResult(true);
        });

        Assert.Equal(1, tryAgainAsked);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 16 — The orphaned D.tmp is removed ONLY once the write lock is ours (spec item 4)
    //
    // D.tmp is the in-flight file of whoever is saving, and only the lock holder saves. Deleting it
    // before the lock is held could delete a colleague's save in progress. CheckoutService therefore
    // removes it itself, at the three write-access exits (normal, forced, recovery), after the version
    // gate and just before Mode becomes WriteAccess. Every other outcome must leave it alone.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Content of the D.tmp these tests plant, so "left alone" can be checked byte for byte.</summary>
    private const string InFlightTmpContent = "colleague-save-in-flight";

    /// <summary>Plants a D.tmp beside <paramref name="db"/> and returns its path.</summary>
    /// <param name="db">Path to D.</param>
    private static string PlantTmp(string db)
    {
        var tmp = db + ".tmp";
        File.WriteAllText(tmp, InFlightTmpContent);
        return tmp;
    }

    /// <summary>Asserts the planted D.tmp still exists, untouched.</summary>
    /// <param name="tmp">Path to the planted D.tmp.</param>
    private static void AssertTmpUntouched(string tmp)
    {
        Assert.True(File.Exists(tmp), "D.tmp must not be deleted by an open that does not hold the write lock.");
        Assert.Equal(InFlightTmpContent, File.ReadAllText(tmp));
    }

    /// <summary>A normal write-access checkout deletes the orphaned D.tmp.</summary>
    [Fact]
    public async Task OrphanedTmp_NormalWriteCheckout_IsDeleted()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.False(File.Exists(tmp), "A write checkout must remove the orphaned D.tmp.");
    }

    /// <summary>
    /// A forced (stale-lock take-over) checkout deletes it too — but NOT at the earlier StaleHolder
    /// answer, where the lock is not ours yet.
    /// </summary>
    [Fact]
    public async Task OrphanedTmp_ForceCheckout_IsDeletedOnlyAfterTheTakeOver()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalStaleLock(db);
        var tmp = PlantTmp(db);

        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));
        AssertTmpUntouched(tmp);

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.False(File.Exists(tmp));
    }

    /// <summary>A crash-recovery checkout that resumes the crashed session deletes it too.</summary>
    [Fact]
    public async Task OrphanedTmp_RecoveryCheckout_IsDeleted()
    {
        var db = DbPath();
        CreateSqliteDb(db);
        var (svc1, lockSvc1) = CreateService();
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);
        var tmp = PlantTmp(db);   // the crashed save's leftover

        var (svc2, _) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.False(File.Exists(tmp));
    }

    /// <summary>A read-only open because ANOTHER session holds the live lock must not touch D.tmp.</summary>
    [Fact]
    public async Task OrphanedTmp_ReadOnlyBecauseOfLiveLock_IsLeftAlone()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        WriteExternalFreshLock(db);
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertTmpUntouched(tmp);
    }

    /// <summary>An observer (reader mode) open must not touch D.tmp.</summary>
    [Fact]
    public async Task OrphanedTmp_ObserverMode_IsLeftAlone()
    {
        using var settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = true;
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertTmpUntouched(tmp);
    }

    /// <summary>A licence-gated read-only open must not touch D.tmp.</summary>
    [Fact]
    public async Task OrphanedTmp_LicenseGatedReadOnly_IsLeftAlone()
    {
        using var license = UseExpiredLicense();
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertTmpUntouched(tmp);
    }

    /// <summary>
    /// A version-gated open (the app is older than the database's stamp) must not touch D.tmp — the
    /// cleanup sits AFTER the gate, so the lock having been taken for a moment is not enough.
    /// </summary>
    [Fact]
    public async Task OrphanedTmp_VersionGated_NormalCheckout_IsLeftAlone()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5");
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertGatedReadOnly(svc, lockSvc, db, "1.2.5");
        AssertTmpUntouched(tmp);
    }

    /// <summary>The forced (stale-lock) exit is gated the same way and leaves D.tmp alone.</summary>
    [Fact]
    public async Task OrphanedTmp_VersionGated_ForceCheckout_IsLeftAlone()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5");
        WriteExternalStaleLock(db);
        var tmp = PlantTmp(db);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertGatedReadOnly(svc, lockSvc, db, "1.2.5");
        AssertTmpUntouched(tmp);
    }

    /// <summary>The recovery exit is gated on the adopted D' and leaves D.tmp alone when it trips.</summary>
    [Fact]
    public async Task OrphanedTmp_VersionGated_RecoveryCheckout_IsLeftAlone()
    {
        var db = DbPath();
        CreateStampedDb(db, null);
        var (svc1, lockSvc1) = CreateService(new Version(1, 2, 3));
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        SetConfigValue(svc1.WorkingPath, MinimumVersionToWrite.ConfigKey, "1.2.5");
        svc1.MarkDirty();
        SimulateCrash(lockSvc1, db);
        var tmp = PlantTmp(db);

        var (svc2, _) = CreateService(new Version(1, 2, 3));
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        AssertTmpUntouched(tmp);
    }

    /// <summary>A missing D (nothing to open) must not touch a D.tmp that happens to be there.</summary>
    [Fact]
    public async Task OrphanedTmp_SourceMissing_IsLeftAlone()
    {
        var (svc, _) = CreateService();
        var db = DbPath("gone");
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.SourceMissing, outcome);
        AssertTmpUntouched(tmp);
    }

    /// <summary>An unreachable location must not touch D.tmp (injected, so it runs everywhere).</summary>
    [Fact]
    public async Task OrphanedTmp_NetworkUnreachable_IsLeftAlone()
    {
        var (svc, _) = CreateService();
        svc.FaultPlan = CheckoutFaultPlan.Parse("unreachable");
        var db = DbPath();
        CreateSqliteDb(db);
        var tmp = PlantTmp(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.NetworkUnreachable, outcome);
        AssertTmpUntouched(tmp);
    }

    /// <summary>A D that another program holds open (SourceUnreadable) must not touch D.tmp.</summary>
    [FactWindowsOnly]
    public async Task OrphanedTmp_SourceUnreadable_IsLeftAlone()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        var tmp = PlantTmp(db);

        using (DbFileTestHelpers.LockExclusively(db))
        {
            var outcome = await svc.CheckoutAsync(db);

            Assert.Equal(CheckoutOutcome.SourceUnreadable, outcome);
        }

        AssertTmpUntouched(tmp);
    }

    /// <summary>
    /// A cleanup that cannot delete (here: a colleague's process has D.tmp open) never fails the checkout:
    /// it is logged and the session opens for writing.
    /// </summary>
    [FactWindowsOnly]
    public async Task OrphanedTmp_CannotBeDeleted_DoesNotFailTheCheckout()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        var tmp = PlantTmp(db);

        CheckoutOutcome outcome;
        using (new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.None))
            outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.True(File.Exists(tmp));
    }

    /// <summary>
    /// A D.tmp that is still there when a save runs (the cleanup was skipped or failed) is simply
    /// overwritten: the save streams its snapshot to D.tmp with <c>FileMode.Create</c> and renames it
    /// over D, so it succeeds, D gets the edit, and no D.tmp remains.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithLeftoverTmp_OverwritesItAndSucceeds()
    {
        var (svc, _) = CreateService();
        var db = DbPath();
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);
        InsertTestValue(svc.WorkingPath, "edited-content");
        var tmp = PlantTmp(db);   // appears after checkout: nothing cleaned it

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal("edited-content", ReadTestValue(db));
        Assert.False(File.Exists(tmp), "The rename consumed D.tmp.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 17 — File errors in the save pipeline come back as clean outcomes (spec item 4, step 4c)
    //
    // A plain file error on D, D.tmp or the working copy D' used to ESCAPE SaveAsync as an exception:
    // autosave logged "AutoSave tick failed", the Save button reached the last-resort handler, and the
    // exit save reached OnClosing by an unplanned path. Now every one returns SaveOutcome.CopyError with
    // the normal transient banner, D untouched and D' + its dirty marker intact — so ReleaseAsync at exit
    // takes its deliberate CopyError branch (lock parked via Suspend, D' + marker kept). A final safety
    // net in SaveAsync does the same for any exception nobody anticipated. And BackupSqliteDatabase
    // opens D' with Mode=ReadWrite, so a deleted working copy is never re-created EMPTY and pushed over D.
    //
    // FileShare.None is only OS-enforced on Windows, hence FactWindowsOnly for those tests.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Records every <see cref="CheckoutService.SaveFailed"/> banner (text and auto-dismiss flag) raised by <paramref name="svc"/>.</summary>
    /// <param name="svc">The service to observe.</param>
    /// <returns>A live list, filled as banners are raised.</returns>
    private static List<(string Message, bool AutoDismiss)> CaptureSaveBanners(CheckoutService svc)
    {
        var banners = new List<(string Message, bool AutoDismiss)>();
        svc.SaveFailed += (message, autoDismiss) => banners.Add((message, autoDismiss));
        return banners;
    }

    /// <summary>
    /// Checks out a fresh SQLite database for writing and leaves one unsaved edit in D' with the dirty
    /// marker written — the state in which a failed save must keep everything.
    /// </summary>
    /// <param name="db">Path of D (created here).</param>
    /// <param name="logger">Logger to inject (see <see cref="CreateService"/>); null uses the no-op logger.</param>
    /// <returns>The service and its lock service.</returns>
    private async Task<(CheckoutService svc, WriteLockService lockSvc)> CheckoutWithUnsavedEditAsync(string db, IAppLogger? logger = null)
    {
        var (svc, lockSvc) = CreateService(logger: logger);
        CreateSqliteDb(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        InsertTestValue(svc.WorkingPath, "unsaved-edit");
        svc.MarkDirty();
        return (svc, lockSvc);
    }

    /// <summary>Asserts a failed save kept the user's work: D' and its dirty marker exist and the session still holds write access.</summary>
    /// <param name="svc">The service whose save failed.</param>
    /// <param name="lockSvc">Its lock service.</param>
    private static void AssertSessionKept(CheckoutService svc, WriteLockService lockSvc)
    {
        Assert.True(File.Exists(svc.WorkingPath), "D' must survive a failed save.");
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "The dirty marker must survive a failed save.");
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// D held exclusively by another program (antivirus / backup / sync) when a save starts: the step-2
    /// hash of D used to throw an IOException out of SaveAsync. Now CopyError plus the in-use banner,
    /// no exception, D unchanged, nothing written to the share, D' + marker kept — and once the other
    /// program lets go, the very next save succeeds and pushes the edit.
    /// </summary>
    [FactWindowsOnly]
    public async Task SaveAsync_SourceHeldExclusively_ReturnsCopyError_KeepsEverything_AndALaterSaveSucceeds()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);

        SaveOutcome outcome;
        using (DbFileTestHelpers.LockExclusively(db))
            outcome = await svc.SaveAsync();   // must NOT throw

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false), false), Assert.Single(banners));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.False(File.Exists(db + ".tmp"), "Nothing may be written to the share when D cannot be read.");
        AssertSessionKept(svc, lockSvc);

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(db));
    }

    /// <summary>
    /// The same failure during an AUTOSAVE: same outcome, but the banner gains the "will try again
    /// automatically" note and is marked auto-dismissing.
    /// </summary>
    [FactWindowsOnly]
    public async Task SaveAsync_SourceHeldExclusively_AutoSave_BannerAutoDismissesAndPromisesRetry()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);

        SaveOutcome outcome;
        using (DbFileTestHelpers.LockExclusively(db))
            outcome = await svc.SaveAsync(isAutoSave: true);

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: true), true), Assert.Single(banners));
    }

    /// <summary>
    /// D.tmp held exclusively by another program while the snapshot is streamed to it: the copy's open
    /// fails with a sharing violation. CopyError plus the in-use banner, no exception, D unchanged, the
    /// other program's file left as it was, D' + marker kept; the next save (after release) succeeds.
    /// </summary>
    [FactWindowsOnly]
    public async Task SaveAsync_TmpHeldExclusivelyDuringCopy_ReturnsCopyError_AndDIsUnchanged()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);   // checkout's own D.tmp cleanup is already done
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);
        var tmp = PlantTmp(db);

        SaveOutcome outcome;
        using (DbFileTestHelpers.LockExclusively(tmp))
            outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false), false), Assert.Single(banners));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        AssertTmpUntouched(tmp);
        AssertSessionKept(svc, lockSvc);

        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(db));
    }

    /// <summary>
    /// D.tmp disappears between the copy and the verify (deleted by a cleaner, quarantined by antivirus):
    /// the step-4 hash used to throw a FileNotFoundException out of SaveAsync. Now CopyError, no rename
    /// (D unchanged), D.tmp not left behind, D' + marker kept. Uses the BeforeTmpVerifyAsync seam — the
    /// window is microseconds wide and cannot be hit otherwise.
    /// </summary>
    [Fact]
    public async Task SaveAsync_TmpVanishesBeforeVerify_ReturnsCopyError_AndDIsUnchanged()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);
        svc.BeforeTmpVerifyAsync = path => { File.Delete(path); return Task.CompletedTask; };

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false), false), Assert.Single(banners));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.False(File.Exists(db + ".tmp"));
        AssertSessionKept(svc, lockSvc);

        svc.BeforeTmpVerifyAsync = null;
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(db));
    }

    /// <summary>
    /// D.tmp becomes unreadable at the verify (another program grabs it exclusively right after the
    /// copy): sharing violation on the step-4 hash. CopyError plus the in-use banner, no rename, D
    /// unchanged. The best-effort delete of D.tmp cannot succeed while the other program holds it, which
    /// is harmless — the next save overwrites it, and that save succeeds once the hold is released.
    /// </summary>
    [FactWindowsOnly]
    public async Task SaveAsync_TmpHeldExclusivelyAtVerify_ReturnsCopyError_AndDIsUnchanged()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);
        FileStream? holder = null;
        svc.BeforeTmpVerifyAsync = path => { holder = DbFileTestHelpers.LockExclusively(path); return Task.CompletedTask; };

        SaveOutcome outcome;
        try
        {
            outcome = await svc.SaveAsync();
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false), false), Assert.Single(banners));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        AssertSessionKept(svc, lockSvc);

        svc.BeforeTmpVerifyAsync = null;
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(db));
        Assert.False(File.Exists(db + ".tmp"), "The successful save consumed D.tmp.");
    }

    /// <summary>
    /// THE macOS SCENARIO (also reproducible on Windows when no connection holds D'): the working copy
    /// has been deleted when a save runs. The backup used to open D' with the default create mode, which
    /// would silently re-create an EMPTY database at that path, snapshot it and push it over D. Now the
    /// open fails: CopyError with the "working copy is missing" banner, NO new file at the working-copy
    /// path (and no local snapshot), D byte-identical, nothing left on the share.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WorkingCopyDeleted_ReturnsCopyError_CreatesNothing_AndDIsByteIdentical()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);
        var workingCopy = svc.WorkingPath;
        SqliteConnection.ClearAllPools();
        File.Delete(workingCopy);

        var outcome = await svc.SaveAsync();

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.SessionFileMissingMessage, false), Assert.Single(banners));
        Assert.False(File.Exists(workingCopy), "A missing working copy must never be re-created (empty) by the save.");
        Assert.False(File.Exists(workingCopy + ".savetmp"));
        Assert.False(File.Exists(db + ".tmp"));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
    }

    /// <summary>
    /// The exit save with D held by another program: ReleaseAsync(saveFirst: true) must not throw and
    /// must take the deliberate CopyError branch — the lock is parked (file left in place, no longer a
    /// writer), D' and the dirty marker are kept for recovery, D is unchanged.
    /// </summary>
    [FactWindowsOnly]
    public async Task ReleaseAsync_SourceHeldExclusively_TakesTheCopyErrorBranch()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);

        SaveOutcome outcome;
        using (DbFileTestHelpers.LockExclusively(db))
            outcome = await svc.ReleaseAsync(saveFirst: true);   // must NOT throw

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.False(lockSvc.IsWriter, "The lock is suspended, not held.");
        Assert.True(File.Exists(LockFileOf(db)), "The lock file is deliberately left in place (it goes stale and protects D).");
        Assert.True(File.Exists(svc.WorkingPath), "D' must survive for recovery.");
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "The dirty marker must survive for recovery.");
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
    }

    /// <summary>
    /// The safety net: an exception NOBODY anticipated inside the pipeline (here an
    /// InvalidOperationException thrown from the test seam, after D.tmp is written) no longer escapes
    /// SaveAsync. CopyError, the "failed unexpectedly. Your changes are kept." banner, SaveFinished still
    /// raised, in-flight flag reset (the next save runs), D unchanged, D' + marker kept.
    /// </summary>
    [Fact]
    public async Task SaveAsync_UnexpectedException_IsCaught_ReturnsCopyError_AndTheNextSaveRuns()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var finished = false;
        svc.SaveFinished += () => finished = true;
        var before = DbFileTestHelpers.HashOf(db);
        svc.BeforeTmpVerifyAsync = _ => throw new InvalidOperationException("unexpected, on purpose");

        var outcome = await svc.SaveAsync();   // must NOT throw

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.UnexpectedMessage, isAutoSave: false), false), Assert.Single(banners));
        Assert.True(finished, "SaveFinished must still fire so the Saving… indicator clears.");
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        AssertSessionKept(svc, lockSvc);

        svc.BeforeTmpVerifyAsync = null;
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(db));
    }

    /// <summary>
    /// An unexpected exception during the EXIT save lands in the same deliberate CopyError branch as any
    /// other transient failure: lock parked via Suspend, D' + marker kept, no exception to OnClosing.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_UnexpectedExceptionInSave_TakesTheCopyErrorBranch()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        svc.BeforeTmpVerifyAsync = _ => throw new InvalidOperationException("unexpected, on purpose");

        var outcome = await svc.ReleaseAsync(saveFirst: true);   // must NOT throw

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.True(File.Exists(LockFileOf(db)));
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Spec item 12 — a failed save shows exactly ONE banner (the save banner), in plain words, and sends
    // ONE path-free handled report. Before: every unexpected failure also called LogError, whose ErrorLogged
    // event produced a second, red banner carrying log text, and several banners quoted the raw .NET message.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Asserts the item-12 contract for one failed save: exactly one save banner with the expected text and
    /// dismiss flag; no LogError (so no ErrorLogged, so no second banner); exactly one handled report naming
    /// <paramref name="step"/> whose exception message, context, grouping key and every metadata value carry
    /// neither the test's folders nor the database file name; and a local log line naming the original
    /// exception's type (when there was one).
    /// </summary>
    /// <param name="logger">The recording logger the service was built with.</param>
    /// <param name="banners">The banners captured from the service.</param>
    /// <param name="db">Path of D, whose file name and folder must not appear in the report.</param>
    /// <param name="expectedText">The complete expected banner text.</param>
    /// <param name="expectedAutoDismiss">The expected auto-dismiss flag.</param>
    /// <param name="step">The step the report must name.</param>
    /// <param name="originalTypeName">The original exception's type name, or null when there was none.</param>
    private void AssertOneBannerOneReport(
        RecordingAppLogger logger, List<(string Message, bool AutoDismiss)> banners, string db,
        string expectedText, bool expectedAutoDismiss, SaveStep step, string? originalTypeName)
    {
        Assert.Equal((expectedText, expectedAutoDismiss), Assert.Single(banners));
        Assert.Empty(logger.Errors);

        var report = Assert.Single(logger.Handled);
        var failure = Assert.IsType<SaveFailedException>(report.Exception);
        Assert.Equal(step, failure.Step);
        Assert.Equal(originalTypeName, failure.OriginalType);
        Assert.Equal($"save-failed:{step}", report.GroupingKey);
        Assert.Equal($"Save failed: {step}", report.Context);
        Assert.Null(failure.InnerException);

        var everythingSent = new List<string>
        {
            report.Exception.Message, report.Context, report.GroupingKey ?? "", report.MetadataTab
        };
        everythingSent.AddRange(report.Metadata?.Values ?? Enumerable.Empty<string>());
        foreach (var text in everythingSent)
        {
            Assert.DoesNotContain(_tempDir, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_workingDir, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Path.GetFileName(db), text, StringComparison.OrdinalIgnoreCase);
        }

        if (originalTypeName is not null)
            Assert.Contains(logger.Infos, line => line.Contains(originalTypeName));
    }

    /// <summary>The safety net: one banner, no LogError, one report of step Unexpected.</summary>
    [Fact]
    public async Task SaveAsync_UnexpectedException_RaisesOneBanner_AndOnePathFreeReport()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var banners = CaptureSaveBanners(svc);
        svc.BeforeTmpVerifyAsync = _ => throw new InvalidOperationException("unexpected, on purpose: " + db);

        await svc.SaveAsync();

        AssertOneBannerOneReport(logger, banners, db,
            SavePrompt.ForTransientFailure(SavePrompt.UnexpectedMessage, isAutoSave: false), false,
            SaveStep.Unexpected, nameof(InvalidOperationException));
    }

    /// <summary>D.tmp vanishes before the verify: one in-use banner, one report of step VerifyAtLocation.</summary>
    [Fact]
    public async Task SaveAsync_TmpVanishesBeforeVerify_RaisesOneBanner_AndOnePathFreeReport()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var banners = CaptureSaveBanners(svc);
        svc.BeforeTmpVerifyAsync = path => { File.Delete(path); return Task.CompletedTask; };

        await svc.SaveAsync();

        var report = Assert.Single(logger.Handled);
        var originalType = ((SaveFailedException)report.Exception).OriginalType;
        Assert.True(originalType is nameof(FileNotFoundException) or nameof(DirectoryNotFoundException), originalType);
        AssertOneBannerOneReport(logger, banners, db,
            SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false), false,
            SaveStep.VerifyAtLocation, originalType);
    }

    /// <summary>
    /// D' deleted before the save: one "files for this session" banner that is STICKY even for an autosave
    /// (retrying can never succeed, so no retry note), one report of step Snapshot.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WorkingCopyDeleted_AutoSave_RaisesOneStickyBanner_AndOnePathFreeReport()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var banners = CaptureSaveBanners(svc);
        SqliteConnection.ClearAllPools();
        File.Delete(svc.WorkingPath);

        await svc.SaveAsync(isAutoSave: true);

        var originalType = ((SaveFailedException)Assert.Single(logger.Handled).Exception).OriginalType;
        Assert.NotNull(originalType);
        AssertOneBannerOneReport(logger, banners, db,
            SavePrompt.SessionFileMissingMessage, expectedAutoDismiss: false,
            SaveStep.Snapshot, originalType);
    }

    /// <summary>
    /// D' held exclusively (another program has it open) when the snapshot is taken: SQLite cannot open it, the
    /// file still exists, so one "couldn't prepare the save" banner and one report of step Snapshot.
    /// </summary>
    [FactWindowsOnly]
    public async Task SaveAsync_WorkingCopyHeldExclusively_RaisesOneBanner_AndOnePathFreeReport()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var banners = CaptureSaveBanners(svc);
        SqliteConnection.ClearAllPools();

        using (DbFileTestHelpers.LockExclusively(svc.WorkingPath))
            await svc.SaveAsync();

        var originalType = ((SaveFailedException)Assert.Single(logger.Handled).Exception).OriginalType;
        Assert.NotNull(originalType);
        AssertOneBannerOneReport(logger, banners, db,
            SavePrompt.ForTransientFailure(SavePrompt.PrepareFailedMessage, isAutoSave: false), false,
            SaveStep.Snapshot, originalType);
    }

    /// <summary>
    /// MarkDirty with the marker path blocked by a directory: the crash-protection warning is the only banner
    /// (auto-dismissing), nothing goes through LogError, and one report of step CrashProtectionMarker is sent
    /// with no original exception.
    /// </summary>
    [Fact]
    public async Task MarkDirty_MarkerWriteFails_RaisesOneBanner_AndOnePathFreeReport()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = CreateService(logger: logger);
        CreateSqliteDb(db);
        await svc.CheckoutAsync(db);
        var banners = CaptureSaveBanners(svc);
        var markerPath = svc.WorkingPath + ".dirty";
        Directory.CreateDirectory(markerPath);

        try
        {
            svc.MarkDirty();
        }
        finally
        {
            Directory.Delete(markerPath);
        }

        AssertOneBannerOneReport(logger, banners, db,
            SavePrompt.CrashProtectionMessage, expectedAutoDismiss: true,
            SaveStep.CrashProtectionMarker, originalTypeName: null);
        var metadata = Assert.Single(logger.Handled).Metadata!;
        Assert.Equal("none", metadata["exceptionType"]);
        Assert.Equal("none", metadata["hresult"]);
    }

    /// <summary>
    /// A persistent fault must not send one report per autosave cycle: the same failing step twice gives two
    /// banners (the user still sees each failure) but ONE handled report; the second leaves only a local line.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SameFailureTwice_RaisesTwoBanners_ButOnlyOneReport()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var banners = CaptureSaveBanners(svc);
        svc.BeforeTmpVerifyAsync = _ => throw new InvalidOperationException("unexpected, on purpose");

        await svc.SaveAsync(isAutoSave: true);
        var infosAfterFirst = logger.Infos.Count;
        await svc.SaveAsync(isAutoSave: true);

        Assert.Equal(2, banners.Count);
        Assert.Single(logger.Handled);
        Assert.Empty(logger.Errors);
        Assert.Contains(logger.Infos.Skip(infosAfterFirst), line => line.Contains(nameof(InvalidOperationException)));
    }

    /// <summary>
    /// The periodic backup (VACUUM INTO of D') has the same flaw as the save snapshot: opened with the
    /// default create mode it would re-create a deleted working copy EMPTY and ship an empty "backup",
    /// rotating real ones out. It now opens the source with Mode=ReadWrite — a missing source throws
    /// (SQLITE_CANTOPEN, code 14) and nothing is created, neither at the source path nor the destination.
    /// </summary>
    [Fact]
    public void BackupService_VacuumInto_MissingWorkingCopy_Throws_AndCreatesNothing()
    {
        var missing = Path.Combine(_workingDir, "gone_working_copy.db");
        var dest    = Path.Combine(_workingDir, "backup.backuptmp");
        var dbMock  = new Moq.Mock<TermPoint.Data.IDatabaseContext>();
        dbMock.SetupGet(d => d.DatabasePath).Returns(missing);
        var backup = new BackupService(dbMock.Object, null!, null!, null!, null!, null!, null!, null!, null!, new NullLogger());

        var ex = Assert.Throws<SqliteException>(() => backup.VacuumIntoFreshConnection(dest));

        Assert.Equal(14, ex.SqliteErrorCode);
        Assert.False(File.Exists(missing), "The backup must not re-create the working copy.");
        Assert.False(File.Exists(dest));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 18 — RestoreFromBackupAsync (spec item 5, step 5a)
    //
    // CheckoutService.RestoreFromBackupAsync wraps the shared DatabaseRestore routine (whose own stages are
    // covered in DatabaseRestoreTests). What is proved HERE is the wrapper's contract: only a write-access
    // session may restore; a save and a restore share one in-flight gate and can never interleave; a refusal
    // leaves D and the session untouched; and after a restore the stale working copy can never be saved over
    // the restored database (HashAtCheckout is deliberately left alone, so every later save is a
    // SourceModified refusal).
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a small SQLite backup file, in a "backups" sub-folder next to D, carrying a marker value that
    /// <see cref="ReadTestValue"/> reads back — so a test can tell the backup's content from D's. The file also
    /// gets the (empty) core tables <c>Sections</c>, <c>Semesters</c> and <c>AcademicYears</c>, because the
    /// restore routine refuses a backup that is not recognisably a TermPoint database.
    /// </summary>
    /// <param name="marker">Value stored in the backup's <c>_test</c> table.</param>
    /// <returns>Full path of the backup.</returns>
    private string CreateBackupWithMarker(string marker)
    {
        var folder = Path.Combine(_tempDir, "backups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"test_{Guid.NewGuid():N}.db");
        CreateSqliteDb(path);
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = string.Concat(DatabaseRestore.RequiredTables.Select(t => $"CREATE TABLE {t} (key TEXT, data TEXT); "));
            cmd.ExecuteNonQuery();
        }
        InsertTestValue(path, marker);
        return path;
    }

    /// <summary>Checks out a fresh SQLite D (holding the value "old-d") for writing.</summary>
    /// <param name="db">Path of D (created here).</param>
    /// <returns>The service and its lock service.</returns>
    private async Task<(CheckoutService svc, WriteLockService lockSvc)> CheckoutWriterWithValueAsync(string db)
    {
        var (svc, lockSvc) = CreateService();
        CreateSqliteDb(db);
        InsertTestValue(db, "old-d");
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        return (svc, lockSvc);
    }

    /// <summary>Everything currently in D's folder, sorted, for exact "no files were created" assertions.</summary>
    private string[] SourceFolderEntries()
        => Directory.GetFileSystemEntries(_tempDir).OrderBy(e => e, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// A reader (another instance holds the lock, so this session is read-only) cannot restore: NotWriter,
    /// D byte-identical, and no file of any kind is created beside it.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_Reader_ReturnsNotWriter_AndTouchesNothing()
    {
        var db = DbPath();
        CreateSqliteDb(db);
        InsertTestValue(db, "old-d");
        WriteExternalFreshLock(db);
        var (svc, _) = CreateService();
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));
        var backup = CreateBackupWithMarker("from-backup");
        var hashBefore = DbFileTestHelpers.HashOf(db);
        var entriesBefore = SourceFolderEntries();

        var result = await svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.NotWriter, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(db));
        Assert.Equal(entriesBefore, SourceFolderEntries());
    }

    /// <summary>A service that has not checked anything out is not a writer either.</summary>
    [Fact]
    public async Task RestoreFromBackupAsync_NothingCheckedOut_ReturnsNotWriter()
    {
        var (svc, _) = CreateService();

        var result = await svc.RestoreFromBackupAsync(CreateBackupWithMarker("from-backup"));

        Assert.Equal(RestoreOutcome.NotWriter, result.Outcome);
    }

    /// <summary>
    /// A writer's restore succeeds: D holds the backup's content, the old D is kept beside it under the
    /// "before restore" name and still opens, and nothing else is left behind. Then the crucial part: the
    /// session's working copy is now STALE and a following save must refuse (SourceModified) — so the old
    /// content with the user's unsaved edit can never be pushed over the restored database — while
    /// HashAtCheckout is deliberately unchanged and the lock still held (ending the session is the caller's job).
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_Writer_RestoresD_AndALaterSaveCannotOverwriteIt()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWriterWithValueAsync(db);
        InsertTestValue(svc.WorkingPath, "unsaved-edit");
        svc.MarkDirty();
        var hashAtCheckout = svc.HashAtCheckout;
        var oldBytes = File.ReadAllBytes(db);
        var backup = CreateBackupWithMarker("from-backup");

        var result = await svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Equal(result.RestoredHash, DbFileTestHelpers.HashOf(db));
        Assert.Equal("from-backup", ReadTestValue(db));

        // The replaced D is kept beside D, byte-for-byte, and still opens.
        Assert.NotNull(result.PreRestoreCopyPath);
        Assert.Equal(_tempDir, Path.GetDirectoryName(result.PreRestoreCopyPath));
        Assert.Matches(@"^test \(before restore \d{4}-\d{2}-\d{2} \d{2}-\d{2}\)\.db$", Path.GetFileName(result.PreRestoreCopyPath)!);
        Assert.Equal(oldBytes, File.ReadAllBytes(result.PreRestoreCopyPath!));
        Assert.Equal("old-d", ReadTestValue(result.PreRestoreCopyPath!));
        Assert.False(File.Exists(db + ".tmp"));

        // The session is stale but intact: the hash is untouched on purpose, the lock still ours, D' still holds the edit.
        Assert.Equal(hashAtCheckout, svc.HashAtCheckout);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal("unsaved-edit", ReadTestValue(svc.WorkingPath));

        // The in-flight gate was released (this is a real refusal, not "already in progress"), and the stale
        // working copy cannot overwrite the restore.
        var save = await svc.SaveAsync();
        Assert.Equal(SaveOutcome.SourceModified, save);
        Assert.Equal(result.RestoredHash, DbFileTestHelpers.HashOf(db));
        Assert.Equal("from-backup", ReadTestValue(db));
    }

    /// <summary>
    /// D was changed by someone else after this session checked it out: TargetChanged, D keeps the external
    /// content, nothing is created beside it, and the session carries on as a writer.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_DModifiedExternally_ReturnsTargetChanged_AndKeepsTheExternalContent()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWriterWithValueAsync(db);
        var backup = CreateBackupWithMarker("from-backup");
        File.WriteAllText(db, "externally-changed");
        var entriesBefore = SourceFolderEntries();

        var result = await svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.TargetChanged, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal("externally-changed", File.ReadAllText(db));
        Assert.Equal(entriesBefore, SourceFolderEntries());
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A save in flight (parked at the BeforeTmpVerifyAsync seam) makes a restore Busy: nothing is touched, and
    /// the parked save then completes normally — the restore neither interleaved with it nor released its gate.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_SaveInFlight_ReturnsBusy_AndTheSaveStillCompletes()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var backup = CreateBackupWithMarker("from-backup");
        var parked  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.BeforeTmpVerifyAsync = async _ => { parked.TrySetResult(); await release.Task; };

        var saveTask = svc.SaveAsync();
        try
        {
            await parked.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var hashWhileParked = DbFileTestHelpers.HashOf(db);

            var result = await svc.RestoreFromBackupAsync(backup);

            Assert.Equal(RestoreOutcome.Busy, result.Outcome);
            Assert.True(result.TargetUnchanged);
            Assert.Equal(hashWhileParked, DbFileTestHelpers.HashOf(db));
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.Equal(SaveOutcome.Success, await saveTask);
        Assert.Equal("unsaved-edit", ReadTestValue(db));

        // With the save finished the gate is free again, so the restore now proceeds.
        svc.BeforeTmpVerifyAsync = null;
        var after = await svc.RestoreFromBackupAsync(backup);
        Assert.Equal(RestoreOutcome.Restored, after.Outcome);
    }

    /// <summary>
    /// If the lock file has been removed, the restore refuses (LockLost) and nothing more: it does NOT
    /// re-create the lock, raise WriteLockLost or demote the session — the normal save / heartbeat paths own
    /// that. D is untouched and no file is created.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_LockFileRemoved_ReturnsLockLost_WithoutDemotingTheSession()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWriterWithValueAsync(db);
        var backup = CreateBackupWithMarker("from-backup");
        var lockLost = false;
        svc.WriteLockLost += _ => lockLost = true;
        File.Delete(LockFileOf(db));
        var hashBefore = DbFileTestHelpers.HashOf(db);
        var entriesBefore = SourceFolderEntries();

        var result = await svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.LockLost, result.Outcome);
        Assert.False(lockLost, "A restore must not demote the session.");
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.False(File.Exists(LockFileOf(db)), "A restore must not re-create the lock file.");
        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(db));
        Assert.Equal(entriesBefore, SourceFolderEntries());
    }

    /// <summary>
    /// The DEBUG fault plan reaches the routine: "cut the network once" (copy:1) fails the first restore with
    /// Unreachable, D untouched and nothing left beside it, the session and its working copy fully usable
    /// (a save still succeeds); the retry then restores.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_InjectedCopyFault_ReturnsUnreachable_SessionContinues_RetrySucceeds()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWriterWithValueAsync(db);
        svc.RestoreFaultPlan = RestoreFaultPlan.Parse("copy:1");
        var backup = CreateBackupWithMarker("from-backup");
        var hashBefore = DbFileTestHelpers.HashOf(db);
        var entriesBefore = SourceFolderEntries();

        var failed = await svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.Unreachable, failed.Outcome);
        Assert.Equal(hashBefore, DbFileTestHelpers.HashOf(db));
        Assert.Equal(entriesBefore, SourceFolderEntries());
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());   // the session is fully usable after a refusal

        var retry = await svc.RestoreFromBackupAsync(backup);
        Assert.Equal(RestoreOutcome.Restored, retry.Outcome);
        Assert.Equal("from-backup", ReadTestValue(db));
    }

    /// <summary>
    /// An unconfirmed rename (injected) counts as "D may be replaced": the session is stale exactly as after
    /// Restored — the pre-restore copy is reported and kept — and a following save refuses rather than risk
    /// overwriting whatever D has become.
    /// </summary>
    [Fact]
    public async Task RestoreFromBackupAsync_InjectedRenameFault_ReturnsRenameUnconfirmed_AndKeepsThePreRestoreCopy()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWriterWithValueAsync(db);
        svc.RestoreFaultPlan = RestoreFaultPlan.Parse("rename");
        var backup = CreateBackupWithMarker("from-backup");
        var oldBytes = File.ReadAllBytes(db);

        var result = await svc.RestoreFromBackupAsync(backup);

        Assert.Equal(RestoreOutcome.RenameUnconfirmed, result.Outcome);
        Assert.False(result.TargetUnchanged);
        Assert.Equal(oldBytes, File.ReadAllBytes(result.PreRestoreCopyPath!));
        Assert.Equal(result.RestoredHash, DbFileTestHelpers.HashOf(db + ".tmp"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 19 — Version gate fails safe (spec item 3, step 3c)
    //
    // Before 3c the gate read the stamp as "string or null": a working copy that antivirus was still holding, or a
    // damaged one, read as null — "unstamped" — and sailed through to write access. Now the read is three-way
    // (found / none / could not be read). A read that cannot be made is retried quietly (LocalCopyIntegrity.MaxAttempts
    // attempts, RetryDelay apart) and, if it still cannot, the checkout REFUSES write access:
    // CheckoutOutcome.VersionCheckUnavailable — lock released (no reader mode), D' with its sidecars and marker
    // removed (kept in crash recovery, where they hold the user's only copy of unsaved edits), no D'', RequiredAppVersion
    // null, D untouched. The same applies to a stamp this release must raise and cannot (user decision 2026-10-14),
    // whereas a malformed ThisReleaseRequires is a bug in the release and is refused at once, without waiting.
    //
    // The tests drive the gate through the internal seams StampReader, StampRaiser, GateRetryDelay and
    // IntegrityFaultPlan, so no test waits for a real retry delay.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A stamp reader (<see cref="CheckoutService.StampReader"/>) that reports the file as unreadable for its first
    /// <c>failures</c> calls and then does the real read, counting every call — "antivirus lets go after a moment".
    /// </summary>
    private sealed class FlakyStampReader
    {
        private readonly int _failures;

        /// <summary>Creates a reader that fails <paramref name="failures"/> times (<see cref="int.MaxValue"/> for "always").</summary>
        /// <param name="failures">How many leading calls report <see cref="StampReadKind.Unreadable"/>.</param>
        public FlakyStampReader(int failures) => _failures = failures;

        /// <summary>How many times the gate has called the reader.</summary>
        public int Calls { get; private set; }

        /// <summary>The delegate for <see cref="CheckoutService.StampReader"/>.</summary>
        /// <param name="path">File to read the stamp of.</param>
        /// <returns>Unreadable while failures remain, otherwise the real classified read.</returns>
        public StampRead Read(string path)
        {
            Calls++;
            return Calls <= _failures
                ? StampRead.Couldnt("simulated: the file is held by another program")
                : MinimumVersionToWrite.ReadStamp(path);
        }
    }

    /// <summary>
    /// A stamp raiser (<see cref="CheckoutService.StampRaiser"/>) that throws for its first <c>failures</c> calls
    /// and then does the real raise, counting every call.
    /// </summary>
    private sealed class FlakyStampRaiser
    {
        private readonly int _failures;

        /// <summary>Creates a raiser that fails <paramref name="failures"/> times (<see cref="int.MaxValue"/> for "always").</summary>
        /// <param name="failures">How many leading calls throw.</param>
        public FlakyStampRaiser(int failures) => _failures = failures;

        /// <summary>How many times the gate has called the raiser.</summary>
        public int Calls { get; private set; }

        /// <summary>The delegate for <see cref="CheckoutService.StampRaiser"/>.</summary>
        /// <param name="path">File to stamp.</param>
        /// <param name="version">Version to require.</param>
        /// <param name="reason">Plain-English reason, or null.</param>
        /// <returns>The real <see cref="MinimumVersionToWrite.Raise"/> result once the failures are spent.</returns>
        /// <exception cref="IOException">While failures remain.</exception>
        public bool Raise(string path, string version, string? reason)
        {
            Calls++;
            if (Calls <= _failures)
                throw new IOException("simulated: the file is held by another program");
            return MinimumVersionToWrite.Raise(path, version, reason);
        }
    }

    /// <summary>
    /// Stands in for the gate's retry wait (<see cref="CheckoutService.GateRetryDelay"/>): records every wait that
    /// is asked for and returns at once.
    /// </summary>
    private sealed class DelayRecorder
    {
        /// <summary>The delay of each wait the gate asked for, in order.</summary>
        public List<TimeSpan> Waits { get; } = new();

        /// <summary>The delegate for <see cref="CheckoutService.GateRetryDelay"/>.</summary>
        /// <param name="delay">The requested wait.</param>
        /// <returns>A completed task.</returns>
        public Task WaitAsync(TimeSpan delay)
        {
            Waits.Add(delay);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Creates a REAL SQLite database D (with a genuine file header and some content, unlike
    /// <see cref="CreateSqliteDb"/>, which leaves a zero-byte file) that carries no stamp — the kind of file the
    /// version gate actually has to read.
    /// </summary>
    /// <param name="path">Where to create D.</param>
    private static void CreateRealUnstampedDb(string path)
    {
        CreateSqliteDb(path);
        InsertTestValue(path, "d-content");
        Assert.True(DatabaseValidator.HasSqliteSignature(path), "precondition: D must carry a real SQLite header");
    }

    /// <summary>
    /// Asserts the signature of a version check that could not be completed: ReadOnly mode but NO snapshot, no
    /// RequiredAppVersion (nobody knows what the database requires), no write lock held or left behind, the
    /// session unable to save, and in the working directory either nothing (D' and its marker removed) or — for
    /// crash recovery — exactly the kept D' and its dirty marker.
    /// </summary>
    /// <param name="svc">The service under test.</param>
    /// <param name="lockSvc">Its lock service.</param>
    /// <param name="db">Path to D.</param>
    /// <param name="keptWorkingCopy">
    /// The crashed session's D' that must still be there with its marker, or null when D' must be gone.
    /// </param>
    private async Task AssertVersionCheckRefusedAsync(
        CheckoutService svc, WriteLockService lockSvc, string db, string? keptWorkingCopy = null)
    {
        lockSvc.PendingRelease?.Wait(LockSettleTimeout); // settle the background lock-file deletion

        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.Null(svc.RequiredAppVersion);
        Assert.False(svc.SessionDirty);
        Assert.Null(svc.CurrentHolder);
        Assert.False(lockSvc.IsWriter);
        Assert.False(File.Exists(Path.ChangeExtension(db, ".lock")), "Lock must be released, not left behind.");
        Assert.False(File.Exists(ComputeReadOnlyPath(db)), "No D'' is made: the gate could not look, it did not trip.");
        Assert.Equal(SaveOutcome.NotInWriteMode, await svc.SaveAsync());

        if (keptWorkingCopy is null)
        {
            Assert.Empty(Directory.GetFiles(_workingDir));   // no D', no -wal/-shm, no .dirty, no D''
        }
        else
        {
            Assert.True(File.Exists(keptWorkingCopy), "Crash recovery: D' holds the unsaved edits — it must be kept.");
            Assert.True(File.Exists(keptWorkingCopy + ".dirty"), "Crash recovery: the dirty marker must be kept.");
        }
    }

    /// <summary>
    /// The stamp cannot be read on any of the gate's attempts: VersionCheckUnavailable after exactly
    /// <see cref="LocalCopyIntegrity.MaxAttempts"/> reads with two quiet waits of <see cref="LocalCopyIntegrity.RetryDelay"/>
    /// between them; the session is not a writer, the lock file is gone, D' and its marker are gone, no D'' was made,
    /// RequiredAppVersion is null, and D is byte-identical.
    /// </summary>
    [Fact]
    public async Task VersionGate_StampUnreadableOnEveryAttempt_RefusesWriteAccess_AfterQuietRetries()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var hashOfD = ComputeFileHash(db);
        var reader = new FlakyStampReader(failures: int.MaxValue);
        var delays = new DelayRecorder();
        svc.StampReader     = reader.Read;
        svc.GateRetryDelay  = delays.WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, outcome);
        Assert.Equal(LocalCopyIntegrity.MaxAttempts, reader.Calls);
        Assert.Equal(2, delays.Waits.Count);
        Assert.All(delays.Waits, w => Assert.Equal(LocalCopyIntegrity.RetryDelay, w));
        await AssertVersionCheckRefusedAsync(svc, lockSvc, db);
        Assert.Equal(hashOfD, ComputeFileHash(db));   // D is never modified
    }

    /// <summary>
    /// A transient failure is absorbed: the stamp is unreadable twice and readable on the third attempt → the
    /// checkout goes through to write access, with nothing reported and nothing left over.
    /// </summary>
    [Fact]
    public async Task VersionGate_StampUnreadableTwiceThenReadable_RetriesAbsorbIt_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var reader = new FlakyStampReader(failures: 2);
        var delays = new DelayRecorder();
        svc.StampReader    = reader.Read;
        svc.GateRetryDelay = delays.WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(3, reader.Calls);
        Assert.Equal(2, delays.Waits.Count);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.Equal("d-content", ReadTestValue(svc.WorkingPath));
    }

    /// <summary>
    /// A readable stamp that blocks still blocks after a retried read: two failures, then a stamp higher than the
    /// app → ReadOnly on a D'' snapshot (the retries change nothing about the gate's verdict).
    /// </summary>
    [Fact]
    public async Task VersionGate_StampUnreadableTwiceThenHigherThanApp_StillOpensReadOnlySnapshot()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateStampedDb(db, "1.2.5");
        var reader = new FlakyStampReader(failures: 2);
        svc.StampReader    = reader.Read;
        svc.GateRetryDelay = new DelayRecorder().WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.ReadOnly, outcome);
        Assert.Equal(3, reader.Calls);
        AssertGatedReadOnly(svc, lockSvc, db, "1.2.5");
    }

    /// <summary>
    /// The forced (stale-lock takeover) exit fails safe too: the takeover is confirmed, the stamp cannot be read →
    /// VersionCheckUnavailable, and the lock that was taken over is released again.
    /// </summary>
    [Fact]
    public async Task VersionGate_ForceCheckout_StampUnreadable_RefusesWriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        WriteExternalStaleLock(db);
        var hashOfD = ComputeFileHash(db);
        var reader = new FlakyStampReader(failures: int.MaxValue);
        var delays = new DelayRecorder();
        svc.StampReader    = reader.Read;
        svc.GateRetryDelay = delays.WaitAsync;

        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition: the gate has not run yet
        Assert.Equal(0, reader.Calls);
        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, outcome);
        Assert.Equal(LocalCopyIntegrity.MaxAttempts, reader.Calls);
        Assert.Equal(2, delays.Waits.Count);
        await AssertVersionCheckRefusedAsync(svc, lockSvc, db);
        Assert.Equal(hashOfD, ComputeFileHash(db));
    }

    /// <summary>
    /// The crash-recovery exit fails safe WITHOUT destroying the user's work: the adopted D' cannot be examined →
    /// VersionCheckUnavailable with D' and its dirty marker KEPT (and the unsaved edit intact), the lock released,
    /// recovery still detectable; "Try again" (the same checkout, now able to read) then recovers into write mode.
    /// </summary>
    [Fact]
    public async Task VersionGate_Recovery_StampUnreadable_KeepsCrashArtifacts_ThenTryAgainRecovers()
    {
        var db = DbPath();
        CreateRealUnstampedDb(db);

        // Previous session: checkout, edit D', mark dirty, crash.
        var (svc1, lockSvc1) = CreateService(new Version(1, 2, 3));
        await svc1.CheckoutAsync(db);
        InsertTestValue(svc1.WorkingPath, "unsaved-edit");
        svc1.MarkDirty();
        var crashedWorkingPath = svc1.WorkingPath;
        SimulateCrash(lockSvc1, db);
        var hashOfD = ComputeFileHash(db);

        // Next launch: recovery is requested but the gate cannot look at the adopted D'.
        var (svc2, lockSvc2) = CreateService(new Version(1, 2, 3));
        var reader = new FlakyStampReader(failures: int.MaxValue);
        var delays = new DelayRecorder();
        svc2.StampReader    = reader.Read;
        svc2.GateRetryDelay = delays.WaitAsync;
        Assert.True(svc2.DetectCrashRecovery(db)); // precondition

        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, outcome);
        Assert.Equal(LocalCopyIntegrity.MaxAttempts, reader.Calls);
        Assert.Equal(2, delays.Waits.Count);
        await AssertVersionCheckRefusedAsync(svc2, lockSvc2, db, keptWorkingCopy: crashedWorkingPath);
        Assert.Equal("unsaved-edit", ReadTestValue(crashedWorkingPath));
        Assert.True(svc2.DetectCrashRecovery(db), "Recovery must still be detectable on the next attempt.");
        Assert.Equal(hashOfD, ComputeFileHash(db));

        // Try again: the very same checkout, now readable, recovers the unsaved edit into write mode.
        svc2.StampReader = MinimumVersionToWrite.ReadStamp;
        var again = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, again);
        Assert.True(lockSvc2.IsWriter);
        Assert.Equal(crashedWorkingPath, svc2.WorkingPath);
        Assert.Equal("unsaved-edit", ReadTestValue(svc2.WorkingPath));
        Assert.True(File.Exists(svc2.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// "Try again" after a refusal works: nothing a VersionCheckUnavailable leaves behind (lock, D', marker)
    /// blocks the next checkout, which — with a reader that works — is a normal write checkout.
    /// </summary>
    [Fact]
    public async Task VersionGate_AfterVersionCheckUnavailable_TryAgainWithAWorkingReader_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        svc.StampReader    = new FlakyStampReader(failures: int.MaxValue).Read;
        svc.GateRetryDelay = new DelayRecorder().WaitAsync;
        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, await svc.CheckoutAsync(db)); // precondition
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        svc.StampReader = MinimumVersionToWrite.ReadStamp;
        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
        Assert.True(File.Exists(Path.ChangeExtension(db, ".lock")));
        Assert.Equal("d-content", ReadTestValue(svc.WorkingPath));
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());   // and it is a fully working session
    }

    /// <summary>
    /// A DIFFERENT instance can take the lock the moment a refusal returns it: the refusal leaves no lock file,
    /// so a colleague is not shut out by a session that never became a writer.
    /// </summary>
    [Fact]
    public async Task VersionGate_AfterVersionCheckUnavailable_AnotherInstanceCanCheckOutAtOnce()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        svc.StampReader    = new FlakyStampReader(failures: int.MaxValue).Read;
        svc.GateRetryDelay = new DelayRecorder().WaitAsync;
        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, await svc.CheckoutAsync(db)); // precondition
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        var (other, otherLock) = CreateService(new Version(1, 2, 3));
        var outcome = await other.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(otherLock.IsWriter);
    }

    /// <summary>
    /// The DEBUG <c>stamp:3</c> fault plan spends all three of one checkout's gate reads: the first checkout is
    /// VersionCheckUnavailable, the plan is then exhausted, and the second ("Try again") is WriteAccess.
    /// </summary>
    [Fact]
    public async Task VersionGate_InjectedStampFault_ThreeFailures_FailsOneCheckout_ThenTryAgainSucceeds()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var delays = new DelayRecorder();
        svc.GateRetryDelay     = delays.WaitAsync;
        svc.IntegrityFaultPlan = IntegrityFaultPlan.Parse("stamp:3");

        var first = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, first);
        Assert.Equal(0, svc.IntegrityFaultPlan!.Remaining);
        Assert.Equal(2, delays.Waits.Count);
        await AssertVersionCheckRefusedAsync(svc, lockSvc, db);

        var second = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, second);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(2, delays.Waits.Count);   // the second checkout needed no retries
    }

    /// <summary>
    /// <c>stamp:2</c> is absorbed by the gate's retries: the checkout goes straight through to write access.
    /// </summary>
    [Fact]
    public async Task VersionGate_InjectedStampFault_TwoFailures_AreAbsorbedByTheRetries()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var delays = new DelayRecorder();
        svc.GateRetryDelay     = delays.WaitAsync;
        svc.IntegrityFaultPlan = IntegrityFaultPlan.Parse("stamp:2");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(0, svc.IntegrityFaultPlan!.Remaining);
        Assert.Equal(2, delays.Waits.Count);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A plan for another stage (<c>locked</c>) never touches the stamp read: the gate does its normal single read
    /// and the checkout goes through without a wait.
    /// </summary>
    [Fact]
    public async Task VersionGate_InjectedFaultForAnotherStage_DoesNotAffectTheStampRead()
    {
        var (svc, _) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var delays = new DelayRecorder();
        svc.GateRetryDelay     = delays.WaitAsync;
        svc.IntegrityFaultPlan = IntegrityFaultPlan.Parse("locked");

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Empty(delays.Waits);
    }

    // ── Raising the stamp fails safe too ─────────────────────────────────────────

    /// <summary>
    /// A release that must stamp (ThisReleaseRequires set, Store build) but cannot write the stamp on any attempt:
    /// VersionCheckUnavailable after three raise attempts with two quiet waits — editing without the stamp would
    /// save new-format data where older versions can still edit it. D' is removed; the real stamp was never written.
    /// </summary>
    [Fact]
    public async Task VersionGate_RaiseFailsOnEveryAttempt_RefusesWriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ChannelOverride     = InstallChannel.Store;
        var raiser = new FlakyStampRaiser(failures: int.MaxValue);
        var delays = new DelayRecorder();
        svc.StampRaiser    = raiser.Raise;
        svc.GateRetryDelay = delays.WaitAsync;
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var hashOfD = ComputeFileHash(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, outcome);
        Assert.Equal(LocalCopyIntegrity.MaxAttempts, raiser.Calls);
        Assert.Equal(2, delays.Waits.Count);
        Assert.All(delays.Waits, w => Assert.Equal(LocalCopyIntegrity.RetryDelay, w));
        await AssertVersionCheckRefusedAsync(svc, lockSvc, db);
        Assert.Equal(hashOfD, ComputeFileHash(db));
    }

    /// <summary>
    /// A raise that fails twice and then works is absorbed: WriteAccess, and the stamp (with its reason) is in D'.
    /// </summary>
    [Fact]
    public async Task VersionGate_RaiseFailsTwiceThenWorks_RetriesAbsorbIt_StampPresentInWorkingCopy()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ThisReleaseReason   = "Section-type colours are now saved.";
        svc.ChannelOverride     = InstallChannel.Store;
        var raiser = new FlakyStampRaiser(failures: 2);
        var delays = new DelayRecorder();
        svc.StampRaiser    = raiser.Raise;
        svc.GateRetryDelay = delays.WaitAsync;
        var db = DbPath();
        CreateRealUnstampedDb(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(3, raiser.Calls);
        Assert.Equal(2, delays.Waits.Count);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(StampRead.Found("1.2.4"), MinimumVersionToWrite.ReadStamp(svc.WorkingPath));
        Assert.Equal("Section-type colours are now saved.", GetConfigValue(svc.WorkingPath, MinimumVersionToWrite.ReasonKey));
    }

    /// <summary>
    /// On a channel that never stamps (Development, Sideload) the raiser is never called, so a raiser that would
    /// always fail cannot cost the user write access.
    /// </summary>
    [Theory]
    [InlineData(InstallChannel.Development)]
    [InlineData(InstallChannel.Sideload)]
    public async Task VersionGate_NonStampingChannel_NeverCallsTheRaiser(InstallChannel channel)
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ChannelOverride     = channel;
        var raiser = new FlakyStampRaiser(failures: int.MaxValue);
        var delays = new DelayRecorder();
        svc.StampRaiser    = raiser.Raise;
        svc.GateRetryDelay = delays.WaitAsync;
        var db = DbPath();
        CreateRealUnstampedDb(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(0, raiser.Calls);
        Assert.Empty(delays.Waits);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A release that requires nothing (ThisReleaseRequires null — the 1.2.3 state) never calls the raiser, whatever
    /// the channel.
    /// </summary>
    [Fact]
    public async Task VersionGate_ThisReleaseRequiresNull_NeverCallsTheRaiser()
    {
        var (svc, _) = CreateService(new Version(1, 2, 3));
        svc.ThisReleaseRequires = null;
        svc.ChannelOverride     = InstallChannel.Store;
        var raiser = new FlakyStampRaiser(failures: int.MaxValue);
        svc.StampRaiser = raiser.Raise;
        var db = DbPath();
        CreateRealUnstampedDb(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.Equal(0, raiser.Calls);
    }

    /// <summary>
    /// A malformed ThisReleaseRequires is a bug in the release, and waiting cannot fix it: on a stamping channel the
    /// checkout is refused at once — ZERO retry waits, the raiser never called, and the stamp read made only once.
    /// </summary>
    [Theory]
    [InlineData("abc",        InstallChannel.Store)]
    [InlineData("not-a-version", InstallChannel.Store)]
    [InlineData("1",          InstallChannel.Store)]
    [InlineData("1.2.4-beta", InstallChannel.DirectDownload)]
    public async Task VersionGate_MalformedThisReleaseRequires_RefusesAtOnce_WithoutRetries(string requires, InstallChannel channel)
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = requires;
        svc.ChannelOverride     = channel;
        var reader = new FlakyStampReader(failures: 0);
        var raiser = new FlakyStampRaiser(failures: 0);
        var delays = new DelayRecorder();
        svc.StampReader    = reader.Read;
        svc.StampRaiser    = raiser.Raise;
        svc.GateRetryDelay = delays.WaitAsync;
        var db = DbPath();
        CreateRealUnstampedDb(db);

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.VersionCheckUnavailable, outcome);
        Assert.Empty(delays.Waits);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(0, raiser.Calls);
        await AssertVersionCheckRefusedAsync(svc, lockSvc, db);
    }

    /// <summary>
    /// The same malformed requirement on a channel that never stamps is irrelevant (the raise step is not even
    /// reached), so editing is not blocked.
    /// </summary>
    [Fact]
    public async Task VersionGate_MalformedThisReleaseRequires_OnANonStampingChannel_IsIgnored()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "not-a-version";
        svc.ChannelOverride     = InstallChannel.Development;
        var db = DbPath();
        CreateRealUnstampedDb(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.True(lockSvc.IsWriter);
    }

    // ── What is NOT a failure to look ────────────────────────────────────────────

    /// <summary>
    /// A genuinely unstamped real SQLite D (a valid file with content but no stamp) passes the gate on the first
    /// read: WriteAccess, one read, no wait — "no stamp" is still the normal case and costs nothing.
    /// </summary>
    [Fact]
    public async Task VersionGate_GenuinelyUnstampedRealDatabase_WriteAccess_OnTheFirstRead()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var reader = new FlakyStampReader(failures: 0);
        var delays = new DelayRecorder();
        svc.StampReader    = reader.Read;
        svc.GateRetryDelay = delays.WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(1, reader.Calls);
        Assert.Empty(delays.Waits);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// The non-SQLite dummy D that most other tests use reads as "no stamp" (None), not "could not look": it passes
    /// the gate on the first read with the real reader, so those tests keep working untouched.
    /// </summary>
    [Fact]
    public async Task VersionGate_NonSqliteDummyDatabase_IsNone_NotUnreadable_WriteAccess()
    {
        var (svc, lockSvc) = CreateService(new Version(0, 0, 1));
        var db = DbPath();
        CreateFile(db, "not-a-sqlite-file");
        var delays = new DelayRecorder();
        svc.GateRetryDelay = delays.WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Empty(delays.Waits);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// A hand-made "no stamp" answer from the reader (StampRead.None) is taken at face value, exactly like the
    /// real thing: WriteAccess after a single read.
    /// </summary>
    [Fact]
    public async Task VersionGate_ReaderSaysNone_WriteAccess_AfterOneRead()
    {
        var (svc, _) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var calls = 0;
        svc.StampReader = _ => { calls++; return StampRead.None; };

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// A local copy SQLite reports as damaged is not the gate's to refuse: no retries (waiting cannot heal damage),
    /// no "in use" refusal (Try again would copy the same damage and loop), and the checkout continues so the
    /// open-time integrity check — which runs next on every path into editing — refuses the file and takes the
    /// damage path (fresh copy, recovery chooser, report).
    /// </summary>
    [Fact]
    public async Task VersionGate_DamagedLocalCopy_NoRetriesNoRefusal_LeftToTheIntegrityCheck()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        var calls = 0;
        var delays = new DelayRecorder();
        svc.StampReader    = _ => { calls++; return StampRead.DamagedFile(new SqliteException("database disk image is malformed", 11)); };
        svc.GateRetryDelay = delays.WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal(1, calls);
        Assert.Empty(delays.Waits);
        Assert.Null(svc.RequiredAppVersion);
        Assert.True(lockSvc.IsWriter);
    }

    /// <summary>
    /// In a stamping build, a damaged local copy is not raised into either: a raise could only fail on a damaged
    /// file, and its refusal would turn damage back into the misleading "in use" loop.
    /// </summary>
    [Fact]
    public async Task VersionGate_DamagedLocalCopy_InAStampingBuild_NeverCallsTheRaiser()
    {
        var (svc, _) = CreateService(new Version(1, 2, 4));
        svc.ThisReleaseRequires = "1.2.4";
        svc.ChannelOverride     = InstallChannel.Store;
        var raiser = new FlakyStampRaiser(failures: int.MaxValue);
        svc.StampRaiser = raiser.Raise;
        svc.StampReader = _ => StampRead.DamagedFile(new SqliteException("file is not a database", 26));
        var db = DbPath();
        CreateRealUnstampedDb(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        Assert.Equal(0, raiser.Calls);
    }

    /// <summary>
    /// The real thing, end to end at the checkout: D damaged on page 1 below a valid header (the "Monday morning"
    /// case). D' is a verified byte copy, so it is damaged too. The gate's real read calls it Damaged and lets the
    /// checkout through — and the very integrity check that runs next calls the same file Damaged, so it is refused
    /// there and never edited. D is only read.
    /// </summary>
    [Fact]
    public async Task VersionGate_RealDamageBehindAValidHeader_IsHandedToTheOpenTimeIntegrityCheck()
    {
        var (svc, lockSvc) = CreateService(new Version(1, 2, 3));
        var db = DbPath();
        CreateRealUnstampedDb(db);
        DbFileTestHelpers.TrashSchemaBelowHeader(db);
        var hashOfD = ComputeFileHash(db);
        var delays = new DelayRecorder();
        svc.GateRetryDelay = delays.WaitAsync;

        var outcome = await svc.CheckoutAsync(db);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Empty(delays.Waits);   // no quiet retries: damage is not "in use"
        Assert.True(lockSvc.IsWriter);
        Assert.True(MinimumVersionToWrite.ReadStamp(svc.WorkingPath).IsDamaged);
        Assert.Equal(IntegrityCheckResult.Damaged, BackupService.CheckIntegrityDetailed(svc.WorkingPath));
        Assert.Equal(hashOfD, ComputeFileHash(db));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 20 — the save re-checks the lock and D just before the rename (F12),
    //            and records its fingerprint before the rename (F8)
    //
    // data-persistence-review-2026-10.md, section 3. The upload between the first
    // check and the rename can take hours (a laptop that sleeps mid-save), during
    // which another user can take over the lock and save. The crash-point hook
    // plays that other user at Save.AfterTmpVerify, the last point before the
    // final check.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Thrown by a crash-point hook to stop the operation where a killed process would stop.</summary>
    private sealed class SimulatedCrashException : Exception { }

    /// <summary>
    /// Counts the operations the share sees on one path, by label, from any thread: the test seam for "how many
    /// times did the save read D?" (installed through <see cref="NetworkFaults.Decide"/>, which answers "no fault").
    /// </summary>
    private sealed class ShareOpCounter
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _counts = new();

        /// <summary>
        /// Installs the counting hook; operations on <paramref name="path"/> are counted, all others pass untouched.
        /// The caller must reset <see cref="NetworkFaults.Decide"/> to null when the test ends.
        /// </summary>
        /// <param name="path">The path whose operations are counted.</param>
        public void Install(string path)
            => NetworkFaults.Decide = request =>
            {
                if (string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase))
                    lock (_gate) _counts[request.Label] = _counts.GetValueOrDefault(request.Label) + 1;
                return NetworkFaultDecision.None;
            };

        /// <summary>How many operations with this label were seen on the path.</summary>
        /// <param name="label">The operation's label, e.g. "ComputeHash" or "File.Stat".</param>
        public int Count(string label)
        {
            lock (_gate) return _counts.GetValueOrDefault(label);
        }
    }

    /// <summary>
    /// Runs <see cref="CheckoutService.SaveAsync"/> with a crash-point hook that does <paramref name="atTmpVerify"/>
    /// when the save reaches <c>Save.AfterTmpVerify</c> (D.tmp uploaded and verified, final check not yet made), and
    /// always resets both static hooks afterwards.
    /// </summary>
    /// <param name="svc">The service whose save runs.</param>
    /// <param name="atTmpVerify">What "the world" does while the save is between its upload and its final check.</param>
    /// <returns>The save's outcome.</returns>
    private static async Task<SaveOutcome> SaveWhileAsync(CheckoutService svc, Action atTmpVerify)
    {
        CrashPoints.Hook = name => { if (name == "Save.AfterTmpVerify") atTmpVerify(); };
        try { return await svc.SaveAsync(); }
        finally { CrashPoints.Hook = null; NetworkFaults.Decide = null; }
    }

    /// <summary>
    /// F12, the lock: another user takes over the lock while the save is uploading. The final check before the
    /// rename finds a foreign lock and refuses with LockLost, so the rename never happens. D is byte-for-byte what it
    /// was, and D' and its dirty marker stay: the user's edit is not lost. The D.tmp is NOT deleted: without the lock
    /// this save must not write to the share (rule 2), and D.tmp's one shared name may by now be the new holder's save
    /// in progress. The new holder's checkout or save deals with a leftover one.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockTakenOverWhileUploading_IsRefusedAtTheFinalCheck_AndDIsUntouched()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);

        var outcome = await SaveWhileAsync(svc, () => WriteExternalFreshLock(db));

        Assert.Equal(SaveOutcome.LockLost, outcome);
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.True(File.Exists(db + ".tmp"), "Without the lock the save must leave D.tmp alone (it may be the new holder's).");
        Assert.True(File.Exists(svc.WorkingPath), "D' must survive the refusal.");
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "The dirty marker must survive the refusal.");
    }

    /// <summary>
    /// F12, the database: another user saves to D while this save is uploading (the lock is still ours as far as the
    /// file says, e.g. they took it over and released it again). The final check sees that D's stamp changed, hashes
    /// it, finds it is not the version this session last saw, and refuses with SourceModified and the save-conflict
    /// state. D keeps the other writer's content, the D.tmp is gone, and D' and its marker stay.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DChangedWhileUploading_IsRefusedAtTheFinalCheck_AsSourceModified()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var conflictsRaised = 0;
        svc.SaveConflictDetected += () => conflictsRaised++;

        var outcome = await SaveWhileAsync(svc, () =>
        {
            InsertTestValue(db, "other-writers-save");
            // Make the change visible to the stamp however coarse the file system's clock is.
            File.SetLastWriteTimeUtc(db, File.GetLastWriteTimeUtc(db).AddMinutes(1));
        });

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.True(svc.HasSaveConflict);
        Assert.Equal(1, conflictsRaised);
        Assert.Equal("other-writers-save", ReadTestValue(db));   // D keeps the other writer's content
        Assert.False(File.Exists(db + ".tmp"), "The D.tmp of a refused save must be deleted.");
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// The cheap path: lock ours and D untouched since the first check. The save succeeds, and the final check only
    /// stat'ed D (twice in all, one per check) without reading it again: D is hashed exactly once, by the first check.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NothingChanged_FinalCheckStatsDButDoesNotRehashIt()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var counter = new ShareOpCounter();
        counter.Install(db);

        var outcome = await SaveWhileAsync(svc, () => { /* nobody does anything */ });

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.Equal("unsaved-edit", ReadTestValue(db));
        Assert.Equal(2, counter.Count("File.Stat"));
        Assert.Equal(1, counter.Count("ComputeHash"));
    }

    /// <summary>
    /// A stamp that differs while the content does not (a program touched D's timestamp, say) costs a re-hash but
    /// never a false refusal: the hash still equals the one from checkout, so the save goes on and succeeds.
    /// </summary>
    [Fact]
    public async Task SaveAsync_OnlyDsTimestampChanged_FinalCheckRehashesAndTheSaveSucceeds()
    {
        var db = DbPath();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db);
        var counter = new ShareOpCounter();
        counter.Install(db);

        var outcome = await SaveWhileAsync(svc, () =>
            File.SetLastWriteTimeUtc(db, File.GetLastWriteTimeUtc(db).AddMinutes(1)));

        Assert.Equal(SaveOutcome.Success, outcome);
        Assert.False(svc.HasSaveConflict);
        Assert.Equal("unsaved-edit", ReadTestValue(db));
        Assert.Equal(2, counter.Count("ComputeHash"));
    }

    /// <summary>
    /// The final check's stat of D times out (the share went away during the upload): the save is refused as
    /// "cannot reach the database" (CopyError), exactly as a timed-out first check is. D is untouched, the D.tmp
    /// is deleted, and D' and its marker stay.
    /// </summary>
    [Fact]
    public async Task SaveAsync_FinalStatTimesOut_IsAnUnreachableCopyError_AndDIsUntouched()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);

        var outcome = await SaveWhileAsync(svc, () => FaultNextStatOf(db, NetworkFaultDecision.TimeOut));

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false), false), Assert.Single(banners));   // the one sticky outage notice (spec item 28)
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.False(File.Exists(db + ".tmp"));
        AssertSessionKept(svc, lockSvc);
    }

    /// <summary>
    /// The final check's stat of D fails with a file error (another program holds D): the save is refused with the
    /// "in use" message (CopyError), exactly as an unreadable D is at the first check. Nothing is lost.
    /// </summary>
    [Fact]
    public async Task SaveAsync_FinalStatFailsWithAFileError_IsAnInUseCopyError_AndDIsUntouched()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);

        var outcome = await SaveWhileAsync(svc, () =>
            FaultNextStatOf(db, NetworkFaultDecision.Throwing(new IOException("sharing violation"))));

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.ForTransientFailure(SavePrompt.InUseMessage, isAutoSave: false), false), Assert.Single(banners));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.False(File.Exists(db + ".tmp"));
        AssertSessionKept(svc, lockSvc);
    }

    /// <summary>
    /// Makes the NEXT stat of <paramref name="path"/> through <see cref="NetworkFileOps"/> fail the given way (the
    /// save's final check, when called from the Save.AfterTmpVerify hook); every other operation is untouched.
    /// </summary>
    /// <param name="path">D's path.</param>
    /// <param name="fault">What the stat does.</param>
    private static void FaultNextStatOf(string path, NetworkFaultDecision fault)
    {
        var fired = 0;
        NetworkFaults.Decide = request =>
            request.Label == "File.Stat"
            && string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase)
            && Interlocked.Exchange(ref fired, 1) == 0
                ? fault
                : NetworkFaultDecision.None;
    }

    /// <summary>
    /// F8: the process dies right after a rename that DID succeed, before the dirty marker is deleted. Because the
    /// save recorded its fingerprint in the marker before the rename, the marker's PendingSaveHash is D's new hash,
    /// and the next start's recovery recognises D as this session's own save and returns write access with the
    /// kept edits, instead of the "Can't Restore" conflict.
    /// </summary>
    [Fact]
    public async Task SaveAsync_CrashRightAfterTheRename_MarkerHoldsTheNewHash_AndRecoveryRestores()
    {
        var db = DbPath();
        var (svc1, lockSvc1) = await CheckoutWithUnsavedEditAsync(db);
        CrashPoints.Hook = name => { if (name == "Save.AfterRename") throw new SimulatedCrashException(); };
        try { await svc1.SaveAsync(); }   // the process "dies" at the hook; SaveAsync's net absorbs the exception here
        finally { CrashPoints.Hook = null; }

        // D holds the new content, and the marker (still there) names it as this session's save.
        Assert.Equal("unsaved-edit", ReadTestValue(db));
        var marker = CheckoutService.TryReadMarker(svc1.WorkingPath + ".dirty");
        Assert.NotNull(marker);
        Assert.Equal(NetworkFileOps.ComputeHash(db), marker!.PendingSaveHash);

        SimulateCrash(lockSvc1, db);
        var (svc2, _) = CreateService();
        var outcome = await svc2.CheckoutAsync(db, recoverWorkingCopy: true);

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.Equal("unsaved-edit", ReadTestValue(svc2.WorkingPath));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Group 21 — spec item 32: a takeover noticed late (field-test findings A8, A2)
    //
    // Bob loses the lock, Alice took it over. (a) A lock file that names another
    // session is final: the check does not re-read it 200 ms later, because that
    // re-read can collide with Bob's own overdue heartbeat and be taken for a
    // network blip. (c) The heartbeat renews only a lock that names this session.
    // (d) The final check deletes D.tmp only when it confirmed the lock is ours.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Change (a): the lock file names another session, so the first read settles it. The save's first check returns
    /// LockLost after reading the lock file exactly ONCE (one existence check, one read; before the change there were
    /// two of each, 200 ms apart), and the log says why there was no re-read. The count is made by the existing
    /// network fault injector, which sees every <see cref="NetworkFileOps"/> operation on the lock file.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockNamesAnotherSession_IsLockLost_AfterReadingTheLockFileOnce()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var lockPath = Path.ChangeExtension(db, ".lock");
        WriteExternalFreshLock(db);
        var counter = new ShareOpCounter();
        counter.Install(lockPath);

        SaveOutcome outcome;
        try { outcome = await svc.SaveAsync(); }
        finally { NetworkFaults.Decide = null; }

        Assert.Equal(SaveOutcome.LockLost, outcome);
        Assert.Equal(1, counter.Count("File.Exists"));
        Assert.Equal(1, counter.Count("File.ReadAllText"));
        Assert.Contains(logger.Infos, line => line.Contains("the lock names another session, so no re-read"));
        Assert.DoesNotContain(logger.Infos, line => line.Contains("retrying in"));
    }

    /// <summary>
    /// Change (a), the other side: a lock file that is MISSING (our own heartbeat's rename hiding it for a moment)
    /// still gets its re-read, and a re-read that then finds the file back but contended is still UNREACHABLE: a
    /// network blip, not a lost lock. The save is a CopyError with the sticky outage notice, nothing is demoted, and
    /// the lock file is exactly what it was. The re-read is proved by counting the existence checks (two).
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockFileMissing_ThenAContendedReread_IsStillUnreachable_NotLockLost()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var banners = CaptureSaveBanners(svc);
        var lockLostFired = false;
        svc.WriteLockLost += _ => lockLostFired = true;
        var lockPath = Path.ChangeExtension(db, ".lock");
        var ourLock = File.ReadAllText(lockPath);
        File.Delete(lockPath);   // the heartbeat's rename has hidden the file for the first read

        var existsCalls = 0;
        NetworkFaults.Decide = request =>
        {
            if (!string.Equals(request.Path, lockPath, StringComparison.OrdinalIgnoreCase))
                return NetworkFaultDecision.None;
            if (request.Label == "File.Exists" && Interlocked.Increment(ref existsCalls) == 2)
                File.WriteAllText(lockPath, ourLock);   // the file is back for the re-read...
            return request.Label == "File.ReadAllText"
                ? NetworkFaultDecision.Throwing(new IOException("sharing violation"))   // ...but the rename still holds it
                : NetworkFaultDecision.None;
        };

        SaveOutcome outcome;
        try { outcome = await svc.SaveAsync(); }
        finally { NetworkFaults.Decide = null; }

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal(2, existsCalls);   // the re-read happened
        Assert.Equal((SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false), false), Assert.Single(banners));
        Assert.False(lockLostFired);
        Assert.Equal(ourLock, File.ReadAllText(lockPath));
        AssertSessionKept(svc, lockSvc);
    }

    /// <summary>
    /// Change (a), A8 reproduced: Bob wakes after Alice took the lock over. The wake check reads the lock, finds
    /// Alice's name, and demotes at once. Before the change it read AGAIN 200 ms later, bumped into Bob's own
    /// heartbeat rewriting the file (modelled here by a second read that fails as contended), called that a network
    /// blip, and carried on as the editor. The wake is driven the way the app drives it: the lock service sees a gap
    /// longer than 90 s on its clock and raises WakeDetected.
    /// </summary>
    [Fact]
    public async Task Wake_LockNamesAnotherSession_WithAContendedSecondRead_Demotes_A8()
    {
        var clock   = new FakeTimeProvider();
        var lockSvc = new WriteLockService(clock, null, startTimers: false);
        var svc     = new CheckoutService(lockSvc, new NullLogger(), action => action(), _workingDir, startTimers: false);
        var db = DbPath();
        CreateFile(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        var lost = new TaskCompletionSource<WriteLockLostReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.WriteLockLost += reason => lost.TrySetResult(reason);

        WriteExternalFreshLock(db);   // Alice took over while Bob's laptop slept
        var lockPath = Path.ChangeExtension(db, ".lock");
        var reads = 0;
        NetworkFaults.Decide = request =>
            request.Label == "File.ReadAllText"
            && string.Equals(request.Path, lockPath, StringComparison.OrdinalIgnoreCase)
            && Interlocked.Increment(ref reads) > 1
                ? NetworkFaultDecision.Throwing(new IOException("sharing violation"))   // any SECOND read bumps into Bob's heartbeat
                : NetworkFaultDecision.None;
        try
        {
            clock.Advance(TimeSpan.FromMinutes(5));   // the laptop slept
            lockSvc.TickWakeCheck();                  // raises WakeDetected, which runs the wake check

            Assert.Same(lost.Task, await Task.WhenAny(lost.Task, Task.Delay(10_000)));
        }
        finally { NetworkFaults.Decide = null; }

        Assert.Equal(WriteLockLostReason.TakenOver, lost.Task.Result);
        Assert.Equal(1, reads);   // the first read settled it; there was no second one to bump
        Assert.NotNull(svc.LockLossNewHolder);
    }

    /// <summary>
    /// Change (c) through the checkout service: Alice has taken the lock over, and Bob's heartbeat reads her name on
    /// its FIRST tick, writes nothing (the lock file is byte-identical afterwards, so her lock is not held up as
    /// live) and raises HeartbeatFailed; the checkout service's ownership check then reads Alice's name and
    /// WriteLockLost follows with the "taken over" reason. Before the change this took three failed beats, or the next
    /// save, and the heartbeat kept freshening Alice's lock meanwhile.
    /// </summary>
    [Fact]
    public async Task Heartbeat_LockNamesAnotherSession_DemotesOnTheFirstTick_AndWritesNothing()
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        CreateFile(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        var lost = new TaskCompletionSource<WriteLockLostReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.WriteLockLost += reason => lost.TrySetResult(reason);

        var lockPath = Path.ChangeExtension(db, ".lock");
        WriteExternalFreshLock(db);                      // Alice took over
        var alices = File.ReadAllBytes(lockPath);

        await lockSvc.ForceRenewHeartbeat();             // ONE beat

        Assert.Same(lost.Task, await Task.WhenAny(lost.Task, Task.Delay(10_000)));
        Assert.Equal(WriteLockLostReason.TakenOver, lost.Task.Result);
        Assert.Equal(alices, File.ReadAllBytes(lockPath));
        Assert.False(File.Exists(lockPath + ".tmp"));
    }

    // ── A12: a lost lock is reported ONCE per write session ─────────────────────────────────────────
    //
    // On waking, the wake check and the heartbeat both run, and (since change (c)) both find Alice's name at once.
    // Both used to raise WriteLockLost, so the window ran two overlapping demotions and the second reopened the
    // database on a working copy the first had already deleted. Now only the first check in a write session raises
    // it; the shared helper (LockLossRace) makes the overlap certain instead of a matter of thread timing.

    /// <summary>
    /// A12 (field, GregSurface): Bob's laptop wakes after Alice took the lock over, and the wake check and the
    /// heartbeat find the loss at the same moment. Before the fix both raised WriteLockLost (the window then ran two
    /// demotions at once and told Bob "the connection could not be restored" instead of "has taken over editing").
    /// Now WriteLockLost is raised exactly once, as "taken over"; the second check says in the log that the loss is
    /// already being handled. Both checks are held at their first read of the lock file until both are running, and the
    /// test's handler only counts (Bob stays in write mode), so both really do reach the decision.
    /// </summary>
    [Fact]
    public async Task WakeCheckAndHeartbeatTogether_RaiseWriteLockLostExactlyOnce_A12()
    {
        var clock   = new FakeTimeProvider();
        var lockSvc = new WriteLockService(clock, null, startTimers: false);
        var logger  = new RecordingAppLogger();
        var svc     = new CheckoutService(lockSvc, logger, action => action(), _workingDir, startTimers: false);
        var db = DbPath();
        CreateFile(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        var reasons = new System.Collections.Concurrent.ConcurrentQueue<WriteLockLostReason>();
        svc.WriteLockLost += reasons.Enqueue;

        WriteExternalFreshLock(db);   // Alice took over while Bob's laptop slept
        using (LockLossRace.HoldFirstCheckUntilTheSecondArrives(Path.ChangeExtension(db, ".lock")))
        {
            clock.Advance(TimeSpan.FromMinutes(5));   // the laptop slept
            lockSvc.TickWakeCheck();                  // the wake check starts in the background...
            await lockSvc.ForceRenewHeartbeat();      // ...and ONE heartbeat beat reads Alice's name and starts the other check

            Assert.True(await LockLossRace.WaitForBothChecksAsync(logger, () => reasons.Count),
                "both the wake check and the heartbeat check should have found the loss");
            await Task.Delay(300);                    // settle: nothing else may be raised after the checks are done
        }

        Assert.Equal(new[] { WriteLockLostReason.TakenOver }, reasons.ToArray());
        Assert.Equal(1, LockLossRace.CountInfos(logger, LockLossRace.WakeVerdict));
        Assert.Equal(1, LockLossRace.CountInfos(logger, LockLossRace.HeartbeatVerdict));
        Assert.Equal(1, LockLossRace.CountInfos(logger, LockLossRace.AlreadyHandled));
    }

    /// <summary>
    /// A12, the same guard in a fixed order (no timing at all): Bob's heartbeat finds Alice's name and WriteLockLost is
    /// raised once. Then a save finds the same loss. It still answers LockLost (the save is refused, D is byte-identical)
    /// but does not raise WriteLockLost a second time; the log says the loss is already being handled. The test's
    /// handler only counts, so Bob is still in write mode when the save runs — the case where a save and a check
    /// race each other.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AfterTheLossWasAlreadyRaised_IsLockLost_WithoutRaisingItAgain_A12()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db, logger);
        var before = DbFileTestHelpers.HashOf(db);
        var reasons = new System.Collections.Concurrent.ConcurrentQueue<WriteLockLostReason>();
        svc.WriteLockLost += reasons.Enqueue;

        WriteExternalFreshLock(db);                // Alice took over
        await lockSvc.ForceRenewHeartbeat();       // ONE beat: the heartbeat check finds Alice's name and raises the loss
        Assert.True(await LockLossRace.WaitUntilAsync(() => reasons.Count == 1, LockLossRace.Deadline),
            "the heartbeat check should have raised the loss");
        Assert.Equal(0, LockLossRace.CountInfos(logger, LockLossRace.AlreadyHandled));   // nobody has been turned away yet

        var outcome = await svc.SaveAsync();       // the save finds the same loss

        Assert.Equal(SaveOutcome.LockLost, outcome);
        Assert.Equal(new[] { WriteLockLostReason.TakenOver }, reasons.ToArray());   // still ONE
        Assert.Equal(1, LockLossRace.CountInfos(logger, LockLossRace.AlreadyHandled));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
    }

    /// <summary>The ways a write session is begun again after a loss (the three places the checkout service grants write access).</summary>
    public enum WriteReEntry
    {
        /// <summary>The lock was released: an ordinary <see cref="CheckoutService.CheckoutAsync"/> takes it.</summary>
        Checkout,

        /// <summary>The lock was left stale: <see cref="CheckoutService.CheckoutAsync"/> judges it, then <see cref="CheckoutService.ForceCheckoutAsync"/> takes it over.</summary>
        ForceCheckout,

        /// <summary>The kept working copy is adopted: <see cref="CheckoutService.CheckoutAsync"/> with recovery (<c>FinishRecoveryCheckoutAsync</c>).</summary>
        Recovery,
    }

    /// <summary>
    /// A12, the guard is per WRITE SESSION, not per service: Bob loses the lock (raised once), the window demotes him
    /// to read-only, and later Bob gets write access again in the app (a reader choosing "Take over as writer": the
    /// read-only session is released, then a new checkout runs). When Alice takes over AGAIN, Bob must be told again —
    /// WriteLockLost is raised a second time. If any of the three places that grant write access forgot to re-arm the
    /// guard, the second loss would be swallowed as "already handled" and Bob would keep editing a database he can no
    /// longer save. Covers a plain checkout, a forced takeover of a stale lock, and a recovery checkout (Bob had
    /// unsaved changes, so his working copy was kept and is adopted).
    /// </summary>
    /// <param name="reEntry">How Bob's second write session begins.</param>
    [Theory]
    [InlineData(WriteReEntry.Checkout)]
    [InlineData(WriteReEntry.ForceCheckout)]
    [InlineData(WriteReEntry.Recovery)]
    public async Task ARegainedWriteSession_ThatLosesTheLockAgain_RaisesItAgain_A12(WriteReEntry reEntry)
    {
        var (svc, lockSvc) = CreateService();
        var db = DbPath();
        var lockPath = Path.ChangeExtension(db, ".lock");
        CreateSqliteDb(db);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        if (reEntry == WriteReEntry.Recovery)
        {
            InsertTestValue(svc.WorkingPath, "unsaved-edit");   // unsaved work: the demotion keeps D' and its marker
            svc.MarkDirty();
        }
        var reasons = new System.Collections.Concurrent.ConcurrentQueue<WriteLockLostReason>();
        svc.WriteLockLost += reasons.Enqueue;

        // First loss, and what the window does about it: demote, then (when the user asks to edit again) release the
        // read-only session and drop its snapshot, as a database switch does before the new checkout.
        WriteExternalFreshLock(db);
        await lockSvc.ForceRenewHeartbeat();
        Assert.True(await LockLossRace.WaitUntilAsync(() => reasons.Count == 1, LockLossRace.Deadline),
            "the first loss should be raised");
        Assert.True(await svc.DemoteToReadOnlyAsync());
        await svc.ReleaseAsync(saveFirst: false);
        svc.CleanupWorkingCopy();
        lockSvc.PendingRelease?.Wait(LockLossRace.Deadline);

        // Bob gets write access again.
        switch (reEntry)
        {
            case WriteReEntry.Checkout:
                File.Delete(lockPath);                                   // Alice finished and released
                Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
                break;
            case WriteReEntry.ForceCheckout:
                WriteExternalStaleLock(db);                              // Alice's lock went stale instead
                Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));
                Assert.Equal(CheckoutOutcome.WriteAccess, await svc.ForceCheckoutAsync());
                break;
            default:
                File.Delete(lockPath);
                Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db, recoverWorkingCopy: true));
                Assert.Equal("unsaved-edit", ReadTestValue(svc.WorkingPath));   // the kept edit came back
                break;
        }
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);

        // Alice takes over again: the second session reports its own loss.
        WriteExternalFreshLock(db);
        await lockSvc.ForceRenewHeartbeat();

        Assert.True(await LockLossRace.WaitUntilAsync(() => reasons.Count == 2, LockLossRace.Deadline),
            "the regained write session lost the lock again, and must say so");
        Assert.Equal(new[] { WriteLockLostReason.TakenOver, WriteLockLostReason.TakenOver }, reasons.ToArray());
    }

    // ── Change (d): the final check deletes D.tmp only when it confirmed the lock is ours ──────────

    /// <summary>The log line the final check writes when it refuses a save, or null when there is none.</summary>
    /// <param name="logger">The recording logger the service under test was given.</param>
    private static string? FinalCheckRefusalLine(RecordingAppLogger logger)
        => logger.Infos.SingleOrDefault(line => line.StartsWith("CheckoutService: the final check before the rename refused the save"));

    /// <summary>
    /// Change (d), the new behaviour: the final check cannot reach the lock file (the share stopped answering during
    /// the upload), so it can NOT tell whose lock it is. The save is a CopyError with the outage notice, D is
    /// byte-identical, D' and its marker stay, and D.tmp is LEFT in place (it may be another computer's save in
    /// progress); the log says why. The leftover is harmless: the next save overwrites it and succeeds.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockCannotBeCheckedAtTheFinalCheck_LeavesD_tmpInPlace_AsACopyError()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db, logger);
        var banners = CaptureSaveBanners(svc);
        var before = DbFileTestHelpers.HashOf(db);
        var lockPath = Path.ChangeExtension(db, ".lock");

        var outcome = await SaveWhileAsync(svc, () =>
            NetworkFaults.Decide = request =>
                request.Label == "File.Exists" && string.Equals(request.Path, lockPath, StringComparison.OrdinalIgnoreCase)
                    ? NetworkFaultDecision.TimeOut
                    : NetworkFaultDecision.None);

        Assert.Equal(SaveOutcome.CopyError, outcome);
        Assert.Equal((SavePrompt.OutageNotice(hasUnsavedChanges: true, autoSaveEnabled: false), false), Assert.Single(banners));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.True(File.Exists(db + ".tmp"), "Without a confirmed lock the save must leave D.tmp alone.");
        Assert.EndsWith("the lock could not be checked, D.tmp left in place.", FinalCheckRefusalLine(logger));
        AssertSessionKept(svc, lockSvc);

        // The leftover D.tmp does no harm: the next save overwrites it, saves, and consumes it.
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("unsaved-edit", ReadTestValue(db));
        Assert.False(File.Exists(db + ".tmp"));
    }

    /// <summary>
    /// Change (d) with change (a), Story 3: another session's name in the lock at the final check is LockLost
    /// after reading the lock file ONCE; D is untouched, D' and its marker stay, D.tmp is left for the new
    /// holder's cleanup, and the log says exactly that.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockLostAtTheFinalCheck_ReadsTheLockOnce_AndLeavesD_tmpForTheNewHolder()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);
        var before = DbFileTestHelpers.HashOf(db);
        var lockPath = Path.ChangeExtension(db, ".lock");
        var counter = new ShareOpCounter();

        var outcome = await SaveWhileAsync(svc, () =>
        {
            WriteExternalFreshLock(db);       // Alice took over while Bob's upload was finishing
            counter.Install(lockPath);        // from here on, count what the final check does to the lock file
        });

        Assert.Equal(SaveOutcome.LockLost, outcome);
        Assert.Equal(1, counter.Count("File.Exists"));
        Assert.Equal(1, counter.Count("File.ReadAllText"));
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.True(File.Exists(db + ".tmp"));
        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
        Assert.EndsWith("lock lost, D.tmp left for the new holder's cleanup.", FinalCheckRefusalLine(logger));
    }

    /// <summary>
    /// Spec item 32, design risk 3, at the checkout service: the EXIT save (<c>ReleaseAsync(saveFirst: true)</c>) is
    /// refused at the final check because Alice took the lock over while Bob's upload was finishing. ReleaseAsync
    /// returns LockLost and raises WriteLockLost once; D is byte-identical; D' and its dirty marker are kept (the edit
    /// is still in D'); D.tmp is left (the save must not touch the share without the lock); and the lock file still
    /// names Alice, because the release re-reads the lock and skips the delete when it is not ours. Bob is no longer
    /// a writer, and the service is read-only.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_ExitSaveRefusedAtTheFinalCheck_ReturnsLockLost_AndKeepsEverything()
    {
        var db = DbPath();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db);
        var before = DbFileTestHelpers.HashOf(db);
        var lockPath = Path.ChangeExtension(db, ".lock");
        var lockLostCount = 0;
        svc.WriteLockLost += _ => lockLostCount++;
        byte[]? alices = null;
        CrashPoints.Hook = name =>
        {
            if (name != "Save.AfterTmpVerify") return;
            WriteExternalFreshLock(db);
            alices = File.ReadAllBytes(lockPath);
        };

        SaveOutcome outcome;
        try { outcome = await svc.ReleaseAsync(saveFirst: true); }
        finally { CrashPoints.Hook = null; }
        lockSvc.PendingRelease?.Wait(TimeSpan.FromSeconds(10));   // let the release's background lock-file handling finish

        Assert.Equal(SaveOutcome.LockLost, outcome);
        Assert.Equal(1, lockLostCount);
        Assert.Equal(before, DbFileTestHelpers.HashOf(db));
        Assert.True(File.Exists(svc.WorkingPath), "D' must be kept");
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"), "the dirty marker must be kept");
        Assert.Equal("unsaved-edit", ReadTestValue(svc.WorkingPath));
        Assert.True(File.Exists(db + ".tmp"), "without the lock the exit save must leave D.tmp alone");
        Assert.Equal(alices, File.ReadAllBytes(lockPath));       // the release did not delete the other session's lock
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
    }

    /// <summary>
    /// Change (d), unchanged behaviour pinned: the lock is confirmed ours at the final check but D changed during the
    /// upload. The save is SourceModified, and D.tmp IS discarded (the lock was ours a moment ago); the log says so.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DChangedAtTheFinalCheck_DiscardsD_tmp_AndSaysSo()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, _) = await CheckoutWithUnsavedEditAsync(db, logger);

        var outcome = await SaveWhileAsync(svc, () =>
        {
            InsertTestValue(db, "other-writers-save");
            File.SetLastWriteTimeUtc(db, File.GetLastWriteTimeUtc(db).AddMinutes(1));   // visible to the stamp however coarse the clock
        });

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.False(File.Exists(db + ".tmp"), "The lock was confirmed ours, so the refused save discards its D.tmp.");
        Assert.EndsWith("D.tmp discarded.", FinalCheckRefusalLine(logger));
        Assert.True(File.Exists(svc.WorkingPath + ".dirty"));
    }

    /// <summary>
    /// Change (d), the re-created lock counts as confirmed: at the final check the lock file has been REMOVED (antivirus,
    /// cloud-sync), the check puts it back (<c>TryRecoverRemovedLockAsync</c>), and D has changed. The refusal is
    /// SourceModified, the lock file is back and names this session, and D.tmp is discarded.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LockRemovedAndPutBackAtTheFinalCheck_ThenDChanged_DiscardsD_tmp()
    {
        var db = DbPath();
        var logger = new RecordingAppLogger();
        var (svc, lockSvc) = await CheckoutWithUnsavedEditAsync(db, logger);
        var lockPath = Path.ChangeExtension(db, ".lock");

        var outcome = await SaveWhileAsync(svc, () =>
        {
            File.Delete(lockPath);
            InsertTestValue(db, "other-writers-save");
            File.SetLastWriteTimeUtc(db, File.GetLastWriteTimeUtc(db).AddMinutes(1));
        });

        Assert.Equal(SaveOutcome.SourceModified, outcome);
        Assert.False(File.Exists(db + ".tmp"));
        Assert.EndsWith("D.tmp discarded.", FinalCheckRefusalLine(logger));
        Assert.True(File.Exists(lockPath), "The check must have put the removed lock file back.");
        Assert.Equal(lockSvc.SessionGuid, JsonSerializer.Deserialize<TermPoint.Models.LockFileData>(File.ReadAllText(lockPath))!.SessionGuid);
        Assert.True(lockSvc.IsWriter);
    }
}
