using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="DatabaseInstanceGuard"/> (spec item 4b): the per-database, per-user
/// single-instance guard that is taken before any cleanup, crash detection or checkout.
///
/// <para><b>How "another process" is modelled.</b> The guard is an exclusive file handle
/// (<see cref="FileShare.None"/>). The operating system refuses a second exclusive handle to the same file
/// whichever process asks — Windows by sharing mode, macOS/Linux by <c>flock</c>, which conflicts between
/// separate open file descriptions even inside one process — so a SECOND <see cref="DatabaseInstanceGuard"/>
/// object over the same working folder, or a raw <see cref="FileStream"/> opened on the guard file, is a
/// faithful stand-in for a second TermPoint process. "A holder that dies" is modelled by closing that raw
/// handle without any release call, which is exactly what the OS does for a process that crashes or is
/// ended in Task Manager. One test (Windows only) does it for real with a child PowerShell process.</para>
///
/// <para>Every test uses its own temporary working folder, so the real <c>%AppData%\TermPoint</c> folder is
/// never touched.</para>
///
/// <para><b>Not covered here (thin glue in <c>MainWindow</c>, verified by inspection and by hand):</b>
/// that startup and switching call the guard BEFORE <c>CleanUpAndOfferRecoveryAsync</c> / the checkout, and
/// release it on the paths listed in the step report. What IS covered is everything those calls rely on:
/// the guard itself, the startup loop (<see cref="DatabaseInstanceGuard.AcquireOrChooseAgainAsync"/>, which
/// returns a path only for a database whose guard is held, so nothing after it can run for a refused one),
/// and the switch decision (<see cref="DatabaseSwitchRecovery.Decide"/>, see
/// <see cref="DatabaseSwitchRecoveryTests"/>).</para>
/// </summary>
public sealed class DatabaseInstanceGuardTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _workingDir;
    private readonly List<DatabaseInstanceGuard> _guards = new();

    /// <summary>Creates isolated temporary folders for databases and for the working folder.</summary>
    public DatabaseInstanceGuardTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"dig_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"dig_wrk_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Releases every guard the test created, then deletes both folders (best effort).</summary>
    public void Dispose()
    {
        foreach (var g in _guards) g.Dispose();
        try { Directory.Delete(_tempDir,    recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a guard over the test's working folder; disposed with the test.</summary>
    /// <param name="logger">Logger to use; defaults to a fresh recording logger.</param>
    private DatabaseInstanceGuard NewGuard(RecordingLogger? logger = null)
    {
        var g = new DatabaseInstanceGuard(_workingDir, logger ?? new RecordingLogger());
        _guards.Add(g);
        return g;
    }

    /// <summary>A database path in the source folder (the file need not exist — the guard never opens D).</summary>
    private string DbPath(string name = "dept.db") => Path.Combine(_tempDir, name);

    /// <summary>Opens the guard file for <paramref name="dbPath"/> exclusively, as a second process would.</summary>
    /// <param name="holder">Any guard over the same folder; used only to compute the file's path.</param>
    private static FileStream OpenGuardFileLikeAnotherProcess(DatabaseInstanceGuard holder, string dbPath)
    {
        var path = holder.GuardFilePathFor(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Logger that remembers every message.</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        /// <summary>All messages logged, in order.</summary>
        public List<string> Messages { get; } = new();
        /// <inheritdoc/>
        public bool ThrowOnError { get; set; }
        /// <inheritdoc/>
        public event EventHandler<string>? ErrorLogged;
        /// <inheritdoc/>
        public void LogError(Exception? ex, string? context = null, bool unhandled = false) => Messages.Add(context ?? ex?.Message ?? "");
        /// <inheritdoc/>
        public void LogWarning(string message, string? context = null) => Messages.Add(message);
        /// <inheritdoc/>
        public void LogInfo(string message, string? context = null) => Messages.Add(message);
        /// <inheritdoc/>
        public void LogBreadcrumb(string message, Dictionary<string, string>? metadata = null) { }
    }

    /// <summary>No-op logger for the working-copy tests.</summary>
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
    // Acquire / refuse / release
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The first acquisition of a free database succeeds and is reported as held.</summary>
    [Fact]
    public void Acquire_FreeDatabase_Succeeds()
    {
        var guard = NewGuard();

        Assert.Equal(InstanceGuardResult.Acquired, guard.TryAcquire(DbPath()));
        Assert.True(guard.IsHeld(DbPath()));
    }

    /// <summary>
    /// A second holder of the SAME database is refused — the "hung first copy, user relaunches" case.
    /// Modelled by a second guard object, which the OS treats like a second process.
    /// </summary>
    [Fact]
    public void SecondHolder_SameDatabase_IsRefused()
    {
        var first  = NewGuard();
        var second = NewGuard();

        Assert.Equal(InstanceGuardResult.Acquired,             first.TryAcquire(DbPath()));
        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, second.TryAcquire(DbPath()));
    }

    /// <summary>
    /// A refusal changes nothing: the refused object holds nothing, and the real holder still holds
    /// the guard (a refusal must never release or steal it).
    /// </summary>
    [Fact]
    public void Refusal_ChangesNothing()
    {
        var first  = NewGuard();
        var second = NewGuard();
        first.TryAcquire(DbPath());

        second.TryAcquire(DbPath());

        Assert.False(second.IsHeld(DbPath()));
        Assert.True(first.IsHeld(DbPath()));
        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, NewGuard().TryAcquire(DbPath()));
    }

    /// <summary>
    /// A raw exclusive handle on the guard file — what ANOTHER process holds — refuses us too, and does not
    /// register as ours.
    /// </summary>
    [Fact]
    public void RawExclusiveHandle_OnTheGuardFile_Refuses()
    {
        var guard = NewGuard();
        using var other = OpenGuardFileLikeAnotherProcess(guard, DbPath());

        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, guard.TryAcquire(DbPath()));
        Assert.False(guard.IsHeld(DbPath()));
    }

    /// <summary>Different databases have different guards: holding one never blocks another.</summary>
    [Fact]
    public void DifferentDatabases_DoNotInterfere()
    {
        var a = NewGuard();
        var b = NewGuard();

        Assert.Equal(InstanceGuardResult.Acquired, a.TryAcquire(DbPath("one.db")));
        Assert.Equal(InstanceGuardResult.Acquired, b.TryAcquire(DbPath("two.db")));
        Assert.Equal(InstanceGuardResult.Acquired, a.TryAcquire(DbPath("three.db")));
    }

    /// <summary>The same file name in two different folders is two different databases.</summary>
    [Fact]
    public void SameFileName_InDifferentFolders_AreDifferentDatabases()
    {
        var a = NewGuard();
        var b = NewGuard();
        var one = Path.Combine(_tempDir, "folderA", "dept.db");
        var two = Path.Combine(_tempDir, "folderB", "dept.db");

        Assert.Equal(InstanceGuardResult.Acquired, a.TryAcquire(one));
        Assert.Equal(InstanceGuardResult.Acquired, b.TryAcquire(two));
    }

    /// <summary>
    /// Every spelling of one path — other letter case, redundant segments, relative — maps to the SAME
    /// guard, so a relaunch that names the database slightly differently is still recognised.
    /// </summary>
    [Fact]
    public void PathSpellings_MapToTheSameGuard()
    {
        var plain    = DbPath("Spell.db");
        var shouted  = plain.ToUpperInvariant();
        var dotted   = Path.Combine(_tempDir, ".", "sub", "..", "spell.db");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, plain);

        var first = NewGuard();
        Assert.Equal(InstanceGuardResult.Acquired, first.TryAcquire(plain));

        foreach (var spelling in new[] { shouted, dotted, relative })
        {
            Assert.Equal(DatabaseInstanceGuard.KeyFor(plain), DatabaseInstanceGuard.KeyFor(spelling));
            Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, NewGuard().TryAcquire(spelling));
            Assert.True(first.IsHeld(spelling));
        }
    }

    /// <summary>Releasing lets a new holder acquire; releasing is idempotent.</summary>
    [Fact]
    public void Release_LetsAnotherHolderAcquire()
    {
        var first  = NewGuard();
        var second = NewGuard();
        first.TryAcquire(DbPath());

        first.ReleaseAll();
        first.ReleaseAll();   // idempotent

        Assert.False(first.IsHeld(DbPath()));
        Assert.Equal(InstanceGuardResult.Acquired, second.TryAcquire(DbPath()));
    }

    /// <summary>
    /// The switch A → B ordering: with both held, releasing "all except B" frees A for others and keeps B.
    /// </summary>
    [Fact]
    public void ReleaseAllExcept_KeepsOnlyTheNamedDatabase()
    {
        var mine  = NewGuard();
        var other = NewGuard();
        var a = DbPath("a.db");
        var b = DbPath("b.db");
        mine.TryAcquire(a);
        mine.TryAcquire(b);

        mine.ReleaseAllExcept(b);

        Assert.False(mine.IsHeld(a));
        Assert.True(mine.IsHeld(b));
        Assert.Equal(InstanceGuardResult.Acquired,             other.TryAcquire(a));
        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, other.TryAcquire(b));
    }

    /// <summary>
    /// "Take over as writer" switches to the database the process already has open: keeping that same
    /// database through the switch must leave it held (not released and re-taken, which would let another
    /// process in between).
    /// </summary>
    [Fact]
    public void ReleaseAllExcept_SameDatabase_StaysHeld()
    {
        var mine = NewGuard();
        mine.TryAcquire(DbPath());

        mine.ReleaseAllExcept(DbPath().ToUpperInvariant());   // a different spelling of the same database

        Assert.True(mine.IsHeld(DbPath()));
        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, NewGuard().TryAcquire(DbPath()));
    }

    /// <summary>A null or unknown "keep" path keeps nothing: the no-database state holds no guard.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("never-acquired.db")]
    public void ReleaseAllExcept_NullOrUnknownPath_ReleasesEverything(string? keep)
    {
        var mine = NewGuard();
        mine.TryAcquire(DbPath("a.db"));
        mine.TryAcquire(DbPath("b.db"));

        mine.ReleaseAllExcept(keep);

        Assert.False(mine.IsHeld(DbPath("a.db")));
        Assert.False(mine.IsHeld(DbPath("b.db")));
        Assert.Equal(InstanceGuardResult.Acquired, NewGuard().TryAcquire(DbPath("a.db")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Same-process re-acquisition
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The same process asking again for the database it already holds is NOT refused by its own guard
    /// (it is how "Take over as writer" re-opens the database it already has open read-only). It is not
    /// reference-counted: one release frees it.
    /// </summary>
    [Fact]
    public void SameProcess_ReAcquiringTheDatabaseItHolds_IsAllowed_AndNotCounted()
    {
        var mine = NewGuard();

        Assert.Equal(InstanceGuardResult.Acquired, mine.TryAcquire(DbPath()));
        Assert.Equal(InstanceGuardResult.Acquired, mine.TryAcquire(DbPath()));
        Assert.Equal(InstanceGuardResult.Acquired, mine.TryAcquire(DbPath().ToUpperInvariant()));

        mine.ReleaseAll();

        Assert.Equal(InstanceGuardResult.Acquired, NewGuard().TryAcquire(DbPath()));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A dead holder never blocks
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A holder that dies without releasing — its handle simply closes, as the OS does for a crashed or
    /// killed process — does not block a relaunch, even though its guard FILE is still on disk.
    /// </summary>
    [Fact]
    public void HolderDies_WithoutReleasing_DoesNotBlock()
    {
        var guard = NewGuard();
        var other = OpenGuardFileLikeAnotherProcess(guard, DbPath());

        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, guard.TryAcquire(DbPath()));   // alive: blocks

        other.Dispose();   // "dies": no release call, no file deletion

        Assert.True(File.Exists(guard.GuardFilePathFor(DbPath())), "the leftover file proves nothing");
        Assert.Equal(InstanceGuardResult.Acquired, guard.TryAcquire(DbPath()));
    }

    /// <summary>
    /// The property that rules out a named mutex: the guard has no thread affinity. It can be acquired on
    /// a thread that then EXITS while holding it (a mutex would be abandoned), and released from a
    /// different thread (a mutex would throw).
    /// </summary>
    [Fact]
    public void Guard_HasNoThreadAffinity()
    {
        var mine = NewGuard();

        var acquiring = new Thread(() => mine.TryAcquire(DbPath()));
        acquiring.Start();
        acquiring.Join();                                   // the acquiring thread has exited

        Assert.True(mine.IsHeld(DbPath()));                 // still held: owned by the process, not the thread
        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, NewGuard().TryAcquire(DbPath()));

        mine.ReleaseAll();                                  // released by another thread than acquired it

        Assert.Equal(InstanceGuardResult.Acquired, NewGuard().TryAcquire(DbPath()));
    }

    /// <summary>Disposing releases everything.</summary>
    [Fact]
    public void Dispose_ReleasesEverything()
    {
        var mine = NewGuard();
        mine.TryAcquire(DbPath("a.db"));
        mine.TryAcquire(DbPath("b.db"));

        mine.Dispose();

        Assert.Equal(InstanceGuardResult.Acquired, NewGuard().TryAcquire(DbPath("a.db")));
        Assert.Equal(InstanceGuardResult.Acquired, NewGuard().TryAcquire(DbPath("b.db")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Fail open
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An unexpected failure (here: the guard folder cannot be created because a FILE sits where it must
    /// go) is not "held by another process": it fails OPEN — reported as <see cref="InstanceGuardResult.Failed"/>,
    /// nothing held, logged with the reason — and the guard works as soon as the obstacle is gone.
    /// </summary>
    [Fact]
    public void UnexpectedFailure_FailsOpen_IsLogged_AndRecovers()
    {
        var log   = new RecordingLogger();
        var guard = NewGuard(log);
        var obstacle = Path.Combine(_workingDir, DatabaseInstanceGuard.GuardFolderName);
        File.WriteAllText(obstacle, "not a folder");

        var result = guard.TryAcquire(DbPath());

        Assert.Equal(InstanceGuardResult.Failed, result);
        Assert.False(guard.IsHeld(DbPath()));
        Assert.Contains(log.Messages, m => m.Contains("proceeding without it"));

        File.Delete(obstacle);

        Assert.Equal(InstanceGuardResult.Acquired, guard.TryAcquire(DbPath()));
    }

    /// <summary>A path that cannot be normalised fails open and never throws.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("a\0b.db")]
    public void InvalidPath_FailsOpen_AndNeverThrows(string path)
    {
        var guard = NewGuard();

        Assert.Equal(InstanceGuardResult.Failed, guard.TryAcquire(path));
        Assert.False(guard.IsHeld(path));
        guard.ReleaseAllExcept(path);   // must not throw either
    }

    /// <summary>After <see cref="DatabaseInstanceGuard.Dispose"/> the guard fails open instead of throwing.</summary>
    [Fact]
    public void AfterDispose_TryAcquire_FailsOpen()
    {
        var guard = NewGuard();
        guard.Dispose();

        Assert.Equal(InstanceGuardResult.Failed, guard.TryAcquire(DbPath()));
    }

    /// <summary>
    /// What counts as contention: Windows sharing / lock violations (and the raw EWOULDBLOCK errno elsewhere);
    /// never a missing folder, a long path, a full disk or a generic I/O error — those must fail open.
    /// </summary>
    [Fact]
    public void IsContention_ClassifiesOnlySharingViolations()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(DatabaseInstanceGuard.IsContention(new IOException("sharing",  unchecked((int)0x80070020))));
            Assert.True(DatabaseInstanceGuard.IsContention(new IOException("lock",     unchecked((int)0x80070021))));
            Assert.False(DatabaseInstanceGuard.IsContention(new IOException("disk full", unchecked((int)0x80070070))));
            Assert.False(DatabaseInstanceGuard.IsContention(new IOException("exists",  unchecked((int)0x800700B7))));
            Assert.False(DatabaseInstanceGuard.IsContention(new IOException("generic")));
            Assert.False(DatabaseInstanceGuard.IsContention(new FileNotFoundException("gone", "x")));
            Assert.False(DatabaseInstanceGuard.IsContention(new PathTooLongException("long")));
            Assert.False(DatabaseInstanceGuard.IsContention(new DirectoryNotFoundException("nodir")));
        }
        else
        {
            Assert.True(DatabaseInstanceGuard.IsContention(new IOException("EWOULDBLOCK mac",   35)));
            Assert.True(DatabaseInstanceGuard.IsContention(new IOException("EWOULDBLOCK linux", 11)));
            Assert.False(DatabaseInstanceGuard.IsContention(new IOException("EEXIST", 17)));
            Assert.False(DatabaseInstanceGuard.IsContention(new IOException("generic")));
            Assert.False(DatabaseInstanceGuard.IsContention(new FileNotFoundException("gone", "x")));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Where the guard lives, and what may touch it
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The guard file sits in its own <c>guards</c> subfolder of the working folder, named from the same
    /// hash prefix as the working copy plus a <c>.guard</c> extension — so it can never collide with D',
    /// D'', a marker or a sidecar, whatever a database file is called.
    /// </summary>
    [Fact]
    public void GuardFile_LivesInItsOwnSubfolder()
    {
        var guard = NewGuard();
        var file  = guard.GuardFilePathFor(DbPath());

        Assert.Equal(Path.Combine(_workingDir, DatabaseInstanceGuard.GuardFolderName), Path.GetDirectoryName(file));
        Assert.Equal(".guard", Path.GetExtension(file));
        Assert.Equal(16, Path.GetFileNameWithoutExtension(file).Length);
    }

    /// <summary>
    /// The key uses the SAME normalisation as the working copy's name: <see cref="CheckoutService.NormalizeSourcePath"/>
    /// is the full path lower-cased, and the guard key starts with the 8 hex characters that prefix D'.
    /// Pinned because changing it would silently rename every user's working copies.
    /// </summary>
    [Fact]
    public void Key_ReusesTheWorkingCopyNormalisation()
    {
        var path = Path.Combine(_tempDir, "Mixed", "Case.DB");
        var normalized = Path.GetFullPath(path).ToLowerInvariant();
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

        Assert.Equal(normalized, CheckoutService.NormalizeSourcePath(path));
        Assert.Equal(hex[..16],  DatabaseInstanceGuard.KeyFor(path));
    }

    /// <summary>
    /// D' is still named <c>{first 8 hex of SHA-256(normalised path)}_{file name}</c> after the shared
    /// normalisation was factored out — the guard key and the working-copy prefix agree.
    /// </summary>
    [Fact]
    public async Task WorkingCopyNaming_IsUnchanged_AndAgreesWithTheGuardKey()
    {
        var db = DbPath("Naming.db");
        DbFileTestHelpers.CreateHealthyDatabase(db);
        var svc = NewCheckoutService();

        await svc.CheckoutAsync(db);
        var name = Path.GetFileName(svc.WorkingPath);
        await svc.ReleaseAsync(saveFirst: false);

        var prefix = DatabaseInstanceGuard.KeyFor(db)[..8];
        Assert.StartsWith(prefix + "_Naming", name);   // D' is "{prefix}_Naming.db", D'' is "{prefix}_Naming_ro.db"
    }

    /// <summary>
    /// None of the working-copy cleanups — stale-artifact sweep, discard, archive, working-copy delete,
    /// reset after a failed open, a full checkout/release cycle — touches a held guard file: it is still
    /// there, still held, and D' lives BESIDE the guards folder, not in it.
    /// </summary>
    [Fact]
    public async Task WorkingCopyCleanups_NeverTouchTheGuardFile()
    {
        var db = DbPath("clean.db");
        DbFileTestHelpers.CreateHealthyDatabase(db);
        var holder = NewGuard();
        Assert.Equal(InstanceGuardResult.Acquired, holder.TryAcquire(db));
        var guardFile = holder.GuardFilePathFor(db);
        var svc = NewCheckoutService();

        svc.CleanupStaleCrashArtifacts(db);
        svc.DiscardCrash(db);
        svc.ArchiveCorruptWorkingCopy(db);
        await svc.CleanupOrphanedTmpAsync(db);

        await svc.CheckoutAsync(db);
        Assert.Equal(_workingDir, Path.GetDirectoryName(svc.WorkingPath));   // beside the guards folder
        svc.MarkDirty();
        svc.CleanupStaleCrashArtifacts(db);                                   // D' + marker present
        await svc.ReleaseAsync(saveFirst: false);
        svc.CleanupWorkingCopy();
        await svc.ResetAfterFailedOpenAsync();
        svc.DiscardCrash(db);

        Assert.True(File.Exists(guardFile));
        Assert.True(holder.IsHeld(db));
        Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, NewGuard().TryAcquire(db));
    }

    /// <summary>Builds an isolated checkout service over the test's working folder.</summary>
    private CheckoutService NewCheckoutService()
        => new(lockService: new WriteLockService(), logger: new NullLogger(), dispatch: a => a(), workingDir: _workingDir);

    // ═════════════════════════════════════════════════════════════════════════
    // The startup loop: refused → message → chooser → …
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A free database is returned at once, with no message and no chooser.</summary>
    [Fact]
    public async Task StartupLoop_FreeDatabase_ReturnsItWithoutAnyDialog()
    {
        var guard = NewGuard();
        var messages = 0;
        var choosers = new List<string>();

        var result = await guard.AcquireOrChooseAgainAsync(
            DbPath(), () => { messages++; return Task.CompletedTask; },
            p => { choosers.Add(p); return Task.FromResult<string?>(null); });

        Assert.Equal(DbPath(), result);
        Assert.Equal(0, messages);
        Assert.Empty(choosers);
        Assert.True(guard.IsHeld(DbPath()));
    }

    /// <summary>
    /// A refused database: the message is shown, the chooser names the refused path, and picking a free
    /// database acquires THAT one. Nothing is held for the refused database.
    /// </summary>
    [Fact]
    public async Task StartupLoop_Refused_ShowsMessageThenChooser_AndOpensTheOtherPick()
    {
        var holder = NewGuard();
        var mine   = NewGuard();
        holder.TryAcquire(DbPath("busy.db"));
        var order = new List<string>();

        var result = await mine.AcquireOrChooseAgainAsync(
            DbPath("busy.db"),
            () => { order.Add("message"); return Task.CompletedTask; },
            p => { order.Add("chooser:" + p); return Task.FromResult<string?>(DbPath("free.db")); });

        Assert.Equal(DbPath("free.db"), result);
        Assert.Equal(["message", "chooser:" + DbPath("busy.db")], order);
        Assert.False(mine.IsHeld(DbPath("busy.db")));
        Assert.True(mine.IsHeld(DbPath("free.db")));
    }

    /// <summary>
    /// Picking the SAME database again is refused again (the loop), the message is shown each time, and
    /// Exit ends it with null — nothing acquired, so the caller opens nothing.
    /// </summary>
    [Fact]
    public async Task StartupLoop_PickingTheSameDatabaseAgain_IsRefusedAgain_UntilExit()
    {
        var holder = NewGuard();
        var mine   = NewGuard();
        holder.TryAcquire(DbPath());
        var messages = 0;
        var picks = new Queue<string?>([DbPath(), DbPath(), null]);   // same, same, then Exit

        var result = await mine.AcquireOrChooseAgainAsync(
            DbPath(),
            () => { messages++; return Task.CompletedTask; },
            _ => Task.FromResult(picks.Dequeue()));

        Assert.Null(result);
        Assert.Equal(3, messages);          // the initial refusal plus two refused re-picks
        Assert.False(mine.IsHeld(DbPath()));
    }

    /// <summary>
    /// If the other window is closed while the chooser is up, picking the same database again succeeds.
    /// </summary>
    [Fact]
    public async Task StartupLoop_HolderLeavesWhileChooserIsUp_SamePickThenSucceeds()
    {
        var holder = NewGuard();
        var mine   = NewGuard();
        holder.TryAcquire(DbPath());

        var result = await mine.AcquireOrChooseAgainAsync(
            DbPath(),
            () => Task.CompletedTask,
            _ => { holder.ReleaseAll(); return Task.FromResult<string?>(DbPath()); });

        Assert.Equal(DbPath(), result);
        Assert.True(mine.IsHeld(DbPath()));
    }

    /// <summary>A guard that FAILED to work (fail open) lets the database through, like an acquired one.</summary>
    [Fact]
    public async Task StartupLoop_FailedGuard_LetsTheDatabaseThrough()
    {
        var guard = NewGuard();
        File.WriteAllText(Path.Combine(_workingDir, DatabaseInstanceGuard.GuardFolderName), "obstacle");
        var dialogs = 0;

        var result = await guard.AcquireOrChooseAgainAsync(
            DbPath(), () => { dialogs++; return Task.CompletedTask; },
            _ => { dialogs++; return Task.FromResult<string?>(null); });

        Assert.Equal(DbPath(), result);
        Assert.Equal(0, dialogs);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Real child process (Windows)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The real thing, across a process boundary: a child PowerShell process holds the guard file
    /// exclusively. While it is alive we are refused; once it is KILLED (no release, no cleanup — what a
    /// crash or Task Manager does) the OS closes its handle and we acquire. Windows only: it needs
    /// PowerShell and an OS-enforced <c>FileShare.None</c>; on macOS/Linux the in-process tests above
    /// model the same conflict through <c>flock</c>, and the real cross-process check is the Mac field test.
    /// </summary>
    [FactWindowsOnly]
    public async Task CrossProcess_LiveHolderBlocks_AndKilledHolderReleases()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            return;   // no Windows PowerShell on this machine: nothing to launch (every supported Windows has it)

        var guard     = NewGuard();
        var guardFile = guard.GuardFilePathFor(DbPath());
        Directory.CreateDirectory(Path.GetDirectoryName(guardFile)!);

        // -EncodedCommand (UTF-16 base64) avoids every quoting problem with the temp path.
        var script =
            $"$f = [System.IO.File]::Open('{guardFile.Replace("'", "''")}', 'OpenOrCreate', 'ReadWrite', 'None'); " +
            "[Console]::Out.WriteLine('READY'); [Console]::Out.Flush(); Start-Sleep -Seconds 120";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var child = Process.Start(new ProcessStartInfo(powershell, $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        })!;

        try
        {
            var first = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal("READY", first);

            Assert.Equal(InstanceGuardResult.HeldByAnotherProcess, guard.TryAcquire(DbPath()));   // alive: blocks
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(30_000), "the child process did not exit after being killed");
        }

        // Killed with no release: the OS must have closed its handle. Allow a moment for process rundown.
        var result = InstanceGuardResult.HeldByAnotherProcess;
        for (var attempt = 0; attempt < 50 && result == InstanceGuardResult.HeldByAnotherProcess; attempt++)
        {
            result = guard.TryAcquire(DbPath());
            if (result == InstanceGuardResult.HeldByAnotherProcess)
                await Task.Delay(100);
        }
        Assert.Equal(InstanceGuardResult.Acquired, result);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Wording
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The "already open" dialog and the chooser banner use the exact agreed wording.</summary>
    [Fact]
    public void AlreadyOpen_Wording_IsExactlyAsAgreed()
    {
        Assert.Equal("TermPoint Is Already Open", DatabaseInstanceGuard.AlreadyOpenTitle);
        Assert.Equal(
            "TermPoint is already open on this computer. If it isn't responding, end it in Task Manager " +
            "and start again — your changes will be offered back.",
            DatabaseInstanceGuard.AlreadyOpenMessage);
        Assert.Equal(
            "That database is already open in another TermPoint window on this computer. " +
            "Choose a different database, or exit.",
            DatabaseChooserViewModel.AlreadyOpenDescription);
    }

    /// <summary>
    /// The same-computer read-only banner names the user who is editing and says what to do if that window
    /// is stuck; with no known holder it falls back to the older wording that names nobody.
    /// </summary>
    [Fact]
    public void SameComputerReadOnlyMessage_NamesTheHolder_AndFallsBackWhenUnknown()
    {
        Assert.Equal(
            "Read-only — jsmith is already editing this database in TermPoint on this computer. " +
            "If that window has stopped responding, it can be ended in Task Manager.",
            MainWindowViewModel.SameComputerReadOnlyMessage("jsmith"));

        const string fallback =
            "Read-only — another TermPoint window is already editing this database on this computer.";
        Assert.Equal(fallback, MainWindowViewModel.SameComputerReadOnlyMessage(null));
        Assert.Equal(fallback, MainWindowViewModel.SameComputerReadOnlyMessage(""));
        Assert.Equal(fallback, MainWindowViewModel.SameComputerReadOnlyMessage("   "));
    }
}
