using Microsoft.Data.Sqlite;
using TermPoint.Services;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The simulated world of the multi-computer harness (spec item 9.3, step 9.3a): one temp folder playing the
/// network share, one shared fake clock, and the <see cref="Computer"/>s (Bob's laptop, Alice's PC, ...) that open,
/// edit, save and close the same database in ONE test process.
///
/// <para><b>What makes several computers possible in one process.</b></para>
/// <list type="bullet">
/// <item><description><b>Identity:</b> every computer has its own <see cref="LockIdentity"/> (machine name, user, a
/// fresh process id per launch), so the lock file tells them apart as it would on a real network. The World keeps the
/// registry of "live processes" that those identities look other processes up in, so a crashed or closed process
/// reads as "no such process" (a same-machine relaunch can then reclaim its lock at once), exactly as
/// <c>Process.GetProcessById</c> would say.</description></item>
/// <item><description><b>Time:</b> one <see cref="FakeTimeProvider"/> is given to every lock service. Minutes of
/// "the heartbeat went stale" are one <see cref="AdvanceTime"/> call. No timer runs by itself: the walk decides when a
/// heartbeat, autosave or poll tick happens.</description></item>
/// <item><description><b>The current actor:</b> only one computer acts at a time. The World records WHO is acting
/// (<see cref="CurrentActor"/>, set around every <see cref="Computer"/> action) because two process-wide settings
/// differ per computer: the network fault hook asks whose network it is, and <c>AppSettings.Current</c> (one static
/// object for every simulated computer) is re-set before each action.</description></item>
/// <item><description><b>The network:</b> <see cref="NetworkDown"/> / <see cref="NetworkUp"/> switch a computer's
/// connection to the share. While it is down, every operation that works on the share (a path or destination under
/// <see cref="ShareDir"/>, or one of the generic lock operations) is answered with a timeout through
/// <see cref="NetworkFaults.Decide"/>; local files keep working. The hook is installed by the constructor and removed
/// by <see cref="Dispose"/>. <see cref="CrashPoints.Hook"/> is left null (mid-operation crashes arrive in 9.3c).</description></item>
/// </list>
///
/// <para>Static state is shared by everything in the process (the fault hook, <c>AppSettings.Current</c>), so a World
/// must not be used by two tests at once. The test assembly already runs its tests one at a time.</para>
/// </summary>
internal sealed class World : IDisposable
{
    /// <summary>Name of the test-only table every traceable edit goes into (created by <see cref="CreateDatabase"/>).</summary>
    public const string EditsTable = "_harness_edits";

    /// <summary>Guards <see cref="_liveProcesses"/> (the lookup can be called from a pool thread).</summary>
    private readonly object _gate = new();

    /// <summary>Process id to start time (UTC) for every simulated process that is currently running.</summary>
    private readonly Dictionary<int, DateTime> _liveProcesses = new();

    /// <summary>The computers added so far, in order.</summary>
    private readonly List<Computer> _computers = new();

    /// <summary>Protects the settings file (and restores <c>AppSettings.Current</c>) when the World is disposed.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>Next process id handed out; simulated ids are unique across the whole World.</summary>
    private int _nextProcessId = 5000;

    /// <summary>Counter that names the frozen copies a crash takes.</summary>
    private int _freezeCounter;

    /// <summary>The computer acting right now (read by the fault hook from any thread).</summary>
    private volatile Computer? _currentActor;

    /// <summary>Creates the folders, installs the network fault hook and puts the settings in a known state.</summary>
    public World()
    {
        var id = Guid.NewGuid().ToString("N");
        RootDir  = Path.Combine(Path.GetTempPath(), $"harness_{id}");
        ShareDir = Path.Combine(RootDir, "share");
        Directory.CreateDirectory(ShareDir);
        Directory.CreateDirectory(ScratchDir);

        CrashPoints.Hook     = null;      // 9.3a never crashes inside an operation
        NetworkFaults.Decide = DecideFault;
        ApplySettings();
    }

    /// <summary>The temp folder that holds everything the World makes (share, computers, scratch copies).</summary>
    public string RootDir { get; }

    /// <summary>The folder playing the network share: it holds D, D's lock file and D.tmp.</summary>
    public string ShareDir { get; }

    /// <summary>Folder for the harness's own scratch copies (inspections, frozen crash states, the database build).</summary>
    public string ScratchDir => Path.Combine(RootDir, "scratch");

    /// <summary>Full path of the shared database D (it exists once <see cref="CreateDatabase"/> has run).</summary>
    public string DatabasePath => Path.Combine(ShareDir, "dept.db");

    /// <summary>The one fake clock every simulated lock service reads. It starts at the real current time.</summary>
    public FakeTimeProvider Clock { get; } = new();

    /// <summary>The computers added so far.</summary>
    public IReadOnlyList<Computer> Computers => _computers;

    /// <summary>
    /// The computer whose action is running right now, or null between actions. Set by <see cref="Computer"/> around
    /// every action; read by the fault hook to decide whose network applies.
    /// </summary>
    public Computer? CurrentActor
    {
        get => _currentActor;
        set => _currentActor = value;
    }

    // ── Building the world ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a real TermPoint database at <see cref="DatabasePath"/> the way the application creates a new one
    /// (<see cref="NewDatabaseFile.CreateAsync"/>: schema built in a local file, then placed on the share), and adds
    /// the test-only <see cref="EditsTable"/> to it. The database is left in rollback-journal mode with no sidecar
    /// files, like a database that has only ever been written by saves.
    /// </summary>
    /// <returns>The path of D.</returns>
    /// <exception cref="InvalidOperationException">The database could not be created.</exception>
    public string CreateDatabase()
    {
        var buildDir = Path.Combine(ScratchDir, "build");
        Directory.CreateDirectory(buildDir);

        var result = NewDatabaseFile.CreateAsync(DatabasePath, buildDir).GetAwaiter().GetResult();
        if (!result.Succeeded)
            throw new InvalidOperationException($"The harness database could not be created: {result.Outcome}", result.Error);

        using (var conn = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode=DELETE;" +
                $"CREATE TABLE IF NOT EXISTS {EditsTable}(id TEXT PRIMARY KEY, actor TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return DatabasePath;
    }

    /// <summary>Adds a simulated computer and starts its first TermPoint process.</summary>
    /// <param name="name">The user's first name, lower case ("bob"); the machine is then named "BOB-PC" and edits "bob-1", "bob-2", ...</param>
    /// <returns>The new computer.</returns>
    public Computer AddComputer(string name)
    {
        var computer = new Computer(this, name);
        _computers.Add(computer);
        return computer;
    }

    // ── Events the world makes happen ────────────────────────────────────────

    /// <summary>Moves the shared fake clock forward (a heartbeat goes stale after minutes of fake time, instantly).</summary>
    /// <param name="by">How far to move the clock; must not be negative.</param>
    public void AdvanceTime(TimeSpan by) => Clock.Advance(by);

    /// <summary>
    /// Cuts a computer's connection to the share. From now on every operation that computer performs on the share
    /// times out (immediately, without waiting the real deadline); its local files keep working. Other computers are
    /// not affected.
    /// </summary>
    /// <param name="computer">The computer that loses the network.</param>
    public void NetworkDown(Computer computer) => computer.NetworkIsDown = true;

    /// <summary>Restores a computer's connection to the share.</summary>
    /// <param name="computer">The computer that gets the network back.</param>
    public void NetworkUp(Computer computer) => computer.NetworkIsDown = false;

    /// <summary>
    /// A colleague who is not one of the simulated computers changes the shared database: a row is inserted directly
    /// into D with a plain SQLite connection (no lock, no copy), so D's bytes change exactly as if somebody saved a
    /// version of D that this World knows nothing about. The row lands in <see cref="EditsTable"/> with the actor
    /// "colleague", so it shows up in <see cref="EditsInDatabase"/>.
    /// </summary>
    /// <param name="editId">The id of the row (for example "colleague-1").</param>
    public void ColleagueWritesDatabase(string editId)
    {
        using (var conn = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO {EditsTable}(id, actor) VALUES (@id, 'colleague')";
            cmd.Parameters.AddWithValue("@id", editId);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// <b>The process was killed between actions.</b> Models a computer whose TermPoint dies (crash, power cut, Task
    /// Manager) at a moment when no operation is in flight; operations that are cut in half arrive in step 9.3c.
    /// <list type="number">
    /// <item><description>The computer's local folder and the share folder are copied exactly as they are on disk
    /// right now (open files included, <c>-wal</c>, <c>-shm</c>, <c>.dirty</c>, <c>.lock</c> and <c>.tmp</c> too). The
    /// copy is taken FIRST because releasing a SQLite connection may checkpoint the WAL, which a killed process never
    /// gets to do.</description></item>
    /// <item><description>The computer's objects are thrown away WITHOUT any teardown call: no release of the lock, no
    /// save, no lifecycle close. Only the operating-system handles are released (the SQLite connection and the
    /// single-instance guard), so the files can be replaced.</description></item>
    /// <item><description>The frozen copies are put back over both folders, undoing whatever the handle release
    /// did.</description></item>
    /// <item><description>The process id is marked dead, so another process on the same machine sees "no such
    /// process".</description></item>
    /// </list>
    /// Afterwards <see cref="Computer.Relaunch"/> (or <see cref="Computer.OpenAsync"/>) starts a fresh TermPoint on the
    /// same folders with a new process id, as a real relaunch would.
    /// </summary>
    /// <param name="computer">The computer whose process is killed.</param>
    public void CrashComputer(Computer computer)
    {
        var frozen      = Path.Combine(ScratchDir, $"frozen_{++_freezeCounter}");
        var frozenShare = Path.Combine(frozen, "share");
        var frozenLocal = Path.Combine(frozen, "local");

        // (a) Freeze the disk as it is right now. The guard folder is skipped: its files are held with
        //     FileShare.None by the dying process, cannot be copied, and carry no data.
        CopyTree(ShareDir, frozenShare, skipDirectory: null);
        CopyTree(computer.LocalDir, frozenLocal, skipDirectory: computer.GuardDir);

        // (b) Throw the objects away; release only the OS handles.
        computer.AbandonProcess();

        // (c) Put the frozen disk back.
        ReplaceContents(ShareDir, frozenShare);
        ReplaceContents(computer.LocalDir, frozenLocal);
        Directory.CreateDirectory(computer.GuardDir);
        Directory.CreateDirectory(computer.WorkDir);

        try { Directory.Delete(frozen, recursive: true); } catch { /* best effort */ }
    }

    // ── Inspecting the world ─────────────────────────────────────────────────

    /// <summary>
    /// The ids of the traceable edits that are in the shared database D, in the order they were inserted. D is read
    /// from a copy, never in place (see <see cref="Inspector"/>).
    /// </summary>
    /// <returns>The edit ids in D.</returns>
    public List<string> EditsInDatabase() => EditsInFile(DatabasePath);

    /// <summary>
    /// The ids of the traceable edits in any database file: a computer's kept working file, an exported or recovered
    /// copy a walk names. Read from a copy.
    /// </summary>
    /// <param name="databaseFile">The file to look in.</param>
    /// <returns>The edit ids; empty when the file does not exist.</returns>
    public List<string> EditsInFile(string databaseFile) => Inspector.EditIds(databaseFile, ScratchDir);

    /// <summary>The names of the files on the share, sorted (D, its lock file, D.tmp, anything exported there).</summary>
    /// <returns>File names without folders.</returns>
    public List<string> ShareFiles()
        => Directory.GetFiles(ShareDir).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Fails if any computer's prompts were asked something its script did not describe (the lifecycle may swallow
    /// the exception the unexpected prompt throws, so every violation is also kept and checked here).
    /// </summary>
    /// <exception cref="Xunit.Sdk.XunitException">A computer saw an unexpected prompt, or left script steps unused.</exception>
    public void AssertAllScriptsConsumed()
    {
        foreach (var computer in _computers)
        {
            Xunit.Assert.Empty(computer.Prompts.Violations);
            Xunit.Assert.True(computer.Prompts.Remaining == 0,
                $"{computer.Name}: {computer.Prompts.Remaining} scripted prompt(s) were never asked.");
        }
    }

    // ── Simulated processes (used by Computer) ───────────────────────────────

    /// <summary>Starts a simulated process: gives it a fresh id and records it as running.</summary>
    /// <param name="startTimeUtc">Receives the process's start time (the fake clock's current time).</param>
    /// <returns>The new process id.</returns>
    internal int StartProcess(out DateTime startTimeUtc)
    {
        lock (_gate)
        {
            var pid = _nextProcessId++;
            startTimeUtc = Clock.GetUtcNow().UtcDateTime;
            _liveProcesses[pid] = startTimeUtc;
            return pid;
        }
    }

    /// <summary>Marks a simulated process as gone (closed or killed).</summary>
    /// <param name="processId">The id returned by <see cref="StartProcess"/>.</param>
    internal void EndProcess(int processId)
    {
        lock (_gate) _liveProcesses.Remove(processId);
    }

    /// <summary>
    /// The process lookup every simulated <see cref="LockIdentity"/> uses: it behaves like
    /// <see cref="System.Diagnostics.Process.GetProcessById(int)"/>, throwing <see cref="ArgumentException"/> for a
    /// process that is not running.
    /// </summary>
    /// <param name="processId">The process to find.</param>
    /// <returns>The running process (with the start time it was launched at).</returns>
    /// <exception cref="ArgumentException">No such process is running.</exception>
    internal ILockProcess LookupProcess(int processId)
    {
        lock (_gate)
        {
            if (_liveProcesses.TryGetValue(processId, out var start))
                return new SimulatedProcess(start);
        }
        throw new ArgumentException($"No simulated process with id {processId} is running.");
    }

    /// <summary>
    /// Puts the process-wide settings in the state every computer expects before it acts: it opens for editing (not as
    /// an observer) and autosave is on. <c>AppSettings.Current</c> is one object for all simulated computers.
    /// </summary>
    internal void ApplySettings()
    {
        AppSettings.Current.OpenInReaderMode = false;
        AppSettings.Current.AutoSaveEnabled  = true;
    }

    // ── Network faults ───────────────────────────────────────────────────────

    /// <summary>
    /// The network fault hook: while the CURRENT actor's network is down, an operation on the share times out.
    /// "On the share" means the path or the destination is under <see cref="ShareDir"/>, or the operation is one of the
    /// generic lock operations, which name no path (see 9.2b: share I/O inside the lock service is faulted through the
    /// wrapping <c>RunAsync</c> label: "lock acquire", "force lock acquire", "start reader polling", "enter reader
    /// mode", "restore lock acquire", "lock re-acquire after removal"). Everything else is answered "no fault".
    /// </summary>
    /// <param name="request">The operation being asked about.</param>
    /// <returns>A timeout for a share operation of a computer whose network is down; otherwise no fault.</returns>
    private NetworkFaultDecision DecideFault(NetworkFaultRequest request)
    {
        var actor = _currentActor;
        if (actor is null || !actor.NetworkIsDown) return NetworkFaultDecision.None;

        var onShare = IsUnderShare(request.Path) || IsUnderShare(request.DestinationPath) || IsGenericLockOperation(request);
        return onShare ? NetworkFaultDecision.TimeOut : NetworkFaultDecision.None;
    }

    /// <summary>True when <paramref name="path"/> is inside the share folder.</summary>
    /// <param name="path">A path, or null.</param>
    private bool IsUnderShare(string? path)
        => path is not null && path.StartsWith(ShareDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for a lock operation that names no path (its label says what it is): those are the lock service's
    /// share operations, wrapped in <c>RunAsync</c> by the checkout service.
    /// </summary>
    /// <param name="request">The operation.</param>
    private static bool IsGenericLockOperation(NetworkFaultRequest request)
        => request.Path is null
           && (request.Label.Contains("lock", StringComparison.OrdinalIgnoreCase)
               || request.Label.Contains("reader", StringComparison.OrdinalIgnoreCase));

    // ── Folder copying (crashes) ─────────────────────────────────────────────

    /// <summary>
    /// Copies a folder tree file by file with <see cref="FileShare.ReadWrite"/>, so files another handle has open
    /// (a live SQLite database and its WAL) are copied as they are on disk.
    /// </summary>
    /// <param name="source">The folder to copy.</param>
    /// <param name="destination">The folder to create (merged into if it exists).</param>
    /// <param name="skipDirectory">A sub-folder not to copy (compared by full path), or null.</param>
    private static void CopyTree(string source, string destination, string? skipDirectory)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            Inspector.CopyShared(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var sub in Directory.GetDirectories(source))
        {
            if (skipDirectory is not null && string.Equals(sub, skipDirectory, StringComparison.OrdinalIgnoreCase))
                continue;
            CopyTree(sub, Path.Combine(destination, Path.GetFileName(sub)), skipDirectory);
        }
    }

    /// <summary>
    /// Makes a folder hold exactly what a frozen copy holds: everything in it is deleted, then the copy is put in.
    /// </summary>
    /// <param name="target">The live folder.</param>
    /// <param name="frozen">The frozen copy to restore.</param>
    private static void ReplaceContents(string target, string frozen)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(target)) File.Delete(file);
        foreach (var dir in Directory.GetDirectories(target)) Directory.Delete(dir, recursive: true);
        CopyTree(frozen, target, skipDirectory: null);
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the fault hook, closes every computer's handles, restores the settings and deletes the temp folders
    /// (best effort).
    /// </summary>
    public void Dispose()
    {
        NetworkFaults.Decide = null;
        CrashPoints.Hook     = null;
        foreach (var computer in _computers) computer.Dispose();
        SqliteConnection.ClearAllPools();
        _settingsGuard.Dispose();
        try { Directory.Delete(RootDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A simulated running process, as <see cref="LockIdentity"/> lookups see it: just its start time.</summary>
    private sealed class SimulatedProcess : ILockProcess
    {
        /// <summary>Creates the process view.</summary>
        /// <param name="startTimeUtc">When the process was launched.</param>
        public SimulatedProcess(DateTime startTimeUtc) => StartTimeUtc = startTimeUtc;

        /// <inheritdoc/>
        public DateTime StartTimeUtc { get; }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
