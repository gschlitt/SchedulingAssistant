using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for what <see cref="CheckoutService"/> does when the open-time integrity check of the local copy fails
/// (spec item 5, step 5d): <see cref="CheckoutService.RefreshLocalCopyAfterFailedCheckAsync"/> (ONE fresh,
/// hash-verified copy; the writer keeps its lock the whole time; D is never touched; a copy holding unsaved work is
/// never overwritten; a reader's snapshot is re-made) and <see cref="CheckoutService.SetAsideFailedLocalCopy"/> (the bad
/// file is renamed and kept, never deleted).
///
/// <para>Each test uses isolated temporary folders for D and for the working copies, a private
/// <see cref="WriteLockService"/> and a synchronous dispatcher, like <see cref="CheckoutFailedOpenTests"/>. Databases
/// are real multi-page SQLite files, so "damaged" means a really trashed page.</para>
/// </summary>
public sealed class CheckoutFreshLocalCopyTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _workingDir;

    /// <summary>How long to wait for the lock service's background lock-file deletion to settle.</summary>
    private static readonly TimeSpan LockSettleTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Creates isolated temporary directories for source databases and working copies.</summary>
    public CheckoutFreshLocalCopyTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"cfl_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"cfl_wrk_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Deletes both temporary directories (best effort).</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir,    recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a service with every dependency isolated.</summary>
    private (CheckoutService svc, WriteLockService lockSvc) CreateService()
    {
        var lockSvc = new WriteLockService();
        var svc = new CheckoutService(
            lockService: lockSvc,
            logger:      new NullLogger(),
            dispatch:    action => action(),
            workingDir:  _workingDir);
        return (svc, lockSvc);
    }

    /// <summary>Creates a healthy multi-page database named <paramref name="name"/> in the source folder and returns its path.</summary>
    private string CreateHealthyDb(string name = "dept")
    {
        var path = Path.Combine(_tempDir, $"{name}.db");
        DbFileTestHelpers.CreateHealthyDatabase(path);
        return path;
    }

    /// <summary>Writes one verifiable value into a <c>_test</c> table of the database at <paramref name="dbPath"/>.</summary>
    private static void InsertTestValue(string dbPath, string value)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the latest value written by <see cref="InsertTestValue"/>, or null.</summary>
    private static string? ReadTestValue(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Path of the lock file the write lock creates next to <paramref name="db"/>.</summary>
    private static string LockFileOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>The session GUID currently written in the lock file beside <paramref name="db"/>.</summary>
    private static string? LockSessionOf(string db)
        => JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockFileOf(db)))?.SessionGuid;

    /// <summary>Writes a lock file with a fresh heartbeat next to <paramref name="db"/>, as a live remote writer would.</summary>
    private static void WriteExternalFreshLock(string db)
    {
        var now = DateTime.UtcNow;
        File.WriteAllText(LockFileOf(db), JsonSerializer.Serialize(new LockFileData
        {
            Username = "other_user", Machine = "OTHER-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = now, Heartbeat = now,
        }));
    }

    /// <summary>Ends a session cleanly and lets the lock service's background lock-file deletion settle.</summary>
    private static async Task FinishAsync(CheckoutService svc, WriteLockService lockSvc)
    {
        await svc.ReleaseAsync(saveFirst: false);
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);
    }

    /// <summary>No-op logger.</summary>
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
    // The writer's fresh copy
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// THE HAPPY PATH. A writer's D' is damaged but D is fine: the fresh copy replaces D' with a hash-verified copy
    /// of D that passes its integrity check; the write lock is still ours and its file is the very same one; D is
    /// byte-identical to before; the baseline hash is D's; and no temporary file is left behind.
    /// </summary>
    [Fact]
    public async Task Writer_DamagedCopy_IsReplacedByAVerifiedCopy_WhileKeepingTheLock_AndLeavingDUntouched()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var session     = LockSessionOf(db);
        var workingPath = svc.WorkingPath;
        var dHash       = DbFileTestHelpers.HashOf(db);
        DbFileTestHelpers.TrashPage(workingPath, 2);
        Assert.Equal(IntegrityCheckResult.Damaged, BackupService.CheckIntegrityDetailed(workingPath));

        var result = await svc.RefreshLocalCopyAfterFailedCheckAsync();

        Assert.Equal(LocalCopyRefreshResult.Ok, result);
        // The lock never changed hands: still the writer, the same session in the same lock file.
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(CheckoutMode.WriteAccess, svc.Mode);
        Assert.Equal(session, LockSessionOf(db));
        // D was only read.
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        // D' is a verified copy of D, at the same path, and the baseline says so.
        Assert.Equal(workingPath, svc.WorkingPath);
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(workingPath));
        Assert.Equal(dHash, svc.HashAtCheckout);
        Assert.Equal(IntegrityCheckResult.Ok, BackupService.CheckIntegrityDetailed(workingPath));
        Assert.False(File.Exists(workingPath + CheckoutService.FreshCopySuffix), "the temporary file must not be left behind");

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>
    /// The baseline is D's CURRENT hash, not the one from the checkout: if D has changed since the checkout, the
    /// fresh copy contains the change, <c>HashAtCheckout</c> follows it, and the next save does not refuse
    /// ("someone else changed the database") — it succeeds.
    /// </summary>
    [Fact]
    public async Task Writer_FreshCopy_UpdatesTheBaselineHash_SoTheNextSaveSucceeds()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var staleBaseline = svc.HashAtCheckout;
        InsertTestValue(db, "added-to-D-meanwhile");
        var newHash = DbFileTestHelpers.HashOf(db);
        Assert.NotEqual(staleBaseline, newHash);
        DbFileTestHelpers.TrashPage(svc.WorkingPath, 2);

        Assert.Equal(LocalCopyRefreshResult.Ok, await svc.RefreshLocalCopyAfterFailedCheckAsync());

        Assert.Equal(newHash, svc.HashAtCheckout);
        Assert.Equal("added-to-D-meanwhile", ReadTestValue(svc.WorkingPath));

        // An edit and a save work normally: D is what the baseline says it is.
        InsertTestValue(svc.WorkingPath, "edit-after-refresh");
        svc.MarkDirty();
        Assert.Equal(SaveOutcome.Success, await svc.SaveAsync());
        Assert.Equal("edit-after-refresh", ReadTestValue(db));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>
    /// A writer's copy that holds unsaved work (a dirty marker) is NEVER overwritten: the refresh refuses, and the
    /// file, the marker, the baseline and the lock are exactly as they were.
    /// </summary>
    [Fact]
    public async Task Writer_CopyWithUnsavedWork_IsRefused_AndNothingChanges()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        InsertTestValue(svc.WorkingPath, "unsaved-edit");
        svc.MarkDirty();
        Assert.True(svc.HasDirtyMarker, "precondition: the copy holds unsaved work");
        var workingPath = svc.WorkingPath;
        var copyHash    = DbFileTestHelpers.HashOf(workingPath);
        var baseline    = svc.HashAtCheckout;
        var session     = LockSessionOf(db);

        var result = await svc.RefreshLocalCopyAfterFailedCheckAsync();

        Assert.Equal(LocalCopyRefreshResult.HoldsUnsavedChanges, result);
        Assert.Equal(copyHash, DbFileTestHelpers.HashOf(workingPath));
        Assert.Equal("unsaved-edit", ReadTestValue(workingPath));
        Assert.True(File.Exists(workingPath + ".dirty"));
        Assert.Equal(baseline, svc.HashAtCheckout);
        Assert.True(lockSvc.IsWriter);
        Assert.Equal(session, LockSessionOf(db));
        Assert.False(File.Exists(workingPath + CheckoutService.FreshCopySuffix));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>
    /// The flow the handler runs for a damaged copy that held recovered changes: the changes are set aside first
    /// (kept, not deleted), which removes the marker; the refresh then succeeds. The archived file is still there.
    /// </summary>
    [Fact]
    public async Task Writer_RecoveredChangesAreArchivedFirst_ThenTheRefreshSucceeds_AndTheArchiveSurvives()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        InsertTestValue(svc.WorkingPath, "unsaved-edit");
        svc.MarkDirty();
        var workingPath = svc.WorkingPath;
        var copyHash    = DbFileTestHelpers.HashOf(workingPath);

        var archive = svc.ArchiveCorruptWorkingCopy(db);
        Assert.NotNull(archive);
        Assert.False(svc.HasDirtyMarker);

        Assert.Equal(LocalCopyRefreshResult.Ok, await svc.RefreshLocalCopyAfterFailedCheckAsync());

        Assert.True(File.Exists(archive!), "the archived recovered changes must survive");
        Assert.Equal(copyHash, DbFileTestHelpers.HashOf(archive!));
        Assert.Equal(DbFileTestHelpers.HashOf(db), DbFileTestHelpers.HashOf(workingPath));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>A stale <c>-wal</c> / <c>-shm</c> pair beside the damaged copy must not survive next to the new file (a corruption hazard on the next open).</summary>
    [Fact]
    public async Task Writer_FreshCopy_RemovesStaleWalSidecars()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        File.WriteAllText(svc.WorkingPath + "-wal", "stale wal");
        File.WriteAllText(svc.WorkingPath + "-shm", "stale shm");
        DbFileTestHelpers.TrashPage(svc.WorkingPath, 2);

        Assert.Equal(LocalCopyRefreshResult.Ok, await svc.RefreshLocalCopyAfterFailedCheckAsync());

        Assert.False(File.Exists(svc.WorkingPath + "-wal"));
        Assert.False(File.Exists(svc.WorkingPath + "-shm"));
        Assert.Equal(IntegrityCheckResult.Ok, BackupService.CheckIntegrityDetailed(svc.WorkingPath));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>
    /// If D itself cannot be read (another program holds it exclusively), nothing is changed: the result is
    /// Unreadable, the damaged copy is untouched, and the lock is still ours.
    /// </summary>
    [FactWindowsOnly]
    public async Task Writer_SharedDatabaseHeldByAnotherProgram_IsUnreadable_AndNothingChanges()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        DbFileTestHelpers.TrashPage(svc.WorkingPath, 2);
        var copyHash = DbFileTestHelpers.HashOf(svc.WorkingPath);

        LocalCopyRefreshResult result;
        using (DbFileTestHelpers.LockExclusively(db))
            result = await svc.RefreshLocalCopyAfterFailedCheckAsync();

        Assert.Equal(LocalCopyRefreshResult.Unreadable, result);
        Assert.Equal(copyHash, DbFileTestHelpers.HashOf(svc.WorkingPath));
        Assert.True(lockSvc.IsWriter);
        Assert.False(File.Exists(svc.WorkingPath + CheckoutService.FreshCopySuffix));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>
    /// If the damaged copy itself cannot be replaced (antivirus holds it), the result is not Ok, the temporary file
    /// is cleaned up, D is untouched and the lock is still ours.
    /// </summary>
    [FactWindowsOnly]
    public async Task Writer_CopyHeldByAnotherProgram_CannotBeReplaced_AndNothingIsLeftBehind()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var workingPath = svc.WorkingPath;
        DbFileTestHelpers.TrashPage(workingPath, 2);
        var dHash = DbFileTestHelpers.HashOf(db);

        LocalCopyRefreshResult result;
        using (DbFileTestHelpers.LockExclusively(workingPath))
            result = await svc.RefreshLocalCopyAfterFailedCheckAsync();

        Assert.NotEqual(LocalCopyRefreshResult.Ok, result);
        Assert.False(File.Exists(workingPath + CheckoutService.FreshCopySuffix));
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        Assert.True(lockSvc.IsWriter);

        await FinishAsync(svc, lockSvc);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The reader's fresh snapshot
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A reader's D'' is re-made: a damaged snapshot becomes a verified copy of D that passes its check. The session
    /// stays read-only, on the same path, holds no lock and D (and the other writer's lock file) are untouched.
    /// </summary>
    [Fact]
    public async Task Reader_DamagedSnapshot_IsRemade()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        WriteExternalFreshLock(db);   // someone else is editing: this session opens read-only on D''
        var otherSession = LockSessionOf(db);
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));
        var snapshot = svc.WorkingPath;
        Assert.Contains("_ro", snapshot);
        var dHash = DbFileTestHelpers.HashOf(db);
        DbFileTestHelpers.TrashPage(snapshot, 2);

        var result = await svc.RefreshLocalCopyAfterFailedCheckAsync();

        Assert.Equal(LocalCopyRefreshResult.Ok, result);
        Assert.Equal(CheckoutMode.ReadOnly, svc.Mode);
        Assert.Equal(snapshot, svc.WorkingPath);
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(snapshot));
        Assert.Equal(dHash, svc.HashAtCheckout);
        Assert.Equal(IntegrityCheckResult.Ok, BackupService.CheckIntegrityDetailed(snapshot));
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(otherSession, LockSessionOf(db));   // the other writer's lock was not touched

        await FinishAsync(svc, lockSvc);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Nothing to refresh
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>With no database open there is nothing to refresh: Failed, and nothing is created.</summary>
    [Fact]
    public async Task NothingOpen_IsFailed()
    {
        var (svc, _) = CreateService();

        Assert.Equal(LocalCopyRefreshResult.Failed, await svc.RefreshLocalCopyAfterFailedCheckAsync());
        Assert.Empty(Directory.GetFileSystemEntries(_workingDir));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Setting a failed copy aside
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A failed copy is RENAMED to <c>.corrupt-…</c> beside where it was, with its bytes intact — not deleted — and
    /// it survives the reset that ends the session, which deletes a copy that has no marker and forgets the path.
    /// </summary>
    [Fact]
    public async Task SetAside_Writer_RenamesTheCopy_KeepsItsBytes_AndSurvivesTheReset()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var workingPath = svc.WorkingPath;
        DbFileTestHelpers.TrashPage(workingPath, 2);
        var badHash = DbFileTestHelpers.HashOf(workingPath);

        var archive = svc.SetAsideFailedLocalCopy();
        await svc.ResetAfterFailedOpenAsync();
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        Assert.NotNull(archive);
        Assert.Contains(".corrupt-", archive);
        Assert.Equal(Path.GetDirectoryName(workingPath), Path.GetDirectoryName(archive));
        Assert.False(File.Exists(workingPath), "the original name is free again");
        Assert.True(File.Exists(archive!), "the bad file is kept for IT, never deleted");
        Assert.Equal(badHash, DbFileTestHelpers.HashOf(archive!));
        Assert.Equal(string.Empty, svc.WorkingPath);
    }

    /// <summary>A reader's failed D'' is set aside the same way.</summary>
    [Fact]
    public async Task SetAside_Reader_RenamesTheSnapshot_AndKeepsIt()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        WriteExternalFreshLock(db);
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));
        var snapshot = svc.WorkingPath;
        DbFileTestHelpers.TrashPage(snapshot, 2);
        var badHash = DbFileTestHelpers.HashOf(snapshot);

        var archive = svc.SetAsideFailedLocalCopy();

        Assert.NotNull(archive);
        Assert.False(File.Exists(snapshot));
        Assert.Equal(badHash, DbFileTestHelpers.HashOf(archive!));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>With no local copy there is nothing to set aside, and nothing happens.</summary>
    [Fact]
    public void SetAside_WithNoCopy_ReturnsNull()
    {
        var (svc, _) = CreateService();

        Assert.Null(svc.SetAsideFailedLocalCopy());
        Assert.Empty(Directory.GetFileSystemEntries(_workingDir));
    }

    /// <summary>
    /// If the rename fails (the file is held by another program), the file is left exactly where it is and null is
    /// returned: setting a copy aside never deletes it.
    /// </summary>
    [FactWindowsOnly]
    public async Task SetAside_WhenTheRenameFails_LeavesTheFileInPlace()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var workingPath = svc.WorkingPath;
        var hash = DbFileTestHelpers.HashOf(workingPath);

        string? archive;
        using (DbFileTestHelpers.LockExclusively(workingPath))
            archive = svc.SetAsideFailedLocalCopy();

        Assert.Null(archive);
        Assert.True(File.Exists(workingPath));
        Assert.Equal(hash, DbFileTestHelpers.HashOf(workingPath));

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>
    /// Setting aside again, even within the same second, never overwrites an earlier archive (which may hold a
    /// user's unsaved work): every archive keeps its own name and its own bytes.
    /// </summary>
    [Fact]
    public async Task SetAside_Repeatedly_NeverOverwritesAnEarlierArchive()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var workingPath = svc.WorkingPath;

        var archives = new List<(string Path, string Hash)>();
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(workingPath, $"generation {i}");   // a new bad copy each time
            var hash = DbFileTestHelpers.HashOf(workingPath);
            var archive = svc.SetAsideFailedLocalCopy();
            Assert.NotNull(archive);
            archives.Add((archive!, hash));
        }

        Assert.Equal(3, archives.Select(a => a.Path).Distinct().Count());
        foreach (var (path, hash) in archives)
        {
            Assert.True(File.Exists(path));
            Assert.Equal(hash, DbFileTestHelpers.HashOf(path));
        }

        await FinishAsync(svc, lockSvc);
    }

    /// <summary>The existing crash-recovery archive keeps working through the shared implementation, and also no longer overwrites a same-second archive.</summary>
    [Fact]
    public async Task ArchiveCorruptWorkingCopy_StillArchivesAndClearsTheMarker()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        InsertTestValue(svc.WorkingPath, "unsaved");
        svc.MarkDirty();
        var workingPath = svc.WorkingPath;

        var archive = svc.ArchiveCorruptWorkingCopy(db);

        Assert.NotNull(archive);
        Assert.True(File.Exists(archive!));
        Assert.False(File.Exists(workingPath));
        Assert.False(File.Exists(workingPath + ".dirty"));

        await FinishAsync(svc, lockSvc);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Giving up: the session ends WITHOUT saving
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The give-up sequence of <c>MainWindow</c> for a copy that keeps failing — set it aside, then
    /// <c>ResetAfterFailedOpenAsync</c> — followed by what <c>MainWindow.OnClosing</c> then does (no save because the
    /// mode is no longer write access, then delete whatever <c>WorkingPath</c> names). Nothing may be saved over
    /// the shared database, the lock must be given back, the archive must survive, and nothing is left to delete.
    /// </summary>
    [Fact]
    public async Task GiveUp_AfterADamagedCopy_SavesNothing_ReleasesTheLock_AndKeepsTheArchive()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var workingPath = svc.WorkingPath;
        DbFileTestHelpers.TrashPage(workingPath, 2);
        var dHash = DbFileTestHelpers.HashOf(db);

        // ── the give-up ──
        var archive = svc.SetAsideFailedLocalCopy();
        await svc.ResetAfterFailedOpenAsync();

        // ── what OnClosing does next ──
        var wasWriteMode = svc.Mode == CheckoutMode.WriteAccess;
        svc.StopAutoSave();
        var saveOutcome = await svc.ReleaseAsync(saveFirst: wasWriteMode);
        if (saveOutcome == SaveOutcome.Success || !svc.HasDirtyMarker)
            svc.CleanupWorkingCopy();
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        Assert.False(wasWriteMode, "after the give-up the session must not look like a write session");
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));                      // D was never written
        Assert.False(File.Exists(LockFileOf(db)), "the lock must be given back");
        Assert.False(lockSvc.IsWriter);
        Assert.NotNull(archive);
        Assert.True(File.Exists(archive!), "the bad file is kept for IT");
        Assert.False(File.Exists(workingPath));
    }

    /// <summary>
    /// Giving up on a copy that holds RECOVERED unsaved work (the "in use" dialog's Close) keeps it: the copy and
    /// its dirty marker survive both the reset and the exit that follows, and D is never written.
    /// </summary>
    [Fact]
    public async Task GiveUp_WithRecoveredUnsavedWork_KeepsTheCopyAndItsMarker_AndSavesNothing()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        InsertTestValue(svc.WorkingPath, "unsaved-edit");
        svc.MarkDirty();
        var workingPath = svc.WorkingPath;
        var copyHash    = DbFileTestHelpers.HashOf(workingPath);
        var dHash       = DbFileTestHelpers.HashOf(db);

        // ── the give-up (no set-aside: the copy was locked, not damaged) ──
        await svc.ResetAfterFailedOpenAsync();

        // ── what OnClosing does next ──
        var wasWriteMode = svc.Mode == CheckoutMode.WriteAccess;
        svc.StopAutoSave();
        var saveOutcome = await svc.ReleaseAsync(saveFirst: wasWriteMode);
        if (saveOutcome == SaveOutcome.Success || !svc.HasDirtyMarker)
            svc.CleanupWorkingCopy();
        lockSvc.PendingRelease?.Wait(LockSettleTimeout);

        Assert.False(wasWriteMode);
        Assert.Equal(dHash, DbFileTestHelpers.HashOf(db));
        Assert.True(File.Exists(workingPath), "unsaved work is never deleted");
        Assert.True(File.Exists(workingPath + ".dirty"));
        Assert.Equal(copyHash, DbFileTestHelpers.HashOf(workingPath));
        Assert.False(File.Exists(LockFileOf(db)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // The shared copy-and-verify step (also used by the checkout itself)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A correct expected hash: the copy lands and verifies.</summary>
    [Fact]
    public async Task CopyAndVerify_MatchingHash_IsOk()
    {
        var (svc, _) = CreateService();
        var db   = CreateHealthyDb();
        var dest = Path.Combine(_workingDir, "copy.db");

        var outcome = await svc.CopyAndVerifyAsync(db, dest, DbFileTestHelpers.HashOf(db), "test");

        Assert.Equal(CheckoutService.CopyVerifyOutcome.Ok, outcome);
        Assert.Equal(DbFileTestHelpers.HashOf(db), DbFileTestHelpers.HashOf(dest));
    }

    /// <summary>
    /// A hash that the copy can never match (as if D kept changing under the copy): the copy is retried once, then
    /// reported as Failed, and the bad file is deleted — a half-verified file must not be mistaken for a good one.
    /// </summary>
    [Fact]
    public async Task CopyAndVerify_NeverMatchingHash_IsFailed_AndDeletesTheBadCopy()
    {
        var (svc, _) = CreateService();
        var db   = CreateHealthyDb();
        var dest = Path.Combine(_workingDir, "copy.db");

        var outcome = await svc.CopyAndVerifyAsync(db, dest, new string('0', 64), "test");

        Assert.Equal(CheckoutService.CopyVerifyOutcome.Failed, outcome);
        Assert.False(File.Exists(dest));
    }

    /// <summary>A source that does not exist cannot be copied: Failed (and D, which is not there, is not created).</summary>
    [Fact]
    public async Task CopyAndVerify_MissingSource_IsFailed()
    {
        var (svc, _) = CreateService();
        var dest = Path.Combine(_workingDir, "copy.db");

        var outcome = await svc.CopyAndVerifyAsync(Path.Combine(_tempDir, "nope.db"), dest, new string('0', 64), "test");

        Assert.Equal(CheckoutService.CopyVerifyOutcome.Failed, outcome);
    }

    /// <summary>A source another program holds exclusively is Unreadable — the retryable "try again in a moment", not a failure.</summary>
    [FactWindowsOnly]
    public async Task CopyAndVerify_SourceHeldExclusively_IsUnreadable()
    {
        var (svc, _) = CreateService();
        var db   = CreateHealthyDb();
        var hash = DbFileTestHelpers.HashOf(db);
        var dest = Path.Combine(_workingDir, "copy.db");

        CheckoutService.CopyVerifyOutcome outcome;
        using (DbFileTestHelpers.LockExclusively(db))
            outcome = await svc.CopyAndVerifyAsync(db, dest, hash, "test");

        Assert.Equal(CheckoutService.CopyVerifyOutcome.Unreadable, outcome);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Housekeeping
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A fresh-copy temporary file left by a crash is swept at the next start, like a save snapshot.</summary>
    [Fact]
    public async Task CleanupStaleCrashArtifacts_SweepsAnOrphanedFreshCopyTempFile()
    {
        var (svc, lockSvc) = CreateService();
        var db = CreateHealthyDb();
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        var tmp = svc.WorkingPath + CheckoutService.FreshCopySuffix;
        File.WriteAllText(tmp, "half-built");
        await FinishAsync(svc, lockSvc);

        svc.CleanupStaleCrashArtifacts(db);

        Assert.False(File.Exists(tmp));
    }
}
