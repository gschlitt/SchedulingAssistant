using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="CheckoutService.RestoreIntoAsync"/> — the database chooser's restore into a database that
/// this app does not have open (spec item 5, step 5c). The routines underneath are covered by
/// <c>DatabaseRestoreTests</c>; what is proved HERE is the wrapper's contract:
/// <list type="bullet">
/// <item><description>a missing destination gets a new database (no lock involved), an existing one is replaced under
/// the write lock, and a destination that is the backup itself is refused before anything happens;</description></item>
/// <item><description>a lock held by anyone else — live or abandoned — is never touched: the restore is refused naming
/// the holder, the database is byte-identical and the other lock file is exactly as it was;</description></item>
/// <item><description>our own lock is ALWAYS released, and its file is gone by the time the call returns (the next open of
/// the same database must not meet it);</description></item>
/// <item><description>the session state of the service (source path, working copy, mode, checkout hash) is never touched,
/// because the chooser has no session.</description></item>
/// </list>
/// Real files in temp folders and real <see cref="WriteLockService"/> instances stand in for the share and for "another
/// user" (a second lock service holds the lock the way a second TermPoint would).
/// </summary>
public sealed class RestoreIntoTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>Stands in for the shared folder holding the database.</summary>
    private readonly string _shareDir;

    /// <summary>Holds the backup files.</summary>
    private readonly string _backupDir;

    /// <summary>The checkout service's working-copy folder (must stay empty: no session is ever created here).</summary>
    private readonly string _workingDir;

    /// <summary>The lock services created by a test, released and settled on disposal.</summary>
    private readonly List<WriteLockService> _locks = new();

    /// <summary>Creates isolated folders for this test instance.</summary>
    public RestoreIntoTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"ri_share_{id}");
        _backupDir  = Path.Combine(Path.GetTempPath(), $"ri_backup_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"ri_work_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_backupDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Releases every lock service, waits for lock files to go, and removes the folders (best effort).</summary>
    public void Dispose()
    {
        foreach (var lockService in _locks)
        {
            lockService.Release();
            lockService.PendingRelease?.Wait(TimeSpan.FromSeconds(10));
        }
        SqliteConnection.ClearAllPools();
        foreach (var dir in new[] { _shareDir, _backupDir, _workingDir })
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a lock service that is cleaned up with the test.</summary>
    private WriteLockService NewLockService()
    {
        var lockService = new WriteLockService();
        _locks.Add(lockService);
        return lockService;
    }

    /// <summary>Creates the service under test with its own lock service (and returns both).</summary>
    private (CheckoutService Svc, WriteLockService Lock) CreateService()
    {
        var lockService = NewLockService();
        var svc = new CheckoutService(
            lockService: lockService,
            logger: new SilentLogger(),
            dispatch: action => action(),
            workingDir: _workingDir);
        return (svc, lockService);
    }

    /// <summary>Path of the database inside the "share".</summary>
    private string TargetPath(string name = "dept.db") => Path.Combine(_shareDir, name);

    /// <summary>The lock file that belongs to <paramref name="db"/>.</summary>
    private static string LockPathOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>
    /// Creates a TermPoint-shaped SQLite file at <paramref name="path"/> (the three core tables) holding one row,
    /// <paramref name="marker"/>, so a test can tell which database a file is by content.
    /// </summary>
    private static void CreateMarkerDb(string path, string marker)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Concat(DatabaseRestore.RequiredTables.Select(t => $"CREATE TABLE {t} (key TEXT, data TEXT); "))
                          + "CREATE TABLE marker (val TEXT); INSERT INTO marker VALUES (@v);";
        cmd.Parameters.AddWithValue("@v", marker);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the marker row of the database at <paramref name="path"/> from a scratch copy (the file under test is never opened by SQLite).</summary>
    private string ReadMarker(string path)
    {
        var copy = Path.Combine(_backupDir, $"read_{Guid.NewGuid():N}.db");
        File.Copy(path, copy);
        try
        {
            using var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT val FROM marker";
            return (string)cmd.ExecuteScalar()!;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        }
    }

    /// <summary>Creates a backup file in the backup folder with the given marker.</summary>
    private string CreateBackup(string marker, string name = "dept_2026-10-13_13-00-00.db")
    {
        var path = Path.Combine(_backupDir, name);
        CreateMarkerDb(path, marker);
        return path;
    }

    /// <summary>Creates the destination database (marker "old-d") and returns its bytes.</summary>
    private byte[] CreateTarget(string? path = null)
    {
        var target = path ?? TargetPath();
        CreateMarkerDb(target, "old-d");
        return File.ReadAllBytes(target);
    }

    /// <summary>Everything currently in the share folder, sorted.</summary>
    private string[] ShareEntries() => Directory.GetFileSystemEntries(_shareDir).OrderBy(e => e, StringComparer.Ordinal).ToArray();

    /// <summary>The "before restore" files in the share folder.</summary>
    private string[] PreRestoreFiles() => Directory.GetFiles(_shareDir, "*(before restore*");

    /// <summary>
    /// Asserts the service's session state is exactly that of a service nothing was ever opened with — the
    /// chooser has no session, and a restore must not invent one.
    /// </summary>
    private static void AssertNoSession(CheckoutService svc)
    {
        Assert.Equal(string.Empty, svc.SourcePath);
        Assert.Equal(string.Empty, svc.WorkingPath);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.Null(svc.HashAtCheckout);
        Assert.Null(svc.CurrentHolder);
    }

    /// <summary>Asserts the working-copy folder is still empty (no D', D'' or marker was created).</summary>
    private void AssertNoWorkingFiles() => Assert.Empty(Directory.GetFileSystemEntries(_workingDir));

    /// <summary>
    /// Writes a lock file as another machine's TermPoint would, with the given heartbeat age, and returns its
    /// exact text so a test can prove it was not touched.
    /// </summary>
    private static string WriteForeignLock(string db, TimeSpan heartbeatAge)
    {
        var now  = DateTime.UtcNow;
        var data = new LockFileData
        {
            Username    = "other_user",
            Machine     = "OTHER-PC",
            SessionGuid = Guid.NewGuid().ToString(),
            Acquired    = now - heartbeatAge,
            Heartbeat   = now - heartbeatAge,
        };
        var json = JsonSerializer.Serialize(data);
        File.WriteAllText(LockPathOf(db), json);
        return json;
    }

    /// <summary>A logger that records nothing and writes nothing.</summary>
    private sealed class SilentLogger : IAppLogger
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

    // ── Missing destination: Created ──────────────────────────────────────────

    /// <summary>
    /// A destination that does not exist gets a new database holding the backup, on the create path: no lock file is
    /// ever written, nothing else is left on the share, and the service has no session afterwards.
    /// </summary>
    [Fact]
    public async Task RestoreInto_MissingTarget_CreatesTheDatabase_WithoutAnyLock()
    {
        var (svc, lockService) = CreateService();
        var backup = CreateBackup("from-backup");
        var target = TargetPath();

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.Created, result.Outcome);
        Assert.True(result.Succeeded);
        Assert.True(result.CreatePath);
        Assert.Equal("from-backup", ReadMarker(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.False(lockService.IsWriter);
        AssertNoSession(svc);
        AssertNoWorkingFiles();
    }

    /// <summary>A damaged backup into a missing destination creates nothing and says so on the create path.</summary>
    [Fact]
    public async Task RestoreInto_MissingTarget_DamagedBackup_CreatesNothing()
    {
        var (svc, _) = CreateService();
        var backup = Path.Combine(_backupDir, "dept_2026-10-13_13-00-00.db");
        File.WriteAllText(backup, "this is not a database");

        var result = await svc.RestoreIntoAsync(backup, TargetPath());

        Assert.Equal(RestoreOutcome.BackupDamaged, result.Outcome);
        Assert.True(result.CreatePath);
        Assert.Empty(ShareEntries());
        AssertNoSession(svc);
    }

    // ── Existing destination: Restored ────────────────────────────────────────

    /// <summary>
    /// An existing, unlocked destination is replaced: the new content is the backup's, the old database is kept
    /// beside it byte-for-byte, and — the point of the final wait — our lock file is already gone when the call
    /// returns, so the next open of this database (here: a second lock service, and the same service again) acquires
    /// the lock at once instead of meeting our just-released one.
    /// </summary>
    [Fact]
    public async Task RestoreInto_ExistingFreeTarget_ReplacesIt_KeepsTheOldOne_AndLeavesNoLockFile()
    {
        var (svc, lockService) = CreateService();
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.Restored, result.Outcome);
        Assert.False(result.CreatePath);
        Assert.Equal("from-backup", ReadMarker(target));
        Assert.Equal(result.RestoredHash, DbFileTestHelpers.HashOf(target));

        Assert.NotNull(result.PreRestoreCopyPath);
        Assert.Equal(_shareDir, Path.GetDirectoryName(result.PreRestoreCopyPath));
        Assert.Equal(oldBytes, File.ReadAllBytes(result.PreRestoreCopyPath!));
        Assert.Equal("old-d", ReadMarker(result.PreRestoreCopyPath!));

        // The lock was released and its file is gone NOW (no settling needed), with nothing else left behind.
        Assert.False(lockService.IsWriter);
        Assert.False(File.Exists(LockPathOf(target)));
        Assert.Equal(new[] { target, result.PreRestoreCopyPath! }.OrderBy(e => e, StringComparer.Ordinal), ShareEntries());

        // The next open can take the lock straight away — through another service, and through the same one.
        var next = NewLockService();
        next.TryAcquire(target);
        Assert.True(next.IsWriter);
        next.Release();
        next.PendingRelease?.Wait(TimeSpan.FromSeconds(10));

        lockService.TryAcquire(target);
        Assert.True(lockService.IsWriter);
        lockService.Release();
        lockService.PendingRelease?.Wait(TimeSpan.FromSeconds(10));

        AssertNoSession(svc);
        AssertNoWorkingFiles();
    }

    /// <summary>
    /// Replacing is only done when the caller allows it. A caller that showed "a new database will be created" asked
    /// no confirmation, so a database that has appeared since is refused (AlreadyExists), untouched, with no lock
    /// ever taken and nothing kept.
    /// </summary>
    [Fact]
    public async Task RestoreInto_ExistingTarget_ReplaceNotAllowed_IsAlreadyExists_AndTouchesNothing()
    {
        var (svc, lockService) = CreateService();
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");

        var result = await svc.RestoreIntoAsync(backup, target, allowReplace: false);

        Assert.Equal(RestoreOutcome.AlreadyExists, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.False(lockService.IsWriter);
        AssertNoSession(svc);
    }

    // ── Someone else holds the lock ───────────────────────────────────────────

    /// <summary>
    /// A live editor (a second lock service in this process stands in for a second TermPoint) blocks the restore:
    /// LockHeld naming them, the database byte-identical, no "before restore" file, their lock file still theirs —
    /// and the restore left no poll timer or lock of its own running.
    /// </summary>
    [Fact]
    public async Task RestoreInto_LockHeldByAnEditor_IsLockHeld_AndNothingIsTouched()
    {
        var (svc, lockService) = CreateService();
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");
        var editor = NewLockService();
        editor.TryAcquire(target);
        Assert.True(editor.IsWriter);
        var lockTextBefore = File.ReadAllText(LockPathOf(target));

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.LockHeld, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.NotNull(result.Holder);
        Assert.False(string.IsNullOrWhiteSpace(result.Holder!.Username));
        Assert.Equal(editor.SessionGuid, result.Holder.SessionGuid);

        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Equal(lockTextBefore, File.ReadAllText(LockPathOf(target)));
        Assert.Equal(new[] { target, LockPathOf(target) }.OrderBy(e => e, StringComparer.Ordinal), ShareEntries());

        Assert.False(lockService.IsWriter);
        Assert.False(lockService.IsPolling);
        AssertNoSession(svc);
        AssertNoWorkingFiles();

        // Their session is unharmed: the lock is still theirs.
        Assert.True(editor.IsWriter);
    }

    /// <summary>
    /// An ABANDONED lock (a heartbeat an hour old, from another computer) is not taken over by a restore either:
    /// LockHeld, naming the holder (the message then points to the normal open, which offers the take-over), and the
    /// lock file is byte-for-byte as it was.
    /// </summary>
    [Fact]
    public async Task RestoreInto_AbandonedLock_IsLockHeld_AndTheLockFileIsUntouched()
    {
        var (svc, _) = CreateService();
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");
        var lockJson = WriteForeignLock(target, TimeSpan.FromHours(1));

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.LockHeld, result.Outcome);
        Assert.Equal("other_user", result.Holder?.Username);
        Assert.Equal("OTHER-PC", result.Holder?.Machine);
        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Equal(lockJson, File.ReadAllText(LockPathOf(target)));
        Assert.Empty(PreRestoreFiles());
        AssertNoSession(svc);
    }

    /// <summary>
    /// A lock file that cannot even be created (here a directory sits where it would go — the same refusal a
    /// Controlled Folder Access block or a permission denial gives) is a Failed restore carrying the reason, and
    /// the database is untouched.
    /// </summary>
    [Fact]
    public async Task RestoreInto_LockFileCannotBeCreated_IsFailedWithTheReason()
    {
        var (svc, _) = CreateService();
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");
        Directory.CreateDirectory(LockPathOf(target));

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Equal(result.Detail, result.Error?.Message);
        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Empty(PreRestoreFiles());
        Assert.True(Directory.Exists(LockPathOf(target)));   // what was there is still there
    }

    // ── Failures after the lock is taken ──────────────────────────────────────

    /// <summary>
    /// The network is cut mid-restore (the DEBUG <c>copy</c> fault): Unreachable, the database byte-identical, no
    /// pre-restore copy or .tmp left, and OUR lock released — its file gone on return, and a fresh acquire succeeds.
    /// </summary>
    [Fact]
    public async Task RestoreInto_InjectedCopyFault_IsUnreachable_TargetUnchanged_AndTheLockIsReleased()
    {
        var (svc, lockService) = CreateService();
        svc.RestoreFaultPlan = RestoreFaultPlan.Parse("copy");
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.Unreachable, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.False(lockService.IsWriter);
        Assert.False(File.Exists(LockPathOf(target)));

        var next = NewLockService();
        next.TryAcquire(target);
        Assert.True(next.IsWriter);
        AssertNoSession(svc);
    }

    /// <summary>The final lock check runs against THIS target: a lock that is no longer ours at the swap (the <c>lock</c> fault) stops the restore, releases ours, and leaves the database alone.</summary>
    [Fact]
    public async Task RestoreInto_InjectedLockFault_IsLockLost_TargetUnchanged_AndTheLockIsReleased()
    {
        var (svc, lockService) = CreateService();
        svc.RestoreFaultPlan = RestoreFaultPlan.Parse("lock");
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = CreateBackup("from-backup");

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.LockLost, result.Outcome);
        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.False(lockService.IsWriter);
        Assert.False(File.Exists(LockPathOf(target)));
    }

    /// <summary>A damaged backup into an existing database: refused, database untouched, lock released.</summary>
    [Fact]
    public async Task RestoreInto_ExistingTarget_DamagedBackup_IsRefused_AndTheLockIsReleased()
    {
        var (svc, lockService) = CreateService();
        var target = TargetPath();
        var oldBytes = CreateTarget(target);
        var backup = Path.Combine(_backupDir, "dept_2026-10-13_13-00-00.db");
        File.WriteAllText(backup, "this is not a database");

        var result = await svc.RestoreIntoAsync(backup, target);

        Assert.Equal(RestoreOutcome.BackupDamaged, result.Outcome);
        Assert.False(result.CreatePath);
        Assert.Equal(oldBytes, File.ReadAllBytes(target));
        Assert.Equal(new[] { target }, ShareEntries());
        Assert.False(lockService.IsWriter);
        Assert.False(File.Exists(LockPathOf(target)));
    }

    // ── Refusals before anything happens ──────────────────────────────────────

    /// <summary>
    /// Restoring a file onto itself is refused up front: Failed with the explanation, the file untouched, and
    /// neither a lock file nor a copy is made.
    /// </summary>
    [Fact]
    public async Task RestoreInto_TargetIsTheBackupItself_IsRefused_AndTouchesNothing()
    {
        var (svc, lockService) = CreateService();
        var backup = CreateBackup("the-only-copy");
        var bytes = File.ReadAllBytes(backup);

        var result = await svc.RestoreIntoAsync(backup, backup.ToUpperInvariant().Replace(".DB", ".db"));

        Assert.Equal(RestoreOutcome.Failed, result.Outcome);
        Assert.Equal(RestorePrompt.DestinationIsBackupFileText, result.Detail);
        Assert.NotNull(result.Error);
        Assert.Equal(bytes, File.ReadAllBytes(backup));
        Assert.Single(Directory.GetFileSystemEntries(_backupDir));
        Assert.False(lockService.IsWriter);
        AssertNoSession(svc);
    }

    /// <summary>
    /// A service that holds a write lock (it has a database open) refuses: Busy, and its session — lock, mode, source,
    /// checkout hash — is exactly as it was. The other database is not touched.
    /// </summary>
    [Fact]
    public async Task RestoreInto_WhileAWriteSessionIsOpen_IsBusy_AndTheSessionIsUntouched()
    {
        var (svc, lockService) = CreateService();
        var open = TargetPath("open.db");
        CreateTarget(open);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(open));
        var sourceBefore = svc.SourcePath;
        var workingBefore = svc.WorkingPath;
        var hashBefore = svc.HashAtCheckout;

        var other = TargetPath("other.db");
        var otherBytes = CreateTarget(other);
        var backup = CreateBackup("from-backup");

        var result = await svc.RestoreIntoAsync(backup, other);

        Assert.Equal(RestoreOutcome.Busy, result.Outcome);
        Assert.True(result.TargetUnchanged);
        Assert.Equal(otherBytes, File.ReadAllBytes(other));
        Assert.False(File.Exists(LockPathOf(other)));

        Assert.True(lockService.IsWriter);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Equal(sourceBefore, svc.SourcePath);
        Assert.Equal(workingBefore, svc.WorkingPath);
        Assert.Equal(hashBefore, svc.HashAtCheckout);
        Assert.True(File.Exists(LockPathOf(open)));   // the open session's own lock is intact
    }

    /// <summary>
    /// The call never throws and never strands the service: with an unusable backup path it comes back as a
    /// failure, and a restore right afterwards works.
    /// </summary>
    [Fact]
    public async Task RestoreInto_NeverThrows_AndTheServiceIsUsableAfterwards()
    {
        var (svc, _) = CreateService();
        var target = TargetPath();

        var failed = await svc.RestoreIntoAsync("", target);
        var good = await svc.RestoreIntoAsync(CreateBackup("from-backup"), target);

        Assert.False(failed.Succeeded);
        Assert.Equal(RestoreOutcome.Created, good.Outcome);
        Assert.Equal("from-backup", ReadMarker(target));
    }
}
