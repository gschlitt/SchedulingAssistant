using System.Security.Cryptography;
using System.Text;

namespace TermPoint.Services;

/// <summary>The outcome of <see cref="DatabaseInstanceGuard.TryAcquire"/>.</summary>
public enum InstanceGuardResult
{
    /// <summary>
    /// This process now holds the guard for the database — or already held it (re-acquiring is allowed).
    /// The caller may run cleanup, crash detection and checkout for the database.
    /// </summary>
    Acquired,

    /// <summary>
    /// Another LIVE process (a second TermPoint run by the same user on this computer) holds the guard
    /// for this database. The caller must skip all cleanup and recovery, take no lock and make no copy,
    /// and tell the user (<see cref="DatabaseInstanceGuard.AlreadyOpenTitle"/> /
    /// <see cref="DatabaseInstanceGuard.AlreadyOpenMessage"/>).
    /// </summary>
    HeldByAnotherProcess,

    /// <summary>
    /// The guard could not be taken for a reason that is NOT "someone else holds it" (the working folder
    /// can't be written, the path can't be normalised, …). Logged. The caller proceeds exactly as if it
    /// had been acquired — the guard fails OPEN; see the remarks on <see cref="DatabaseInstanceGuard"/>.
    /// </summary>
    Failed
}

/// <summary>
/// A per-database, per-user single-instance guard (spec item 4b): at most one live TermPoint process of a
/// given Windows/macOS user may work on a given database (D) at a time.
///
/// <para><b>Why it exists.</b> Everything TermPoint does before it takes the write lock — sweeping stale
/// working-copy artifacts, deciding that a dirty marker means "the last session crashed" and offering
/// Restore/Discard — assumes no other process of this user is working on the same local files. (Deleting an
/// orphaned <c>D.tmp</c> on the share is a different matter: it is done only once the write lock is held,
/// inside the checkout.) A second copy launched while the first is hung (or hidden) broke that assumption:
/// it saw the live session's dirty marker as a crash, Discard deleted the live marker, and a later sweep
/// could delete the live working copy (D'). The guard is taken BEFORE any of that on both startup and
/// switching, and while another process holds it none of it runs.</para>
///
/// <para><b>The mechanism: an exclusive file handle.</b> The guard is a small empty file in
/// <c>&lt;working folder&gt;\guards\</c> (beside the working copies D'/D'', in the per-user
/// <c>%AppData%\TermPoint\working</c>) opened with <see cref="FileShare.None"/> and kept open while the
/// guard is held. The operating system, not TermPoint, enforces and releases it:</para>
/// <list type="bullet">
/// <item><description><b>Dies with its owner.</b> A crashed or Task-Manager-killed process closes its
/// handles, so a dead copy can never block a relaunch — while a frozen but live copy keeps its handle and
/// correctly does. (Nothing is ever written to the file and it is never deleted, so a leftover file proves
/// nothing; only a live handle counts.)</description></item>
/// <item><description><b>Per user by construction.</b> The folder lives under the user's own application
/// data, so another Windows user on the same PC has a different guard file and different working copies
/// — they share nothing, and need no protection from each other. (The shared write lock arbitrates between
/// users.)</description></item>
/// <item><description><b>Cross-platform.</b> On Windows <c>FileShare.None</c> is an OS sharing mode. On macOS
/// .NET implements it with <c>flock(LOCK_EX)</c>, which is honoured between .NET processes (all that need
/// honour it here) and released by the kernel when the process exits.</description></item>
/// </list>
///
/// <para><b>Why not a named <see cref="Mutex"/>.</b> It would be released by the OS on death too, but a
/// mutex is <i>thread-affine</i>: it must be released by the thread that acquired it, and a thread that
/// exits while owning one abandons it. This code is acquired from async startup/switch continuations and
/// released from others, which may run on different threads — a latent source of
/// <see cref="ApplicationException"/> on release and of "abandoned" semantics. A file handle has no thread
/// affinity. A per-user name also has to be built by hand for a mutex (session/namespace rules); the
/// folder gives that for free.</para>
///
/// <para><b>Same-process re-acquisition is allowed.</b> The guard remembers what this process holds, so
/// asking for a database it already holds returns <see cref="InstanceGuardResult.Acquired"/> without
/// opening a second handle. That is what lets "Take over as writer" switch to the database the process
/// already has open read-only without its own guard refusing it. A second <see cref="DatabaseInstanceGuard"/>
/// object in the same process is, by design, treated as a different holder (this is how the tests model
/// "another process" faithfully on every platform: the OS refuses a second exclusive handle to the same
/// file whichever process asks).</para>
///
/// <para><b>Fails open.</b> If the guard cannot be taken for any reason other than "held by another live
/// process" (the folder can't be created or written, the path is invalid, an unexpected I/O error), the
/// result is <see cref="InstanceGuardResult.Failed"/>: the failure is logged and the app proceeds as it
/// did before the guard existed. A broken guard must never lock the user out of their own data. The cost
/// is that the protection is off for that session — visible in the log. The same goes for a platform or file
/// system that does not support the lock (macOS advisory locking disabled, a network-mounted home
/// folder): the open simply succeeds and there is nothing to refuse.</para>
///
/// <para><b>Key.</b> The file is named from SHA-256 of the same normalised path that names the working copy
/// (<c>CheckoutService.NormalizeSourcePath</c>: full path, lower-cased), so every spelling of one path maps to
/// one guard, and exactly the sessions that would share a D' are the sessions kept apart. It lives in its own
/// <c>guards</c> subfolder, so its name can never collide with D', D'', a dirty marker, a <c>.savetmp</c>, a WAL
/// sidecar or a corrupt-archive — however a database file happens to be named — and none of the
/// working-copy cleanups (<c>CleanupStaleCrashArtifacts</c>, <c>DiscardCrash</c>, <c>CleanupWorkingCopy</c>,
/// <c>ResetAfterFailedOpenAsync</c>, <c>ArchiveCorruptWorkingCopy</c>), which act only on paths computed
/// from the database path, ever reaches it.</para>
///
/// <para><b>Threading.</b> All members are safe to call from any thread (a lock guards the table of held
/// guards). Nothing blocks or waits: acquiring is a single non-blocking open.</para>
/// </summary>
public sealed class DatabaseInstanceGuard : IDisposable
{
    // ── Shared user-facing text ───────────────────────────────────────────────

    /// <summary>Title of the dialog shown when another live copy of TermPoint holds the guard.</summary>
    public const string AlreadyOpenTitle = "TermPoint Is Already Open";

    /// <summary>
    /// Body of the dialog shown when another live copy of TermPoint holds the guard. Tells the user what
    /// to do if that copy is frozen, and that nothing is lost: the frozen copy's unsaved changes are kept
    /// on disk and are offered back at the next start.
    /// </summary>
    public const string AlreadyOpenMessage =
        "TermPoint is already open on this computer. If it isn't responding, end it in Task Manager " +
        "and start again — your changes will be offered back.";

    /// <summary>Name of the subfolder of the working folder that holds the guard files.</summary>
    internal const string GuardFolderName = "guards";

    /// <summary>File extension of a guard file.</summary>
    private const string GuardFileExtension = ".guard";

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>The folder that contains the guard files (<c>&lt;working folder&gt;\guards</c>).</summary>
    private readonly string _guardDir;

    /// <summary>Where unexpected failures are reported.</summary>
    private readonly IAppLogger _logger;

    /// <summary>Protects <see cref="_held"/> and <see cref="_disposed"/>.</summary>
    private readonly object _gate = new();

    /// <summary>
    /// The guards this object holds: guard-file key → the open exclusive handle. Closing a handle is what
    /// releases its guard.
    /// </summary>
    private readonly Dictionary<string, FileStream> _held = new();

    /// <summary>True once <see cref="Dispose"/> has run; further acquisitions then fail open.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates a guard whose files live under <paramref name="workingDir"/>.
    /// </summary>
    /// <param name="workingDir">
    /// The working folder (the one that holds D'/D''). Defaults to
    /// <see cref="CheckoutService.DefaultWorkingDirectory"/>. Pass an isolated temporary folder in tests, so
    /// the real per-user folder is never touched.
    /// </param>
    /// <param name="logger">Logger for unexpected failures. Defaults to <see cref="App.Logger"/>.</param>
    public DatabaseInstanceGuard(string? workingDir = null, IAppLogger? logger = null)
    {
        _guardDir = Path.Combine(workingDir ?? CheckoutService.DefaultWorkingDirectory, GuardFolderName);
        _logger   = logger ?? App.Logger;
    }

    // ── Acquire ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Takes the guard for <paramref name="dbPath"/> without blocking.
    ///
    /// <para>Asking for a database this object already holds succeeds without opening anything. Otherwise
    /// the guard file is opened exclusively: if the OS refuses because another live process holds it, the
    /// result is <see cref="InstanceGuardResult.HeldByAnotherProcess"/> and NOTHING is changed — in
    /// particular no guard this object already holds is released. Any other failure is logged and returns
    /// <see cref="InstanceGuardResult.Failed"/> (fail open). Never throws.</para>
    /// </summary>
    /// <param name="dbPath">Full path of the database (D), in any spelling.</param>
    /// <returns>
    /// <see cref="InstanceGuardResult.Acquired"/> (now held by this object, or already was),
    /// <see cref="InstanceGuardResult.HeldByAnotherProcess"/>, or <see cref="InstanceGuardResult.Failed"/>.
    /// </returns>
    public InstanceGuardResult TryAcquire(string dbPath)
    {
        string key;
        try
        {
            key = KeyFor(dbPath);
        }
        catch (Exception ex)
        {
            // An empty or invalid path can't be normalised. There is nothing to guard.
            return FailOpen(dbPath, ex);
        }

        lock (_gate)
        {
            if (_disposed)
                return InstanceGuardResult.Failed;

            if (_held.ContainsKey(key))
                return InstanceGuardResult.Acquired;   // this process already holds it: re-acquiring is allowed

            try
            {
                Directory.CreateDirectory(_guardDir);

                // Exclusive: FileShare.None makes the OS refuse every other open of this file for as long
                // as the handle lives, and release it the moment the process dies. OpenOrCreate, because
                // the file is never deleted — only its handle matters. The size is never touched.
                var stream = new FileStream(
                    GuardFilePath(key), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
                _held[key] = stream;
                return InstanceGuardResult.Acquired;
            }
            catch (IOException ex) when (IsContention(ex))
            {
                return InstanceGuardResult.HeldByAnotherProcess;
            }
            catch (Exception ex)
            {
                return FailOpen(dbPath, ex);
            }
        }
    }

    /// <summary>
    /// Runs the startup acquisition loop: takes the guard for <paramref name="dbPath"/>, and while another
    /// live process holds it, tells the user and lets them choose a different database — until one can be
    /// acquired or they leave. Picking the same database again is refused again (the loop), exactly as
    /// the first time.
    ///
    /// <para>A seam kept free of UI so the "refused → message → chooser → …" behaviour is unit-tested; the
    /// window supplies the two dialogs. Nothing that needs the guard (cleanup, recovery, checkout) runs
    /// inside it: those steps are the caller's, and run only for the path this method returns.</para>
    /// </summary>
    /// <param name="dbPath">The database the user (or settings) chose to open.</param>
    /// <param name="showAlreadyOpenAsync">Shows the "TermPoint is already open" dialog and completes when it is dismissed.</param>
    /// <param name="showChooserAsync">
    /// Shows the database chooser (naming the refused path) and returns the database the user picked, or
    /// null when they left it — Exit, which shuts the app down, or Create New, which hands over to the
    /// setup wizard.
    /// </param>
    /// <returns>
    /// The path whose guard is now held (or failed open) — the one to open; or null when the user left
    /// the chooser, in which case nothing was acquired and nothing may be opened.
    /// </returns>
    public async Task<string?> AcquireOrChooseAgainAsync(
        string dbPath,
        Func<Task> showAlreadyOpenAsync,
        Func<string, Task<string?>> showChooserAsync)
    {
        while (true)
        {
            if (TryAcquire(dbPath) != InstanceGuardResult.HeldByAnotherProcess)
                return dbPath;

            _logger.LogInfo($"DatabaseInstanceGuard: another TermPoint process holds the guard for '{dbPath}' — refusing to open it.");
            await showAlreadyOpenAsync();

            var chosen = await showChooserAsync(dbPath);
            if (chosen is null)
                return null;

            dbPath = chosen;
        }
    }

    // ── Release ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Releases every guard this object holds EXCEPT the one for <paramref name="keepDbPath"/> (which is
    /// left held if it was). Used when a database switch has torn the old database down (release the old
    /// guard, keep the new one — and keep it when old and new are the same database), and to give back a
    /// guard taken for a switch target that is not going to be opened. Never throws; a path that is not
    /// held, or cannot be normalised, simply keeps nothing.
    /// </summary>
    /// <param name="keepDbPath">The database whose guard stays held, or null to release everything.</param>
    public void ReleaseAllExcept(string? keepDbPath)
    {
        string? keepKey = null;
        if (!string.IsNullOrEmpty(keepDbPath))
        {
            try { keepKey = KeyFor(keepDbPath); }
            catch { /* an invalid path holds no guard; release everything */ }
        }

        lock (_gate)
        {
            foreach (var key in _held.Keys.Where(k => k != keepKey).ToList())
            {
                // Closing the handle IS the release. A failure to close can't leave the guard held (the
                // OS drops it with the handle at the latest when the process ends), so it is only logged.
                try { _held[key].Dispose(); }
                catch (Exception ex) { _logger.LogInfo($"DatabaseInstanceGuard: error while releasing a guard ({ex.GetType().Name}: {ex.Message})"); }
                _held.Remove(key);
            }
        }
    }

    /// <summary>
    /// Releases every guard this object holds: the "no database open" state, and normal application exit
    /// (the OS would release them when the process ends anyway). Never throws; safe to call repeatedly.
    /// </summary>
    public void ReleaseAll() => ReleaseAllExcept(null);

    /// <summary>True when this object currently holds the guard for <paramref name="dbPath"/>.</summary>
    /// <param name="dbPath">Full path of the database, in any spelling.</param>
    /// <returns>True only if held; false for a path that is not held or cannot be normalised.</returns>
    public bool IsHeld(string dbPath)
    {
        try
        {
            var key = KeyFor(dbPath);
            lock (_gate) return _held.ContainsKey(key);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Releases everything and stops the object from acquiring any more.</summary>
    public void Dispose()
    {
        lock (_gate) _disposed = true;
        ReleaseAll();
    }

    // ── Keys and files ────────────────────────────────────────────────────────

    /// <summary>
    /// The guard key for a database: the first 16 hex characters of SHA-256 of the normalised path
    /// (<see cref="CheckoutService.NormalizeSourcePath"/>, the same normalisation that names D'). 64 bits
    /// is far more than needed for the handful of databases one user opens, and keeps the file name short.
    /// </summary>
    /// <param name="dbPath">Full path of the database, in any spelling.</param>
    /// <returns>An upper-case hex string; equal for every spelling of one path.</returns>
    /// <exception cref="ArgumentException">The path is empty or invalid.</exception>
    internal static string KeyFor(string dbPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(CheckoutService.NormalizeSourcePath(dbPath)));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>Full path of the guard file that belongs to <paramref name="key"/>.</summary>
    /// <param name="key">A value from <see cref="KeyFor"/>.</param>
    private string GuardFilePath(string key) => Path.Combine(_guardDir, key + GuardFileExtension);

    /// <summary>
    /// Full path of the guard file for <paramref name="dbPath"/>. Exposed for tests, which assert that the
    /// working-copy cleanups never touch it.
    /// </summary>
    /// <param name="dbPath">Full path of the database, in any spelling.</param>
    internal string GuardFilePathFor(string dbPath) => GuardFilePath(KeyFor(dbPath));

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>
    /// True when <paramref name="ex"/>, thrown by an exclusive open of the guard file, means "another
    /// process has it open" — as opposed to any other I/O failure, which must fail OPEN.
    ///
    /// <list type="bullet">
    /// <item><description><b>Windows:</b> an <see cref="IOException"/> whose HRESULT wraps Win32
    /// <c>ERROR_SHARING_VIOLATION</c> (32) or <c>ERROR_LOCK_VIOLATION</c> (33).</description></item>
    /// <item><description><b>macOS / Linux:</b> .NET reports a contended <c>flock</c> as an
    /// <see cref="IOException"/> whose HRESULT is the raw <c>EWOULDBLOCK</c> errno — 35 on macOS, 11 on
    /// Linux. If a future runtime ever reported it differently the effect would be to FAIL OPEN (no
    /// guard), never to lock a user out; the logged HRESULT would show it.</description></item>
    /// </list>
    /// "Not found" and "path too long" are I/O exceptions too but are never contention.
    /// </summary>
    /// <param name="ex">The exception from the open.</param>
    /// <returns>True for a sharing / lock violation.</returns>
    internal static bool IsContention(IOException ex)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException or PathTooLongException or DriveNotFoundException)
            return false;

        var hr = ex.HResult;

        if (OperatingSystem.IsWindows())
        {
            const int Win32Facility         = unchecked((int)0x80070000);
            const int ErrorSharingViolation = 32;
            const int ErrorLockViolation    = 33;

            return (hr & unchecked((int)0xFFFF0000)) == Win32Facility
                && (hr & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
        }

        const int EagainLinux = 11;
        const int EagainMac   = 35;
        return hr is EagainLinux or EagainMac;
    }

    /// <summary>
    /// Logs why the guard could not be taken and returns <see cref="InstanceGuardResult.Failed"/>, so the
    /// caller proceeds unguarded (fail open). Logged at the plain information level on purpose: it must
    /// not raise the error banner or a crash report, yet it must be findable in the log — with the
    /// HRESULT, which is what a field tester on a Mac needs to read.
    /// </summary>
    /// <param name="dbPath">The database the guard was for.</param>
    /// <param name="ex">Why it failed.</param>
    private InstanceGuardResult FailOpen(string dbPath, Exception ex)
    {
        try
        {
            _logger.LogInfo(
                $"DatabaseInstanceGuard: could not take the single-instance guard for '{dbPath}' " +
                $"({ex.GetType().Name}, HRESULT 0x{ex.HResult:X8}: {ex.Message}) — proceeding without it.");
        }
        catch
        {
            // Logging must never turn a fail-open into a failure.
        }
        return InstanceGuardResult.Failed;
    }
}
