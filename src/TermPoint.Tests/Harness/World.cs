using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit.Abstractions;

namespace TermPoint.Tests.Harness;

/// <summary>
/// The simulated world of the multi-computer harness (spec item 9.3, steps 9.3a and 9.3b): one temp folder playing the
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
/// <see cref="NetworkFaults.Decide"/>; local files keep working.</description></item>
/// </list>
///
/// <para><b>The funnel (step 9.3b).</b> Every computer action and every world event goes through <see cref="Do{T}"/> /
/// <see cref="DoAsync{T}"/>: it appends a line to the story, runs the action, then (for a top-level step) reads the
/// <see cref="Ledger"/> and runs every invariant. A violation of a finding the walk declared
/// (<see cref="ExpectFindings"/>) is recorded and the walk goes on; any other violation throws a
/// <see cref="HarnessViolationException"/> whose message is the printed story. A step started from inside another
/// step (a dialog's script advancing the clock, say) only adds its story line: the invariants run when the outer step
/// ends.</para>
///
/// <para><b>Power cuts (step 9.3c-2a).</b> The one exception to "the hook only observes" is
/// <see cref="RunWithPowerCutAsync"/>: while it runs an operation, the hook cuts the armed computer's power at the armed
/// crash point (freezes the disk, marks the process dead, throws <see cref="PowerCutException"/>), and throws again at every
/// later crash point and network operation of that process. Which computer reached a point is read from an
/// <c>AsyncLocal</c> that follows the flow of control onto other threads.</para>
///
/// <para><b>Observers.</b> <see cref="CrashPoints.Hook"/> is otherwise an OBSERVER: it never throws on its own. At
/// <c>Save.AfterTmpVerify</c> (and <c>Restore.AfterTmpVerified</c>) it checks invariant I4 (the lock file holds the
/// acting computer's session and the database is the version that computer last saw); a walk can add code at a point
/// with <see cref="AtCrashPoint"/>. Prompts and banners report to <see cref="ObservePrompt"/> /
/// <see cref="ObserveBanner"/>, which feed the story, the saved-copy list and the promise tracker (invariant I5).</para>
///
/// <para>Static state is shared by everything in the process (the fault hook, the crash-point hook,
/// <c>AppSettings.Current</c>), so a World must not be used by two tests at once. The test assembly already runs its
/// tests one at a time.</para>
/// </summary>
internal sealed class World : IDisposable
{
    /// <summary>Name of the test-only table every traceable edit goes into (created by <see cref="CreateDatabase"/>).</summary>
    public const string EditsTable = "_harness_edits";

    /// <summary>Guards <see cref="_liveProcesses"/> and the lists the observers add to from any thread.</summary>
    private readonly object _gate = new();

    /// <summary>Process id to start time (UTC) for every simulated process that is currently running.</summary>
    private readonly Dictionary<int, DateTime> _liveProcesses = new();

    /// <summary>The computers added so far, in order.</summary>
    private readonly List<Computer> _computers = new();

    /// <summary>Protects the settings file (and restores <c>AppSettings.Current</c>) when the World is disposed.</summary>
    private readonly SettingsFileGuard _settingsGuard = new();

    /// <summary>The test's output, where the story is always written; null for none.</summary>
    private readonly ITestOutputHelper? _output;

    /// <summary>The chronological narrative.</summary>
    private readonly Story _story = new();

    /// <summary>I1, I2 and I3.</summary>
    private readonly InvariantChecker _invariants = new();

    /// <summary>I5.</summary>
    private readonly PromiseTracker _promises = new();

    /// <summary>Every violation recorded so far (each once), with its finding id.</summary>
    private readonly List<Violation> _violations = new();

    /// <summary>Keys of the violations already recorded, so one that stays true is reported once.</summary>
    private readonly HashSet<string> _reportedKeys = new();

    /// <summary>The finding ids the walk declared it expects.</summary>
    private readonly HashSet<string> _expected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Violations found by observers during a step (I4, the F7 rule, unclassified promises), raised when the step ends.</summary>
    private readonly List<Violation> _pending = new();

    /// <summary>I4 observations made at <c>Restore.AfterTmpVerified</c>: they count only if the restore then really lands (<see cref="NoteRestoreLanded"/>).</summary>
    private readonly List<Violation> _restoreSuspects = new();

    /// <summary>The dialogs each computer was asked during the current step, by computer name.</summary>
    private readonly Dictionary<string, List<PromptCall>> _stepPrompts = new();

    /// <summary>The computers that completed an open during the current step.</summary>
    private readonly HashSet<Computer> _openedThisStep = new();

    /// <summary>Extra code walks asked to run at a crash point, by point name; each runs once.</summary>
    private readonly Dictionary<string, List<(Action Action, string StoryLine)>> _crashPointActions = new();

    /// <summary>Extra code that runs EVERY time execution passes a crash point (a planted bug), by point name.</summary>
    private readonly Dictionary<string, List<Action>> _repeatingCrashPointActions = new();

    /// <summary>How many "Save a Copy" paths have been handed out (numbers "copy-1.db", "copy-2.db", ...).</summary>
    private int _savedCopyCounter;

    /// <summary>How many times the network fault hook has been asked about an operation (a sign that work is still running on the share).</summary>
    private int _shareActivity;

    /// <summary>Every file a "Save a Copy" picker answered with.</summary>
    private readonly List<string> _savedCopyPaths = new();

    /// <summary>Edit ids covered by a confirmed discard.</summary>
    private readonly HashSet<string> _confirmedDiscards = new();

    /// <summary>Next process id handed out; simulated ids are unique across the whole World.</summary>
    private int _nextProcessId = 5000;

    /// <summary>Counter that names the frozen copies a crash takes.</summary>
    private int _freezeCounter;

    /// <summary>The computer acting right now (read by the fault hook from any thread).</summary>
    private volatile Computer? _currentActor;

    /// <summary>
    /// The process whose action is running in the current flow of control (set by <see cref="EnterAction"/>). Unlike
    /// <see cref="_currentActor"/> it follows the code across threads and awaits, including the background tasks an action
    /// starts, so a crash point or a network operation is attributed to the right computer (and to the right
    /// incarnation of it) wherever the production code runs it.
    /// </summary>
    private readonly AsyncLocal<ProcessLife?> _acting = new();

    /// <summary>The power cut that is armed for the operation running now, or null (see <see cref="RunWithPowerCutAsync"/>).</summary>
    private ArmedPowerCut? _armedCut;

    /// <summary>The pause that is armed for the operation running now, or null (see <see cref="RunWithPauseAsync"/>).</summary>
    private ArmedPause? _armedPause;

    /// <summary>
    /// The pause whose held operation the current flow of control belongs to, or null. Set on the held operation's own task, so it
    /// follows every task and thread the operation starts (and not the event that runs meanwhile, which is never held).
    /// </summary>
    private readonly AsyncLocal<ArmedPause?> _pausedFlow = new();

    /// <summary>
    /// The lost-lock demotion the current flow of control is in the middle of, with the database it started on (invariant I6, clause b):
    /// set at the demotion's first <c>Demote.*</c> point, cleared at its last. Like <see cref="_acting"/> it follows the code across awaits.
    /// </summary>
    private readonly AsyncLocal<DemotionRecord?> _demotion = new();

    /// <summary>What every pause did so far, in order (for the report).</summary>
    private readonly List<PauseResult> _pauseResults = new();

    /// <summary>The edits a computer applied while its own operation was held in the edit-loss window, from a save's snapshot to the cleanup of the working copy (finding F2; <see cref="PausePoints.EditLossWindow"/>).</summary>
    private readonly HashSet<string> _editsAppliedDuringHeldSave = new();

    /// <summary>The edits another computer committed to the shared database while a save was held before its rename, by the step they were committed in (finding F12).</summary>
    private readonly Dictionary<string, int> _commitsDuringHeldSave = new();

    /// <summary>Every crash point reached so far, in order, with the name of the computer that reached it ("?" when none could be told).</summary>
    private readonly List<(string Computer, string Point)> _hitLog = new();

    /// <summary>The crash points at which a power cut fired, in order ("bob@Save.AfterRename").</summary>
    private readonly List<string> _firedCuts = new();

    /// <summary>Number of top-level funnel steps started so far; the number of the current step.</summary>
    private int _step;

    /// <summary>How many funnel calls are running inside each other (0 between steps). Changed with <see cref="Interlocked"/>: a pause runs two actions at once.</summary>
    private int _depth;

    /// <summary>The ledger at the end of the previous step: "how things were before this step".</summary>
    private Ledger _last = Ledger.Empty;

    /// <summary>True once <see cref="Finish"/> has run.</summary>
    private bool _finished;

    /// <summary>True once the story has been written to the test output.</summary>
    private bool _outputWritten;

    /// <summary>Creates the folders, installs the network fault hook and the crash-point observer, and puts the settings in a known state.</summary>
    /// <param name="output">The test's output helper, where the story is always written; null for none.</param>
    /// <param name="title">What the story is of (shown in its heading), or empty.</param>
    public World(ITestOutputHelper? output = null, string title = "")
    {
        _output = output;
        Title   = title;

        var id = Guid.NewGuid().ToString("N");
        RootDir  = Path.Combine(Path.GetTempPath(), $"harness_{id}");
        ShareDir = Path.Combine(RootDir, "share");
        Directory.CreateDirectory(ShareDir);
        Directory.CreateDirectory(ScratchDir);

        CrashPoints.Hook     = OnCrashPoint;
        NetworkFaults.Decide = DecideFault;
        ApplySettings();
    }

    /// <summary>What the story is of; shown in its heading.</summary>
    public string Title { get; }

    /// <summary>The temp folder that holds everything the World makes (share, computers, scratch copies).</summary>
    public string RootDir { get; }

    /// <summary>The folder playing the network share: it holds D, D's lock file and D.tmp.</summary>
    public string ShareDir { get; }

    /// <summary>Folder for the harness's own scratch copies (inspections, frozen crash states, the database build).</summary>
    public string ScratchDir => Path.Combine(RootDir, "scratch");

    /// <summary>Full path of the shared database D (it exists once <see cref="CreateDatabase"/> has run).</summary>
    public string DatabasePath => Path.Combine(ShareDir, "dept.db");

    /// <summary>Full path of D's lock file.</summary>
    public string LockFilePath => Path.ChangeExtension(DatabasePath, ".lock");

    /// <summary>Full path of the second shared database ("other.db") the generated runs switch to and from (made by <see cref="EnsureOtherDatabase"/> when something first needs it).</summary>
    public string OtherDatabasePath => Path.Combine(ShareDir, "other.db");

    /// <summary>Full path of the second database's lock file.</summary>
    public string OtherLockFilePath => Path.ChangeExtension(OtherDatabasePath, ".lock");

    /// <summary>
    /// The policy that answers a dialog nobody scripted, for every computer of this world (set by a generated run;
    /// null for a hand-written walk, whose unexpected dialogs fail the test). Each computer hands it to its
    /// <see cref="ScriptedPrompts"/> whenever a process is launched.
    /// </summary>
    public Func<Computer, PromptCall, PromptAnswer>? PromptPolicy { get; set; }

    /// <summary>
    /// A number that grows whenever work touches the share (every operation asks the network fault hook) or a computer
    /// logs something. A caller waiting for background work to finish watches it stand still.
    /// </summary>
    public int Activity => Volatile.Read(ref _shareActivity) + _computers.Sum(c => c.LogActivity);

    /// <summary>The one fake clock every simulated lock service reads. It starts at the real current time.</summary>
    public FakeTimeProvider Clock { get; } = new();

    /// <summary>The computers added so far.</summary>
    public IReadOnlyList<Computer> Computers => _computers;

    /// <summary>
    /// The computer whose action is running right now, or null between actions. Set by <see cref="Computer"/> around
    /// every action; read by the fault hook to decide whose network applies, and by the I4 observer to know who is
    /// saving.
    /// </summary>
    public Computer? CurrentActor
    {
        get => _currentActor;
        set => _currentActor = value;
    }

    /// <summary>
    /// The computer whose flow of control is running the code that asks: the one whose action started it, wherever the code hops
    /// (threads, tasks, timers, awaits). Unlike <see cref="CurrentActor"/> it is right while TWO actions overlap (a pause runs an event
    /// of another computer, or of the same one, while an operation is held), so any code that runs inside a crash-point hook and needs
    /// to know "who is doing this" must use it. Null when the caller belongs to no computer's action.
    /// </summary>
    public Computer? ActingComputer => ActingLife()?.Owner;

    /// <summary>
    /// Called for every crash point a computer's flow reaches (with its position in the hit log, the computer and the point), on the
    /// thread that reached it, before anything else happens there. The pause sweep installs it in a dry run to ask, at each point, which
    /// events would be possible in that very state. Null for none.
    /// </summary>
    public Action<int, Computer?, string>? CrashPointSurvey { get; set; }

    /// <summary>The ledger at the end of the previous step (what things looked like before the current step).</summary>
    public Ledger LastLedger => _last;

    /// <summary>What every pause did so far, in order.</summary>
    public IReadOnlyList<PauseResult> PauseResults
    {
        get { lock (_gate) return _pauseResults.ToList(); }
    }

    /// <summary>The violations recorded so far, each once, with its finding id set.</summary>
    public IReadOnlyList<Violation> Violations
    {
        get { lock (_gate) return _violations.ToList(); }
    }

    /// <summary>The files a "Save a Copy" picker answered with, in order (recorded automatically when the prompt is served).</summary>
    public IReadOnlyList<string> SavedCopyPaths
    {
        get { lock (_gate) return _savedCopyPaths.ToList(); }
    }

    /// <summary>Edit ids covered by a confirmed discard (the conflict banner's "Discard Changes" after its confirmation).</summary>
    public ISet<string> ConfirmedDiscards => _confirmedDiscards;

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
        Do<int>(null, $"The shared database {Path.GetFileName(DatabasePath)} was created", () =>
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
            return 0;
        });
        return DatabasePath;
    }

    /// <summary>
    /// Creates D by copying a template database that was built once per test process the way
    /// <see cref="CreateDatabase"/> builds one (same schema, <see cref="EditsTable"/> added, rollback-journal mode, no
    /// sidecar files). The thousands of worlds a generated exploration makes would otherwise each pay for building the
    /// schema. The second database is made only when something needs it (<see cref="EnsureOtherDatabase"/>).
    /// </summary>
    /// <returns>The path of D.</returns>
    public string CreateDatabaseFromTemplate()
    {
        Do<int>(null, $"The shared database {Path.GetFileName(DatabasePath)} was created", () =>
        {
            File.Copy(HarnessDatabaseTemplate.Path, DatabasePath);
            return 0;
        });
        return DatabasePath;
    }

    /// <summary>
    /// Makes sure the second database ("other.db") exists on the share (a copy of the template, made the first time a
    /// computer is about to switch to it). Not a story step: the second database holds no traceable edits.
    /// </summary>
    /// <returns>The path of the second database.</returns>
    public string EnsureOtherDatabase()
    {
        if (!File.Exists(OtherDatabasePath))
            File.Copy(HarnessDatabaseTemplate.Path, OtherDatabasePath);
        return OtherDatabasePath;
    }

    /// <summary>
    /// Deletes D's lock file, as antivirus, a cloud-sync client or a person tidying the folder might (a file that
    /// is simply gone, not taken over by anyone).
    /// </summary>
    public void DeleteLockFile()
        => Do(null, "Somebody deleted the lock file from the share", () =>
        {
            if (File.Exists(LockFilePath)) File.Delete(LockFilePath);
        });

    /// <summary>True when D's lock file exists.</summary>
    public bool LockFileExists => File.Exists(LockFilePath);

    /// <summary>
    /// The session id that the lock file of a database names (invariant I6 compares it with a computer's own session).
    /// </summary>
    /// <param name="databasePath">Full path of the database whose lock file (same name, <c>.lock</c>) is read.</param>
    /// <returns>The session id; null when there is no lock file or it cannot be read.</returns>
    public string? LockFileSession(string databasePath)
    {
        try
        {
            var lockPath = Path.ChangeExtension(databasePath, ".lock");
            return File.Exists(lockPath) ? JsonSerializer.Deserialize<LockFileData>(ReadSharedText(lockPath))?.SessionGuid : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Hands out a fresh, never-used path for a "Save a Copy" picker to answer with, under the world's scratch folder
    /// ("copy-1.db", "copy-2.db", ...), so a generated run needs no scripted export path and its story names the
    /// same files every time it is replayed.
    /// </summary>
    /// <returns>A full path whose folder exists and whose file does not.</returns>
    public string NextSavedCopyPath()
    {
        var dir = Path.Combine(ScratchDir, "exports");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"copy-{++_savedCopyCounter}.db");
    }

    /// <summary>How many backup files have been handed out ("backup-1.db", ...).</summary>
    private int _backupCounter;

    /// <summary>
    /// Hands out a fresh path under the world's scratch folder for a backup file a "restore from a backup" reads, so a
    /// generated run needs no scripted path and its story names the same files every time it is replayed.
    /// </summary>
    /// <returns>A full path whose folder exists and whose file does not.</returns>
    public string NextBackupPath()
    {
        var dir = Path.Combine(ScratchDir, "backups");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"backup-{++_backupCounter}.db");
    }

    /// <summary>SHA-256 of D's bytes now, in the hex form the checkout service uses for its fingerprints (null when D cannot be read).</summary>
    /// <returns>The fingerprint, or null.</returns>
    public string? CurrentDatabaseHash()
    {
        try { return HashFile(DatabasePath); }
        catch (Exception) { return null; }
    }

    /// <summary>True when a "will offer to restore" promise made to the computer is still waiting for its next edit-access open.</summary>
    /// <param name="computer">The computer.</param>
    public bool HasPendingPromise(Computer computer) => _promises.HasPendingOffer(computer);

    /// <summary>One line per promise still undecided, as the story's closing notes print them.</summary>
    /// <returns>The lines; empty when nothing is pending.</returns>
    public IReadOnlyList<string> PendingPromiseLines() => _promises.PendingLines();

    /// <summary>
    /// A short code for the databases a computer has a "will offer to restore" promise pending for: "" none, "d" the
    /// shared database D, "o" the second database, "u" a promise whose database was not known (any combination, in that
    /// order). The exhaustive search's state key carries it, because a promise about D and one about the other database
    /// are judged at different opens.
    /// </summary>
    /// <param name="computer">The computer.</param>
    /// <returns>The code; an empty string when nothing is pending.</returns>
    public string PendingPromiseCode(Computer computer)
    {
        var pending = _promises.PendingOfferDatabases(computer);
        var d = CheckoutService.NormalizeSourcePath(DatabasePath);
        var o = CheckoutService.NormalizeSourcePath(OtherDatabasePath);
        return (pending.Contains(d) ? "d" : "") + (pending.Contains(o) ? "o" : "") + (pending.Contains("") ? "u" : "");
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

    // ── The funnel ───────────────────────────────────────────────────────────

    /// <summary>
    /// The one place every action and event goes through (synchronous form): appends <paramref name="storyLine"/> to the
    /// story, runs <paramref name="body"/>, and, when this is a top-level step, runs every invariant.
    /// </summary>
    /// <typeparam name="T">The body's result type.</typeparam>
    /// <param name="actor">The computer acting, or null for a world event.</param>
    /// <param name="storyLine">The sentence for the story ("Bob made edit bob-1").</param>
    /// <param name="body">What to do.</param>
    /// <param name="describe">Optional: turns the result into the tail of the story line ("... — Success").</param>
    /// <returns>The body's result.</returns>
    /// <exception cref="HarnessViolationException">An invariant was broken that the walk did not declare.</exception>
    public T Do<T>(Computer? actor, string storyLine, Func<T> body, Func<T, string>? describe = null)
    {
        var nested = Volatile.Read(ref _depth) > 0;
        if (!nested) BeginStep();
        var entry = _story.Add(_step, nested ? 1 : 0, storyLine);

        Interlocked.Increment(ref _depth);
        T result;
        try { result = body(); }
        catch (Exception ex)
        {
            entry.Text += DescribeThrown(ex);
            throw;
        }
        finally { Interlocked.Decrement(ref _depth); }

        if (actor is { PowerIsOut: true }) entry.Text += " — it was cut short by the power cut";
        else if (describe is not null) entry.Text += " — " + describe(result);
        if (!nested) EndStep();
        return result;
    }

    /// <summary>The funnel for an action with no result.</summary>
    /// <param name="actor">The computer acting, or null for a world event.</param>
    /// <param name="storyLine">The sentence for the story.</param>
    /// <param name="body">What to do.</param>
    public void Do(Computer? actor, string storyLine, Action body)
        => Do<int>(actor, storyLine, () => { body(); return 0; });

    /// <summary>
    /// The funnel for an asynchronous action (see <see cref="Do{T}"/>).
    /// </summary>
    /// <typeparam name="T">The body's result type.</typeparam>
    /// <param name="actor">The computer acting, or null for a world event.</param>
    /// <param name="storyLine">The sentence for the story.</param>
    /// <param name="body">What to do.</param>
    /// <param name="describe">Optional: turns the result into the tail of the story line.</param>
    /// <returns>The body's result.</returns>
    /// <exception cref="HarnessViolationException">An invariant was broken that the walk did not declare.</exception>
    public async Task<T> DoAsync<T>(Computer? actor, string storyLine, Func<Task<T>> body, Func<T, string>? describe = null)
    {
        var nested = Volatile.Read(ref _depth) > 0;
        if (!nested) BeginStep();
        var entry = _story.Add(_step, nested ? 1 : 0, storyLine);

        Interlocked.Increment(ref _depth);
        T result;
        try { result = await body(); }
        catch (Exception ex)
        {
            entry.Text += DescribeThrown(ex);
            throw;
        }
        finally { Interlocked.Decrement(ref _depth); }

        if (actor is { PowerIsOut: true }) entry.Text += " — it was cut short by the power cut";
        else if (describe is not null) entry.Text += " — " + describe(result);
        if (!nested) EndStep();
        return result;
    }

    /// <summary>The funnel for an asynchronous action with no result.</summary>
    /// <param name="actor">The computer acting, or null for a world event.</param>
    /// <param name="storyLine">The sentence for the story.</param>
    /// <param name="body">What to do.</param>
    /// <returns>A task that completes when the step, and its invariants, are done.</returns>
    public Task DoAsync(Computer? actor, string storyLine, Func<Task> body)
        => DoAsync<int>(actor, storyLine, async () => { await body(); return 0; });

    /// <summary>The tail of a story line for an action that ended with an exception: a power cut reads as such, anything else by type.</summary>
    /// <param name="ex">The exception.</param>
    private static string DescribeThrown(Exception ex)
        => ex is PowerCutException cut
            ? $" — it was stopped there by the power cut ({cut.Where})"
            : $" — it threw {ex.GetType().Name}";

    /// <summary>Starts a top-level step: forgets the previous step's dialogs and opens.</summary>
    private void BeginStep()
    {
        _step++;
        lock (_gate)
        {
            _stepPrompts.Clear();
            _openedThisStep.Clear();
            _restoreSuspects.Clear();
        }
    }

    /// <summary>
    /// Ends a top-level step: reads the ledger, runs the invariants and the promise checks, records the violations and
    /// throws if one was not declared.
    /// </summary>
    /// <exception cref="HarnessViolationException">An undeclared violation was found.</exception>
    private void EndStep()
    {
        var now = Ledger.Capture(this);

        var found = new List<Violation>();
        lock (_gate)
        {
            found.AddRange(_pending);
            _pending.Clear();
        }

        Computer[] opened;
        lock (_gate) opened = _openedThisStep.ToArray();

        found.AddRange(_invariants.Check(this, now, _step));
        found.AddRange(_promises.Evaluate(this, _last, now, opened, _step));
        _last = now;

        Record(found);
    }

    /// <summary>
    /// Records the violations of a step (each key once), gives each its finding id, and throws when one of them is not a
    /// declared finding.
    /// </summary>
    /// <param name="found">The breaches found at the end of the step.</param>
    /// <exception cref="HarnessViolationException">A recorded violation's finding was not declared.</exception>
    private void Record(IEnumerable<Violation> found)
    {
        var undeclared = false;
        lock (_gate)
        {
            foreach (var violation in found)
            {
                if (!_reportedKeys.Add(violation.Key)) continue;
                violation.FindingId = Findings.Classify(violation);
                _violations.Add(violation);
                if (!_expected.Contains(violation.FindingId)) undeclared = true;
            }
        }

        if (undeclared) throw Fail(Array.Empty<string>());
    }

    /// <summary>Builds the exception that carries the printed story, writing the story to the test output first.</summary>
    /// <param name="notes">Extra closing lines.</param>
    private HarnessViolationException Fail(IEnumerable<string> notes)
    {
        var text = PrintStory(notes);
        WriteOutput(text);
        return new HarnessViolationException(text);
    }

    // ── Declaring and checking findings ──────────────────────────────────────

    /// <summary>
    /// Declares the findings this walk is expected to produce ("F6", "F7", "new", ...). A violation of a declared
    /// finding is recorded and the walk goes on; any other violation stops the walk at once. At <see cref="Finish"/> the
    /// set that occurred must EQUAL the declared set.
    /// </summary>
    /// <param name="findingIds">The finding ids; call with none (or never) for a walk that must produce no violation.</param>
    public void ExpectFindings(params string[] findingIds)
    {
        _expected.Clear();
        foreach (var id in findingIds) _expected.Add(id);
    }

    /// <summary>
    /// Ends the walk: prints the story to the test output, lists the promises still pending, and checks that the set of
    /// findings that occurred equals the declared set.
    /// </summary>
    /// <exception cref="HarnessViolationException">A declared finding never occurred (or an undeclared one did; that normally stopped the walk earlier).</exception>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;

        var occurred = Violations.Select(v => v.FindingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>
        {
            $"Declared findings: {Describe(_expected)}",
            $"Findings that occurred: {Describe(occurred)}"
        };
        notes.AddRange(_promises.PendingLines());

        var text = PrintStory(notes);
        WriteOutput(text);
        if (!occurred.SetEquals(_expected))
            throw new HarnessViolationException(text);
    }

    /// <summary>Formats a set of finding ids for a note ("F6, F7" or "none").</summary>
    /// <param name="ids">The ids.</param>
    private static string Describe(IEnumerable<string> ids)
    {
        var list = ids.OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    /// <summary>Renders the story with its edit locations and violations.</summary>
    /// <param name="notes">Extra closing lines.</param>
    /// <returns>The printed story.</returns>
    public string PrintStory(IEnumerable<string> notes)
    {
        var ledger = _last;

        var editIds = new List<string>();
        foreach (var computer in _computers) editIds.AddRange(computer.AppliedEdits);
        if (ledger.Shared is not null) editIds.AddRange(ledger.Shared);
        foreach (var edits in ledger.Live.Values) editIds.AddRange(edits);
        foreach (var kept in ledger.Kept.Values) editIds.AddRange(kept.Edits);
        foreach (var edits in ledger.SavedCopies.Values) editIds.AddRange(edits);
        foreach (var edits in ledger.Archives.Values) editIds.AddRange(edits);

        var places = editIds.Distinct().Select(e => $"{e}: {string.Join("; ", ledger.PlacesOf(e))}");
        var violations = Violations.Select(v => $"{v.Invariant} / {v.FindingId} (step {v.Step}): {v.Reason}");
        return _story.Render(Title, places, violations, notes);
    }

    /// <summary>Writes the story to the test output, once.</summary>
    /// <param name="text">The printed story.</param>
    private void WriteOutput(string text)
    {
        if (_outputWritten) return;
        _outputWritten = true;
        try { _output?.WriteLine(text); } catch (InvalidOperationException) { /* the test already finished */ }
    }

    // ── Events the world makes happen ────────────────────────────────────────

    /// <summary>Moves the shared fake clock forward (a heartbeat goes stale after minutes of fake time, instantly).</summary>
    /// <param name="by">How far to move the clock; must not be negative.</param>
    public void AdvanceTime(TimeSpan by)
        => Do(null, $"{DescribeDuration(by)} passed", () => Clock.Advance(by));

    /// <summary>Formats a duration for the story ("5 minutes", "31 seconds", "1 hour 30 minutes").</summary>
    /// <param name="by">The duration.</param>
    private static string DescribeDuration(TimeSpan by)
    {
        if (by.TotalSeconds < 60) return $"{(int)by.TotalSeconds} second{(((int)by.TotalSeconds) == 1 ? "" : "s")}";
        if (by.TotalMinutes < 60) return $"{(int)by.TotalMinutes} minute{(((int)by.TotalMinutes) == 1 ? "" : "s")}";
        var hours = (int)by.TotalHours;
        var minutes = by.Minutes;
        return minutes == 0 ? $"{hours} hour{(hours == 1 ? "" : "s")}" : $"{hours} hour{(hours == 1 ? "" : "s")} {minutes} minutes";
    }

    /// <summary>
    /// Cuts a computer's connection to the share. From now on every operation that computer performs on the share
    /// times out (immediately, without waiting the real deadline); its local files keep working. Other computers are
    /// not affected.
    /// </summary>
    /// <param name="computer">The computer that loses the network.</param>
    public void NetworkDown(Computer computer)
        => Do(null, $"{computer.DisplayName}'s network went down", () => { computer.NetworkIsDown = true; });

    /// <summary>Restores a computer's connection to the share.</summary>
    /// <param name="computer">The computer that gets the network back.</param>
    public void NetworkUp(Computer computer)
        => Do(null, $"{computer.DisplayName}'s network came back", () => { computer.NetworkIsDown = false; });

    /// <summary>
    /// A colleague who is not one of the simulated computers changes the shared database: a row is inserted directly
    /// into D with a plain SQLite connection (no lock, no copy), so D's bytes change exactly as if somebody saved a
    /// version of D that this World knows nothing about. The row lands in <see cref="EditsTable"/> with the actor
    /// "colleague", so it shows up in <see cref="EditsInDatabase"/>.
    /// </summary>
    /// <param name="editId">The id of the row (for example "colleague-1").</param>
    public void ColleagueWritesDatabase(string editId)
        => Do(null, $"A 1.2.2 colleague changed the database (edit {editId})", () =>
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
        });

    /// <summary>
    /// Takes a snapshot of the shared database D's bytes (D with its sidecars is not live: no computer should have it
    /// open). For <see cref="ItRestoresOldFile"/>, or for a walk that wants an older D to copy back behind
    /// TermPoint's back.
    /// </summary>
    /// <returns>D's bytes now.</returns>
    public byte[] TakeSnapshot() => File.ReadAllBytes(DatabasePath);

    /// <summary>
    /// <b>IT restores an old file:</b> an earlier snapshot of D is copied over D outside TermPoint (a backup tool, a
    /// person dragging a file). This is an allowed reset for invariant I1: edits that were in D and are not in the old
    /// file are expected to be gone, so the I1 baseline starts again from the old file.
    /// </summary>
    /// <param name="snapshot">The bytes from <see cref="TakeSnapshot"/>.</param>
    public void ItRestoresOldFile(byte[] snapshot)
        => Do(null, $"IT restored an older copy of {Path.GetFileName(DatabasePath)} over the shared database", () =>
        {
            var before = TryEditsInFile(DatabasePath) ?? new List<string>();
            File.WriteAllBytes(DatabasePath, snapshot);
            ExcuseEditsGoneFromDatabase(before);
            _invariants.ResetBaselineAtNextCheck();
        });

    /// <summary>
    /// Writes a lock file for another session onto the share, as if that person had just taken the lock. The lock's
    /// session id is a fresh GUID, so it is never any simulated computer's. Used by walks that need "the lock changed
    /// hands" at a precise instant (see <see cref="AtCrashPoint"/>).
    /// </summary>
    /// <param name="userName">The new holder's user name ("alice").</param>
    /// <param name="machineName">The new holder's machine name ("ALICE-PC").</param>
    public void PlaceForeignLock(string userName, string machineName)
        => Do(null, $"{char.ToUpperInvariant(userName[0])}{userName[1..]}'s lock appeared on the share", () =>
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var data = new LockFileData
            {
                Username = userName, Machine = machineName, SessionGuid = Guid.NewGuid().ToString(),
                Acquired = now, Heartbeat = now, ProcessId = 1, ProcessStartTimeUtc = now
            };
            File.WriteAllText(LockFilePath, JsonSerializer.Serialize(data));
        });

    /// <summary>
    /// <b>The process was killed between actions.</b> Models a computer whose TermPoint dies (crash, power cut, Task
    /// Manager) at a moment when no operation is in flight; an operation cut in half is <see cref="RunWithPowerCutAsync"/> (step 9.3c-2a).
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
        => Do(null, $"{computer.DisplayName}'s computer crashed", () => CrashCore(computer));

    /// <summary>The body of <see cref="CrashComputer"/> (see there).</summary>
    /// <param name="computer">The computer whose process is killed.</param>
    private void CrashCore(Computer computer)
    {
        // (a) Freeze the disk as it is right now.
        var frozen = FreezeDisk(computer);

        // (b) Throw the objects away; release only the OS handles.
        computer.AbandonProcess();

        // (c) Put the frozen disk back.
        RestoreFrozenDisk(computer, frozen);
    }

    /// <summary>
    /// Copies the share folder and a computer's local folder exactly as they are on disk right now. The instance-guard
    /// folder is skipped: its files are held with <see cref="FileShare.None"/> by the dying process, cannot be copied, and
    /// carry no data. Safe to call from any thread, while the process still has its files open.
    /// </summary>
    /// <param name="computer">The computer whose local folder is copied.</param>
    /// <returns>The frozen copies.</returns>
    private FrozenDisk FreezeDisk(Computer computer)
    {
        var root = Path.Combine(ScratchDir, $"frozen_{Interlocked.Increment(ref _freezeCounter)}");
        var frozen = new FrozenDisk(root, Path.Combine(root, "share"), Path.Combine(root, "local"), Path.Combine(root, "exports"));
        CopyTree(ShareDir, frozen.Share, skipDirectory: null);
        CopyTree(computer.LocalDir, frozen.Local, skipDirectory: computer.GuardDir);

        // Where a "Save a Copy" file goes is part of the disk too: a copy that was half placed at the cut stays half placed.
        var exports = ExportsDir;
        if (Directory.Exists(exports)) CopyTree(exports, frozen.Exports, skipDirectory: null);
        return frozen;
    }

    /// <summary>The folder the generated runs' "Save a Copy" pickers answer with files in.</summary>
    private string ExportsDir => Path.Combine(ScratchDir, "exports");

    /// <summary>
    /// Puts a frozen disk back over the share folder and the computer's local folder, then removes the frozen copies. The
    /// computer's process must already be abandoned (its handles released), or its files cannot be replaced.
    /// </summary>
    /// <param name="computer">The computer whose local folder is replaced.</param>
    /// <param name="frozen">The copies made by <see cref="FreezeDisk"/>.</param>
    private void RestoreFrozenDisk(Computer computer, FrozenDisk frozen)
    {
        ReplaceContents(ShareDir, frozen.Share);
        ReplaceContents(computer.LocalDir, frozen.Local);
        Directory.CreateDirectory(computer.GuardDir);
        Directory.CreateDirectory(computer.WorkDir);
        if (Directory.Exists(frozen.Exports)) ReplaceContents(ExportsDir, frozen.Exports);

        try { Directory.Delete(frozen.Root, recursive: true); } catch { /* best effort */ }
    }

    // ── Cutting the power in the middle of an operation (step 9.3c-2a) ───────

    /// <summary>
    /// Runs one operation of a computer and cuts its power the <paramref name="occurrence"/>-th time it reaches the named
    /// crash point. At that instant the share and the computer's local folder are frozen as they are on disk, the computer
    /// is marked dead (every later crash-point hit and network operation of the process throws
    /// <see cref="PowerCutException"/> at once, because production code catches exceptions and may carry on), and the
    /// operation is stopped. When control returns here, whatever production did after the cut (cleanup, retries,
    /// messages) is thrown away: the computer's objects are abandoned and the frozen disk is put back, exactly as after a
    /// power cut. Only the computer in question runs during its own action, so putting back the share is correct. If the
    /// operation never reaches the point, nothing happens and the operation simply completes.
    ///
    /// <para>The whole thing is ONE funnel step: the operation's own lines are nested under it and the invariants run
    /// once, after the disk is back. Call it from a top-level context, never from inside another funnel step.</para>
    /// </summary>
    /// <param name="computer">The computer whose power is cut.</param>
    /// <param name="point">One of <see cref="TermPoint.Services.CrashPoints.All"/>.</param>
    /// <param name="occurrence">Which time the point is reached (1 = the first).</param>
    /// <param name="storyLine">The sentence for the step ("Bob presses Save").</param>
    /// <param name="operation">The operation, normally one of the computer's action methods.</param>
    /// <returns>True when the power was cut; false when the operation never reached the point.</returns>
    /// <exception cref="ArgumentException">The name is not a crash point.</exception>
    /// <exception cref="InvalidOperationException">The disk could not be frozen at the cut, so the cut would not be faithful.</exception>
    public Task<bool> RunWithPowerCutAsync(Computer computer, string point, int occurrence, string storyLine, Func<Task> operation)
    {
        if (!CrashPoints.All.Contains(point))
            throw new ArgumentException($"'{point}' is not a crash point.", nameof(point));

        var cut = new ArmedPowerCut(computer, point, occurrence);
        return DoAsync<bool>(null, $"{storyLine}, and {computer.DisplayName}'s computer loses power at {point}", async () =>
        {
            lock (_gate) _armedCut = cut;
            try
            {
                try { await operation(); }
                catch (Exception) when (cut.Fired)
                {
                    // The process was dead from the cut on: whatever it threw afterwards is not an event.
                }
            }
            finally
            {
                lock (_gate) _armedCut = null;
            }

            if (!cut.Fired) return false;

            // Let the dead process's own background work finish (it is stopped at every crash point and network operation),
            // so that nothing of it can write to the disk after the frozen copy is put back.
            await computer.SettleAfterPowerCutAsync();
            cut.WaitUntilFrozen(TimeSpan.FromSeconds(60));

            if (cut.Disk is null)
                throw new InvalidOperationException(
                    $"The disk could not be frozen at {point}, so the power cut would not be faithful: {cut.FreezeFailure}");

            computer.AbandonProcess();
            RestoreFrozenDisk(computer, cut.Disk);
            _story.Add(_step, 1, $"{computer.DisplayName}'s files are back exactly as they were at the cut; nothing the program did afterwards was written");
            return true;
        },
        fired => fired ? "the power was cut" : "that point was not reached, so the power was not cut");
    }

    /// <summary>
    /// Cuts the power of the process that has just reached a crash point: marks it dead, freezes the disk at this
    /// instant, and records the cut. Called from the crash-point hook, on whatever thread reached the point.
    /// </summary>
    /// <param name="cut">The armed cut that matched.</param>
    /// <param name="life">The process reaching the point.</param>
    /// <param name="point">The crash point.</param>
    private void FireCut(ArmedPowerCut cut, ProcessLife life, string point)
    {
        life.CutPower();   // first, so the process's other threads stop at their next crash point
        try { cut.Disk = FreezeDisk(cut.Computer); }
        catch (Exception ex) { cut.FreezeFailure = ex; }
        finally { cut.MarkFrozen(); }

        lock (_gate) _firedCuts.Add($"{cut.Computer.Name}@{point}");
        var nth = cut.Occurrence > 1 ? $" (the {cut.Occurrence}{(cut.Occurrence == 2 ? "nd" : cut.Occurrence == 3 ? "rd" : "th")} time it was reached)" : "";
        _story.Add(_step, 1, $"{cut.Computer.DisplayName}'s computer lost power at {point}{nth}");

        // A restore from a backup that has just replaced the database is a deliberate replacement even if the program
        // never gets to say so: the I1 baseline starts again from it.
        if (point == "Restore.AfterRename") NoteRestoreLanded();
    }

    /// <summary>Records the process whose action is about to run in the current flow of control (called by <see cref="Computer"/> at the start of every action).</summary>
    /// <param name="life">The process.</param>
    internal void EnterAction(ProcessLife life) => _acting.Value = life;

    /// <summary>The process the current flow of control belongs to, or (for code outside any action) the computer acting now; null when neither.</summary>
    private ProcessLife? ActingLife() => _acting.Value ?? _currentActor?.Life;

    /// <summary>How many crash points have been reached so far (a position in the hit log).</summary>
    public int HitCount
    {
        get { lock (_gate) return _hitLog.Count; }
    }

    /// <summary>The crash points reached since a position in the hit log, in order.</summary>
    /// <param name="start">A value of <see cref="HitCount"/> taken earlier.</param>
    /// <returns>The computer's name ("?" when unknown) and the point, for each hit.</returns>
    public IReadOnlyList<(string Computer, string Point)> HitsSince(int start)
    {
        lock (_gate) return _hitLog.Skip(start).ToList();
    }

    /// <summary>The cuts that fired so far ("bob@Save.AfterRename"), in order.</summary>
    public IReadOnlyList<string> FiredCuts
    {
        get { lock (_gate) return _firedCuts.ToList(); }
    }

    // ── Pausing an operation in the middle (step 9.3c-2b) ────────────────────

    /// <summary>
    /// Runs one operation of a computer and HOLDS it the <paramref name="occurrence"/>-th time it reaches the named crash point,
    /// runs one other event meanwhile, and then lets the operation go on. This is a computer that goes to sleep in the middle of an
    /// operation while the world carries on: the power cut of step 9.3c-2a ends the operation, a pause only delays it.
    ///
    /// <list type="number">
    /// <item><description>The operation runs on its own task, marked as the held operation. When it makes the n-th arrival at the
    /// point, that thread blocks on a gate, and every other thread of the operation stands still at its next crash point or network
    /// operation (the whole process is suspended).</description></item>
    /// <item><description>The event (<paramref name="pauseEvent"/>) runs on a task of the test's flow, if it is
    /// <paramref name="eventIsPossible"/> in the state the pause finds. It is given a grace period of real time.</description></item>
    /// <item><description>The operation is released. If the event was still unfinished at the end of the grace period, it was waiting
    /// for the operation (the switch waits for a save in flight, which is designed behaviour): both then finish, the story says "(the
    /// event) waited for (the operation) to finish" and the outcome is <see cref="PauseOutcome.Waited"/>, not a violation.</description></item>
    /// <item><description>Only when something is STILL unfinished at <see cref="PauseLimits.FinishLimit"/> after the release (or the
    /// operation neither reached the point nor returned) is it a <see cref="ViolationKind.Hang"/>: the stuck work is abandoned the way a
    /// power cut abandons a dead process (its computer is marked dead, so anything that ever moves is stopped at the next crash point
    /// or network operation), and the run goes on. The test run never hangs.</description></item>
    /// </list>
    ///
    /// <para>The whole thing is ONE funnel step: the operation's and the event's story lines are nested under it and the invariants run
    /// once, at the end. Between the event and the release the shared database is read, so that invariant I1 knows what the event
    /// committed before the held save's rename lands over it. Call it from a top-level context, never from inside another funnel step.</para>
    /// </summary>
    /// <param name="computer">The computer whose operation is held.</param>
    /// <param name="point">One of <see cref="TermPoint.Services.CrashPoints.All"/>.</param>
    /// <param name="occurrence">Which time the point is reached (1 = the first).</param>
    /// <param name="storyLine">The sentence for the operation ("Bob presses Save").</param>
    /// <param name="operation">The operation, normally one of the computer's action methods.</param>
    /// <param name="eventStory">The event as a phrase that completes "... is held while ..." ("Alice takes over the lock").</param>
    /// <param name="eventIsPossible">Asked at the pause: false means the event is not possible in this state and nothing happens meanwhile.</param>
    /// <param name="pauseEvent">The event that happens while the operation is held.</param>
    /// <param name="eventComputers">The computers the event works on: the ones abandoned if the event hangs (may be null).</param>
    /// <param name="limits">The real-time limits; null for <see cref="PauseLimits.Default"/>.</param>
    /// <returns>What the pause did.</returns>
    /// <exception cref="ArgumentException">The name is not a crash point.</exception>
    public Task<PauseResult> RunWithPauseAsync(
        Computer computer, string point, int occurrence, string storyLine, Func<Task> operation,
        string eventStory, Func<bool> eventIsPossible, Func<Task> pauseEvent,
        IReadOnlyList<Computer>? eventComputers = null, PauseLimits? limits = null)
    {
        if (!CrashPoints.All.Contains(point))
            throw new ArgumentException($"'{point}' is not a crash point.", nameof(point));

        var lim   = limits ?? PauseLimits.Default;
        var pause = new ArmedPause(computer, point, occurrence);
        var headline = $"{storyLine}, and at {point}{DescribeOccurrence(occurrence)} {computer.DisplayName}'s operation is held while {eventStory}";

        return DoAsync<PauseResult>(null, headline, async () =>
        {
            var actorBefore = _currentActor;
            Task opTask    = Task.CompletedTask;
            Task eventTask = Task.CompletedTask;
            var outcome    = PauseOutcome.NotReached;
            string? hang   = null;
            var stuck      = new List<Computer>();

            try
            {
                lock (_gate) _armedPause = pause;
                opTask = Task.Run(() => RunHeldOperation(pause, operation));

                // 1. The operation runs until it is held at the point, or it ends without reaching it.
                if (!await CompletesWithinAsync(Task.WhenAny(pause.HeldTask, opTask), lim.ReachLimit))
                {
                    hang = $"{computer.DisplayName}'s operation neither reached {point}{DescribeOccurrence(occurrence)} nor finished within " +
                           $"{lim.ReachLimit.TotalSeconds:0.#} seconds";
                    stuck.Add(computer);
                    outcome = PauseOutcome.Hung;
                }
                else if (!pause.IsHeld)
                {
                    await opTask;   // an exception of the operation is the step's exception
                    _story.Add(_step, 1, $"{computer.DisplayName}'s operation never reached {point}{DescribeOccurrence(occurrence)}, so nothing happened meanwhile");
                }
                else
                {
                    // 2. The event, while every thread of the operation stands still.
                    var heldAt = TryEditsInFile(DatabasePath);
                    var interleaved = false;
                    if (!eventIsPossible())
                    {
                        _story.Add(_step, 1, $"({eventStory}) was not possible at that moment, so nothing happened meanwhile");
                        outcome = PauseOutcome.Skipped;
                    }
                    else
                    {
                        eventTask   = Task.Run(pauseEvent);
                        interleaved = await CompletesWithinAsync(eventTask, lim.Grace);
                        if (interleaved) NoteCommitsDuringHeldSave(pause, heldAt);
                        else _story.Add(_step, 1,
                            $"({eventStory}) had not finished after {lim.Grace.TotalSeconds:0.#} seconds: it is waiting for {computer.DisplayName}'s operation, so the operation is let go");
                        outcome = interleaved ? PauseOutcome.Interleaved : PauseOutcome.Waited;
                    }

                    // 3. Let the operation go on, and let both finish.
                    pause.Release();
                    var both = Task.WhenAll(opTask, eventTask);
                    if (!await CompletesWithinAsync(both, lim.FinishLimit))
                    {
                        var which = !opTask.IsCompleted ? $"{computer.DisplayName}'s operation" : $"the event ({eventStory})";
                        hang = $"{which} had not finished {lim.FinishLimit.TotalSeconds:0.#} seconds after {computer.DisplayName}'s operation was let go from {point}";
                        if (!opTask.IsCompleted) stuck.Add(computer);
                        if (!eventTask.IsCompleted && eventComputers is not null) stuck.AddRange(eventComputers);
                        outcome = PauseOutcome.Hung;
                    }
                    else
                    {
                        await both;   // an exception of the operation or the event is the step's exception
                        if (outcome == PauseOutcome.Waited)
                            _story.Add(_step, 1, $"({eventStory}) waited for {computer.DisplayName}'s operation to finish");
                    }
                }
            }
            finally
            {
                pause.Release();   // never leave a thread held, whatever happened
                lock (_gate) _armedPause = null;
                _currentActor = actorBefore;   // the two overlapping actions restore it in either order
            }

            if (hang is not null)
            {
                _story.Add(_step, 1, $"HANG: {hang}; the stuck work was abandoned");
                AddPending(new Violation
                {
                    Step = _step, Invariant = "Hang", Kind = ViolationKind.Hang,
                    Key = $"Hang:{_step}:{computer.Name}:{point}", Computer = computer.Name, CrashPoint = point,
                    Reason = hang
                });
                AbandonStuck(stuck);
            }

            var result = new PauseResult(computer.Name, point, occurrence, eventStory, outcome);
            lock (_gate) _pauseResults.Add(result);
            return result;
        },
        r => r.Outcome switch
        {
            PauseOutcome.NotReached  => "that point was not reached, so the pause did not happen",
            PauseOutcome.Skipped     => "the event was not possible then, so nothing happened meanwhile",
            PauseOutcome.Interleaved => "the event happened while the operation was held, then the operation went on",
            PauseOutcome.Waited      => "the event had to wait for the operation, which was then let go",
            _                        => "something did not finish: a hang"
        });
    }

    /// <summary>Starts the held operation on the calling (pool) thread, marked so that everything it starts is held with it.</summary>
    /// <param name="pause">The pause the operation belongs to.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>The operation's task.</returns>
    private Task RunHeldOperation(ArmedPause pause, Func<Task> operation)
    {
        _pausedFlow.Value = pause;   // this flow, and every task and thread it starts, is the held operation
        return operation();
    }

    /// <summary>Waits for a task, but not longer than a limit of real time, without leaving the timer running.</summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="limit">The longest to wait.</param>
    /// <returns>True when the task completed in time (faulted or not); false when the limit passed first.</returns>
    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan limit)
    {
        using var cancel = new CancellationTokenSource();
        var timer = Task.Delay(limit, cancel.Token);
        var first = await Task.WhenAny(task, timer);
        if (first != task) return false;
        cancel.Cancel();
        return true;
    }

    /// <summary>
    /// The event of a pause has just finished while a save was held before its rename: reads the shared database and (1) tells the
    /// I1 baseline what it holds now, so that edits another computer committed meanwhile are known when the held save's rename lands
    /// over them, and (2) remembers which edits those were (finding F12).
    /// </summary>
    /// <param name="pause">The pause.</param>
    /// <param name="heldAt">The edits the shared database held when the operation was held (null when it could not be read).</param>
    private void NoteCommitsDuringHeldSave(ArmedPause pause, List<string>? heldAt)
    {
        var now = TryEditsInFile(DatabasePath);
        _invariants.ObserveSharedMidStep(now);

        if (heldAt is null || now is null || !PausePoints.IsSaveBeforeRename(pause.Point)) return;
        lock (_gate)
        {
            foreach (var edit in now.Where(e => !heldAt.Contains(e)))
                _commitsDuringHeldSave[edit] = _step;
        }
    }

    /// <summary>
    /// Abandons work that never finished, the way a power cut abandons a dead process: the computer's process is marked dead (so that
    /// if the stuck code ever moves, it is stopped at its next crash point, network operation or dialog) and its handles are released.
    /// </summary>
    /// <param name="computers">The computers whose work is stuck.</param>
    private static void AbandonStuck(IEnumerable<Computer> computers)
    {
        foreach (var computer in computers.Distinct())
        {
            computer.Life.CutPower();
            try { computer.AbandonProcess(); }
            catch (Exception) { /* best effort: the work is stuck, so this may be too */ }
        }
    }

    /// <summary>
    /// Called by a computer when one of its edits has been applied (the row is in its working file and the marker is written). An edit
    /// applied while that computer's own operation is held in the edit-loss window (<see cref="PausePoints.EditLossWindow"/>) is not in the save,
    /// yet the save's last step deletes the marker that protected it and a close or switch deletes the working copy: finding F2. Remembered so that, if the edit is later lost, the classifier can say so.
    /// </summary>
    /// <param name="computer">The computer that edited.</param>
    /// <param name="editId">The edit's id.</param>
    internal void NoteEditApplied(Computer computer, string editId)
    {
        var pause = _armedPause;
        if (pause is { IsFrozen: true } && pause.Computer == computer && PausePoints.IsInEditLossWindow(pause.Point))
            lock (_gate) _editsAppliedDuringHeldSave.Add(editId);
    }

    /// <summary>True when the edit was applied while its computer's own operation was held in the edit-loss window (see <see cref="NoteEditApplied"/>).</summary>
    /// <param name="editId">The edit's id.</param>
    public bool WasEditAppliedDuringHeldSave(string editId)
    {
        lock (_gate) return _editsAppliedDuringHeldSave.Contains(editId);
    }

    /// <summary>
    /// True when every one of the edits was committed to the shared database, in the given step, while a save was held before its
    /// rename (see <see cref="NoteCommitsDuringHeldSave"/>): the rename that followed is what removed them (finding F12).
    /// </summary>
    /// <param name="editIds">The edits that disappeared.</param>
    /// <param name="step">The step the disappearance was found in.</param>
    public bool WereCommittedDuringHeldSave(IReadOnlyList<string> editIds, int step)
    {
        if (editIds.Count == 0) return false;
        lock (_gate) return editIds.All(e => _commitsDuringHeldSave.TryGetValue(e, out var s) && s == step);
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

    /// <summary>Like <see cref="EditsInFile"/> but answers null for a file that cannot be read, instead of throwing.</summary>
    /// <param name="databaseFile">The file to look in.</param>
    /// <returns>The edit ids; empty when the file does not exist; null when it exists but cannot be read.</returns>
    public List<string>? TryEditsInFile(string databaseFile) => Inspector.TryEditIds(databaseFile, ScratchDir);

    /// <summary>Runs <c>PRAGMA integrity_check</c> on a copy of a database file (invariant I3).</summary>
    /// <param name="databaseFile">The file to check.</param>
    /// <returns>Null when the file passes (or does not exist); otherwise what is wrong.</returns>
    public string? IntegrityProblem(string databaseFile) => Inspector.IntegrityProblem(databaseFile, ScratchDir);

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

    /// <summary>The dialogs a computer has been asked so far during the current step, in order.</summary>
    /// <param name="computer">The computer.</param>
    /// <returns>A copy of the list; empty when none.</returns>
    public IReadOnlyList<PromptCall> StepPrompts(Computer computer)
    {
        lock (_gate)
            return _stepPrompts.TryGetValue(computer.Name, out var list) ? list.ToList() : new List<PromptCall>();
    }

    // ── Observers (prompts, banners, restores, opens) ────────────────────────

    /// <summary>
    /// Called by a computer's scripted prompts for every dialog it served: writes the dialog into the story, remembers it
    /// for the step's classification, records the file of a "Save a Copy" pick, records a confirmed discard, applies the
    /// F7 rule ("Restore" followed by "Can't Restore") and hands a message to the promise tracker.
    /// </summary>
    /// <param name="computer">The computer that was asked.</param>
    /// <param name="call">What it was asked, with the answer.</param>
    public void ObservePrompt(Computer computer, PromptCall call)
    {
        if (computer.PowerIsOut) return;   // a dialog of a process without power is seen by nobody

        _story.Add(_step, 1, DescribePrompt(computer, call));

        lock (_gate)
        {
            if (!_stepPrompts.TryGetValue(computer.Name, out var list))
                _stepPrompts[computer.Name] = list = new List<PromptCall>();
            list.Add(call);

            if (call.Kind == PromptKind.PickSaveFile && call.PickedPath is not null)
                _savedCopyPaths.Add(call.PickedPath);

            // A confirmed discard covers the unsaved edits the computer held when the step began.
            if (call.Kind == PromptKind.Choice && call.Title == HarnessTitles.DiscardConfirm
                && call.Answer == ChoiceAnswer.Affirmative)
                _confirmedDiscards.UnionWith(_last.UnsavedEditsOf(computer.Name));
        }

        // The F7 rule: "Restore" chosen, and the very next question says it cannot be done.
        if (call.Kind == PromptKind.Choice && call.Title == HarnessTitles.CantRestore)
        {
            var calls = computer.Prompts.Calls;
            var previous = calls.Take(calls.Count - 1).LastOrDefault(c => c.Kind == PromptKind.Choice);
            if (previous is { Title: HarnessTitles.RestoreUnsavedChanges, Answer: ChoiceAnswer.Affirmative })
            {
                AddPending(new Violation
                {
                    Step = _step, Invariant = "I5", Kind = ViolationKind.RestoreOfferedThenImpossible,
                    DataUnchangedSinceKept = DataUnchangedSinceKept(computer),
                    AllKeptEditsAlreadyShared = AllKeptEditsAreInShared(computer),
                    Key = $"I5:F7:{_step}:{computer.Name}", Computer = computer.Name,
                    Reason = $"{computer.DisplayName} was asked \"{HarnessTitles.RestoreUnsavedChanges}\", chose Restore, and was then told " +
                             "\"Can't Restore Unsaved Changes\": the question should not have been asked",
                    ComputerPrompts = StepPrompts(computer)
                });
            }
        }

        if (call.Kind == PromptKind.Message)
            HandMessageToTracker(computer, call.Title, call.Text);
    }

    /// <summary>
    /// The F5 rule: did nobody change any DATA in the shared database since the computer's work was kept? True when the
    /// edits in D are exactly the edits in the computer's kept working file, leaving out the edits the computer itself
    /// made that D does not hold (its unsaved work). Then the database's bytes alone differ from what the computer
    /// started from (a no-edit save somewhere rewrote the file), which is finding F5; when D holds an edit the kept
    /// file does not know, someone changed data and the refusal is the ordinary F7. Read at the moment "Can't Restore
    /// Unsaved Changes" is shown, when the kept file is still on disk.
    /// </summary>
    /// <param name="computer">The computer that was refused.</param>
    /// <returns>True for F5; false for F7 (and when either file cannot be read).</returns>
    private bool DataUnchangedSinceKept(Computer computer)
    {
        var kept = TryEditsInFile(computer.WorkingFile);
        var shared = TryEditsInFile(DatabasePath);
        if (kept is null || shared is null) return false;

        var own = computer.AppliedEdits.Where(e => !shared.Contains(e)).ToHashSet();
        var base_ = kept.Where(e => !own.Contains(e)).ToHashSet();
        return base_.SetEquals(shared);
    }

    /// <summary>
    /// The F8 rule: is every edit this computer made that sits in its kept working file already in the shared database?
    /// Then "Can't Restore" refuses to restore changes the database already holds (the computer's own save landed, and
    /// the crash came before the unsaved-changes marker was deleted). False when there is no such edit, when one is not in
    /// the database, or when either file cannot be read. Read at the moment "Can't Restore Unsaved Changes" is shown,
    /// when the kept file is still on disk.
    /// </summary>
    /// <param name="computer">The computer that was refused.</param>
    /// <returns>True for F8.</returns>
    private bool AllKeptEditsAreInShared(Computer computer)
    {
        var kept   = TryEditsInFile(computer.WorkingFile);
        var shared = TryEditsInFile(DatabasePath);
        if (kept is null || shared is null) return false;

        var mine = kept.Where(e => computer.AppliedEdits.Contains(e)).ToList();
        return mine.Count > 0 && mine.All(shared.Contains);
    }

    /// <summary>
    /// Called when a banner appears on a computer's screen (the lock-lost banner, a save-failure banner): writes it into
    /// the story and hands it to the promise tracker.
    /// </summary>
    /// <param name="computer">The computer whose screen shows the banner.</param>
    /// <param name="text">The banner text.</param>
    public void ObserveBanner(Computer computer, string text)
    {
        if (computer.PowerIsOut) return;   // a banner of a process without power is seen by nobody

        _story.Add(_step, 1, $"{computer.DisplayName}'s screen showed a banner: {MessagePromises.FirstSentence(text)}");
        HandMessageToTracker(computer, "", text);
    }

    /// <summary>Looks a message up in the promise tracker and keeps any violation it returns.</summary>
    /// <param name="computer">The computer that showed it.</param>
    /// <param name="title">The title ("" for a banner).</param>
    /// <param name="text">The text.</param>
    private void HandMessageToTracker(Computer computer, string title, string text)
    {
        var violation = _promises.OnMessage(this, computer, title, text, _step);
        if (violation is not null) AddPending(violation);
    }

    /// <summary>Called by a computer when an open (startup, switch, or the reopening after a conflict) has finished.</summary>
    /// <param name="computer">The computer that opened a database.</param>
    public void NoteOpened(Computer computer)
    {
        if (computer.PowerIsOut) return;
        lock (_gate) _openedThisStep.Add(computer);
    }

    /// <summary>
    /// Called when a restore from a backup really replaced D (outcome Restored or RenameUnconfirmed): the I1 baseline starts
    /// again from the restored file, and any I4 observation made at <c>Restore.AfterTmpVerified</c> becomes a violation.
    /// </summary>
    public void NoteRestoreLanded()
    {
        _invariants.ResetBaselineAtNextCheck();
        ExcuseEditsGoneFromDatabase(_last.Shared ?? new List<string>());
        lock (_gate)
        {
            _pending.AddRange(_restoreSuspects);
            _restoreSuspects.Clear();
        }
    }

    /// <summary>
    /// An explicit, deliberate replacement of D (IT restoring an old file, a restore from a backup) removes edits from
    /// the shared database ON PURPOSE. The edits that were in D <paramref name="before"/> the replacement and are not in
    /// it now are excused from invariant I2: the replacement is the confirmed act that removed them.
    /// </summary>
    /// <param name="before">The edits D held before the replacement.</param>
    private void ExcuseEditsGoneFromDatabase(IEnumerable<string> before)
    {
        var after = TryEditsInFile(DatabasePath) ?? new List<string>();
        lock (_gate) _confirmedDiscards.UnionWith(before.Where(e => !after.Contains(e)));
    }

    /// <summary>Adds a violation to be raised when the current step ends.</summary>
    /// <param name="violation">The violation.</param>
    private void AddPending(Violation violation)
    {
        lock (_gate) _pending.Add(violation);
    }

    /// <summary>Describes a dialog for the story, in plain English.</summary>
    /// <param name="computer">The computer that was asked.</param>
    /// <param name="call">The dialog.</param>
    private static string DescribePrompt(Computer computer, PromptCall call)
    {
        var who = computer.DisplayName;
        switch (call.Kind)
        {
            case PromptKind.Message:
                return $"{who} saw the message \"{call.Title}\": {MessagePromises.FirstSentence(call.Text)}";

            case PromptKind.Choice:
            {
                var buttons = call.Buttons.Split('|');
                var answer = call.Answer switch
                {
                    ChoiceAnswer.Affirmative => $"chose \"{buttons[0]}\"",
                    ChoiceAnswer.Negative    => $"chose \"{(buttons.Length > 1 ? buttons[1] : "the second button")}\"",
                    ChoiceAnswer.ClosedWithX => "closed it with the X",
                    _                        => "left it unanswered"
                };
                return $"{who} was asked \"{call.Title}\" [{string.Join(" | ", buttons)}] and {answer}";
            }

            case PromptKind.Wait:
                return $"{who} saw the wait dialog \"{call.Title}\"";

            case PromptKind.PickSaveFile:
                return call.PickedPath is null
                    ? $"{who} was shown the Save dialog \"{call.Title}\" and cancelled it"
                    : $"{who} was shown the Save dialog \"{call.Title}\" and chose {Path.GetFileName(call.PickedPath)}";

            case PromptKind.StaleLockWait:
                return $"{who} waited for the other user's lock to prove inactive ({call.Text} seconds left)";

            default:
                return $"{who} saw the database chooser ({call.Text})";
        }
    }

    // ── Crash-point observer (I4) and walk hooks ─────────────────────────────

    /// <summary>
    /// Runs extra code the first time execution passes a named crash point (for example "write Alice's lock the
    /// instant before Bob's rename"). The code runs INSIDE the operation, on the thread that hit the point, before the
    /// I4 observer looks. It runs once.
    /// </summary>
    /// <param name="name">One of <see cref="CrashPoints.All"/>.</param>
    /// <param name="action">What to do there.</param>
    /// <param name="storyLine">The sentence for the story ("(at Save.AfterTmpVerify) ..."); null for a default.</param>
    /// <exception cref="ArgumentException">The name is not a crash point.</exception>
    public void AtCrashPoint(string name, Action action, string? storyLine = null)
    {
        if (!CrashPoints.All.Contains(name))
            throw new ArgumentException($"'{name}' is not a crash point.", nameof(name));

        lock (_gate)
        {
            if (!_crashPointActions.TryGetValue(name, out var list))
                _crashPointActions[name] = list = new List<(Action, string)>();
            list.Add((action, storyLine ?? $"(at {name}) the walk's extra step ran"));
        }
    }

    /// <summary>
    /// Runs extra code EVERY time execution passes a named crash point, silently (no story line): the way a test plants
    /// a bug without touching production code ("MarkDirty forgot the marker" is the code that deletes the marker
    /// at <c>Dirty.AfterMarkerWritten</c>). Unlike <see cref="AtCrashPoint"/> it is never used up.
    /// </summary>
    /// <param name="name">One of <see cref="CrashPoints.All"/>.</param>
    /// <param name="action">What to do there, every time.</param>
    /// <exception cref="ArgumentException">The name is not a crash point.</exception>
    public void AtCrashPointEveryTime(string name, Action action)
    {
        if (!CrashPoints.All.Contains(name))
            throw new ArgumentException($"'{name}' is not a crash point.", nameof(name));

        lock (_gate)
        {
            if (!_repeatingCrashPointActions.TryGetValue(name, out var list))
                _repeatingCrashPointActions[name] = list = new List<Action>();
            list.Add(action);
        }
    }

    /// <summary>
    /// The <see cref="CrashPoints.Hook"/>: runs the walk's extra code for the point (once), notes a lost-lock demotion's database
    /// (invariant I6, clause b), holds the operation when a pause is armed for this point, then, at the points just before a rename
    /// onto the shared database, checks invariant I4. Throws only for a process whose power is out.
    /// </summary>
    /// <param name="name">The crash point being passed.</param>
    private void OnCrashPoint(string name)
    {
        // A process without power does nothing at all from the cut on: nothing is recorded, no walk code runs, no
        // invariant looks. Production code may have caught the first PowerCutException and carried on; this stops it again.
        var life = ActingLife();
        if (life is { PowerIsOut: true }) throw new PowerCutException(name);

        // A held operation (step 9.3c-2b) is a computer that sleeps in the middle of it: while the pause is on, every thread of the
        // operation stands still at its next crash point. (The event that runs meanwhile is not marked, so it never waits here.)
        var held = _pausedFlow.Value;
        if (held is { IsFrozen: true })
        {
            held.WaitForRelease(PauseLimits.SafetyWait);
            if (life is { PowerIsOut: true }) throw new PowerCutException(name);   // abandoned while it was held
        }

        var armed = _armedCut;

        // The lock file's delete (Lock.AfterDeleted) is done by a background task, at the same time as the thread that
        // released the lock carries on. A cut armed for it would freeze whatever the carrying-on thread had managed by then,
        // which differs from run to run. So that the frozen disk (and a replay of the case) is always the same, the other
        // thread waits at its next crash point, once released, until the delete has happened and the power is cut.
        if (armed is not null && armed.Point == "Lock.AfterDeleted" && !armed.Fired && name != armed.Point
            && life is not null && life.Owner == armed.Computer && life.Owner.Locks.PendingRelease is { IsCompleted: false })
        {
            armed.WaitUntilFrozen(TimeSpan.FromSeconds(5));
            if (life.PowerIsOut) throw new PowerCutException(name);
        }

        int hitIndex;
        lock (_gate)
        {
            hitIndex = _hitLog.Count;
            _hitLog.Add((life?.Owner.Name ?? "?", name));
        }
        CrashPointSurvey?.Invoke(hitIndex, life?.Owner, name);

        // The armed power cut: the n-th time the armed computer reaches the armed point, its power goes.
        if (armed is not null && life is not null && life.Owner == armed.Computer
            && name == armed.Point && !armed.Fired && armed.CountArrival() && armed.TryFire())
        {
            FireCut(armed, life, name);
            throw new PowerCutException(name);
        }

        List<(Action Action, string StoryLine)>? due = null;
        List<Action>? repeating = null;
        lock (_gate)
        {
            if (_crashPointActions.TryGetValue(name, out var list) && list.Count > 0)
            {
                due = list.ToList();
                list.Clear();
            }
            if (_repeatingCrashPointActions.TryGetValue(name, out var again))
                repeating = again.ToList();
        }

        if (repeating is not null)
            foreach (var action in repeating) action();

        if (due is not null)
        {
            foreach (var (action, storyLine) in due)
            {
                _story.Add(_step, 1, storyLine);
                action();
            }
        }

        // Invariant I6, clause b: a demotion stays on the database it started on. The database it started on is noted BEFORE a pause
        // holds it here and compared AFTER, so what happened meanwhile (the person switched database) is what the comparison sees.
        DemotionRecord? demotion = null;
        if (life is not null && name.StartsWith("Demote.", StringComparison.Ordinal)) demotion = NoteDemotionPoint(life);

        // The armed pause: the n-th time the held operation's flow reaches the armed point, the thread that got there is held.
        var pause = _armedPause;
        if (pause is not null && life is not null && held == pause && life.Owner == pause.Computer
            && name == pause.Point && pause.CountArrival() && pause.TryHold())
        {
            HoldAtPoint(pause, life, name);
            if (life.PowerIsOut) throw new PowerCutException(name);                 // abandoned while it was held
        }

        if (demotion is not null) CheckDemotionStaysOnItsDatabase(demotion, life!, name);

        if (name is "Save.AfterTmpVerify" or "Restore.AfterTmpVerified") CheckWriterAtRename(name);
    }

    /// <summary>
    /// Holds the calling thread at the point of a pause until the harness lets the operation go. Everything else of the operation's
    /// flow stands still at its next crash point or network operation meanwhile (see <see cref="OnCrashPoint"/>).
    /// </summary>
    /// <param name="pause">The pause that claimed this arrival.</param>
    /// <param name="life">The process that reached the point.</param>
    /// <param name="point">The crash point.</param>
    private void HoldAtPoint(ArmedPause pause, ProcessLife life, string point)
    {
        _story.Add(_step, 1, $"{life.Owner.DisplayName}'s operation reached {point}{DescribeOccurrence(pause.Occurrence)} and is held there " +
                             $"(everything of {life.Owner.DisplayName}'s process stands still)");
        pause.WaitForRelease(PauseLimits.SafetyWait);
    }

    /// <summary>The words that say which arrival at a point was meant (" (the 2nd time it is reached)"); empty for the first.</summary>
    /// <param name="occurrence">1 for the first arrival.</param>
    private static string DescribeOccurrence(int occurrence)
        => occurrence <= 1 ? "" : $" (the {occurrence}{(occurrence == 2 ? "nd" : occurrence == 3 ? "rd" : "th")} time it is reached)";

    /// <summary>
    /// The key a database is known by in invariant I6: its full path, lower case (production's <c>NormalizeSourcePath</c>); empty when
    /// the checkout has no source yet.
    /// </summary>
    /// <param name="sourcePath">A checkout's <c>SourcePath</c>, or null.</param>
    private static string DatabaseKey(string? sourcePath)
        => string.IsNullOrEmpty(sourcePath) ? "" : CheckoutService.NormalizeSourcePath(sourcePath);

    /// <summary>
    /// A lost-lock demotion reached a <c>Demote.*</c> point (invariant I6, clause b): the first point of a demotion in this flow of
    /// control records the database the session is on; later points compare against it.
    /// </summary>
    /// <param name="life">The process whose demotion it is.</param>
    /// <returns>The record of this demotion (new at its first point).</returns>
    private DemotionRecord NoteDemotionPoint(ProcessLife life)
    {
        var record = _demotion.Value;
        if (record is null || record.Life != life)
        {
            record = new DemotionRecord(life, DatabaseKey(life.Owner.Checkout.SourcePath));
            _demotion.Value = record;
        }
        return record;
    }

    /// <summary>
    /// Invariant I6, clause b: a demotion applies only to the database it started on. At every <c>Demote.*</c> point (after any pause
    /// held it there) the session's database must still be the one the demotion started on; the last point ends the record. A
    /// mismatch is kept and raised when the step ends (finding F16: the demotion then copies the OTHER database and makes the new
    /// session read-only).
    /// </summary>
    /// <param name="started">The demotion's record.</param>
    /// <param name="life">The process whose demotion it is.</param>
    /// <param name="point">The <c>Demote.*</c> point just reached.</param>
    private void CheckDemotionStaysOnItsDatabase(DemotionRecord started, ProcessLife life, string point)
    {
        var now = DatabaseKey(life.Owner.Checkout.SourcePath);
        if (!string.Equals(now, started.Database, StringComparison.OrdinalIgnoreCase))
        {
            AddPending(new Violation
            {
                Step = _step, Invariant = "I6", Kind = ViolationKind.DemotionOnAnotherDatabase,
                Key = $"I6b:{_step}:{life.Owner.Name}:{Path.GetFileName(started.Database)}:{Path.GetFileName(now)}",
                Computer = life.Owner.Name, CrashPoint = point,
                Reason = $"a lost-lock demotion of {life.Owner.DisplayName}'s session started on {Path.GetFileName(started.Database)}, and at {point} " +
                         $"the session is on {Path.GetFileName(now)}: the demotion goes on against the wrong database"
            });
        }

        if (point == "Demote.AfterSnapshotCreated") _demotion.Value = null;   // the demotion's last point: it is over
    }

    /// <summary>
    /// Invariant I4, observed just before a rename onto D: the lock file on the share must exist and hold the acting
    /// computer's session, and D must still be exactly the version that computer last saw (its fingerprint, or the
    /// pending-save fingerprint in its marker). A mismatch is kept and raised when the step ends. For a restore the
    /// observation is only a suspicion until the restore really lands, because the restore re-checks the lock and D
    /// itself immediately before its swap.
    /// </summary>
    /// <param name="point">The crash point: <c>Save.AfterTmpVerify</c> or <c>Restore.AfterTmpVerified</c>.</param>
    private void CheckWriterAtRename(string point)
    {
        // Who reached the rename: the computer whose flow of control it is, NOT "the computer acting now". While a pause runs an
        // event, two actions overlap and the single "current actor" field cannot say which of them is saving.
        var actor = ActingComputer;
        if (actor is null) return;

        var problems = new List<string>();

        // The database being written is the one the acting session is on (D, or "other.db" after a switch).
        var sourcePath = string.IsNullOrEmpty(actor.Checkout.SourcePath) ? DatabasePath : actor.Checkout.SourcePath;
        var sourceLockPath = Path.ChangeExtension(sourcePath, ".lock");
        var onSharedDatabase = string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase);

        // The lock must be the saver's.
        try
        {
            if (!File.Exists(sourceLockPath))
            {
                problems.Add("the lock file does not exist");
            }
            else
            {
                var json = ReadSharedText(sourceLockPath);
                var holder = JsonSerializer.Deserialize<LockFileData>(json);
                if (holder?.SessionGuid != actor.Locks.SessionGuid)
                    problems.Add($"the lock belongs to {holder?.Username} on {holder?.Machine}, not to {actor.DisplayName}");
            }
        }
        catch (Exception ex)
        {
            problems.Add($"the lock file could not be read ({ex.GetType().Name})");
        }

        // The database must be the version the saver last saw.
        try
        {
            var expected = new List<string>();
            if (actor.Checkout.HashAtCheckout is { } seen) expected.Add(seen);
            var markerFile = onSharedDatabase || string.IsNullOrEmpty(actor.Checkout.WorkingPath)
                ? actor.MarkerFile
                : actor.Checkout.WorkingPath + ".dirty";
            if (actor.PendingSaveHashInMarker(markerFile) is { } pending) expected.Add(pending);

            var current = HashFile(sourcePath);
            if (!expected.Contains(current, StringComparer.OrdinalIgnoreCase))
                problems.Add($"the database is no longer the version {actor.DisplayName} last saw");
        }
        catch (Exception ex)
        {
            problems.Add($"the database could not be read ({ex.GetType().Name})");
        }

        if (problems.Count == 0) return;

        var what = point == "Save.AfterTmpVerify" ? "Save's rename onto the shared database" : "the restore's rename onto the shared database";
        var violation = new Violation
        {
            Step = _step, Invariant = "I4", Kind = ViolationKind.WriteWithoutLockOrOnStaleVersion,
            Key = $"I4:{_step}:{point}:{actor.Name}", Computer = actor.Name, CrashPoint = point,
            Reason = $"just before {what}, {string.Join(" and ", problems)}"
        };

        if (point == "Save.AfterTmpVerify") AddPending(violation);
        else lock (_gate) _restoreSuspects.Add(violation);
    }

    /// <summary>Reads a small text file that another process may be replacing.</summary>
    /// <param name="path">The file.</param>
    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>SHA-256 of a file's bytes, in the hex form the checkout service uses for its fingerprints.</summary>
    /// <param name="path">The file.</param>
    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
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
        Interlocked.Increment(ref _shareActivity);

        // A process without power cannot reach anything: its operation throws at once (it neither waits nor times out).
        var life = ActingLife();
        if (life is { PowerIsOut: true }) return NetworkFaultDecision.Throwing(new PowerCutException(request.Label));

        // A held operation touches the share no more than it passes crash points: its threads stand still until it is released.
        if (_pausedFlow.Value is { IsFrozen: true } held)
        {
            held.WaitForRelease(PauseLimits.SafetyWait);
            if (life is { PowerIsOut: true }) return NetworkFaultDecision.Throwing(new PowerCutException(request.Label));   // abandoned while held
        }

        var actor = life?.Owner ?? _currentActor;
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
        {
            // A file that is deleted while the copy is being made (by a thread of the process that is not the one being
            // frozen) simply was not there at that instant.
            try { Inspector.CopyShared(file, Path.Combine(destination, Path.GetFileName(file))); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

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
    /// Writes the story to the test output if nothing has yet (a walk that stopped early), removes the hooks, closes
    /// every computer's handles, restores the settings and deletes the temp folders (best effort). Does NOT check the
    /// declared findings: call <see cref="Finish"/> for that, so a failing walk's own exception is never replaced.
    /// </summary>
    public void Dispose()
    {
        if (!_outputWritten) WriteOutput(PrintStory(_promises.PendingLines()));

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

/// <summary>
/// The template database the generated runs copy instead of building a schema for every world: built ONCE per test
/// process in a private temp folder, exactly the way <see cref="World.CreateDatabase"/> builds D (a new TermPoint
/// database, then the test-only <see cref="World.EditsTable"/>, in rollback-journal mode with no sidecar files), and
/// removed when the process exits.
/// </summary>
internal static class HarnessDatabaseTemplate
{
    /// <summary>Guards the one-time build.</summary>
    private static readonly object Gate = new();

    /// <summary>The template file, once built.</summary>
    private static string? _path;

    /// <summary>The full path of the template database (built on first use).</summary>
    /// <exception cref="InvalidOperationException">The template could not be built.</exception>
    public static string Path
    {
        get
        {
            lock (Gate) return _path ??= Build();
        }
    }

    /// <summary>True when a process with this id is running (used to tell a live test host's template folder from a dead one's).</summary>
    /// <param name="processId">The process id.</param>
    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Builds the template database in this process's own temp folder and arranges for the folder to be deleted at exit (a killed host's folder is removed by the next one).</summary>
    /// <returns>The template's full path.</returns>
    /// <exception cref="InvalidOperationException">The database could not be created.</exception>
    private static string Build()
    {
        // One folder per test process (named by its process id). A test host that is killed leaves its folder behind,
        // so each process first removes the folders of processes that are no longer running.
        var temp = System.IO.Path.GetTempPath();
        foreach (var old in Directory.GetDirectories(temp, "termpoint_harness_template_*"))
        {
            var suffix = System.IO.Path.GetFileName(old)["termpoint_harness_template_".Length..];
            if (!int.TryParse(suffix, out var pid)) continue;
            if (pid == Environment.ProcessId || IsRunning(pid)) continue;
            try { Directory.Delete(old, recursive: true); } catch { /* best effort */ }
        }

        var folder = System.IO.Path.Combine(temp, $"termpoint_harness_template_{Environment.ProcessId}");
        var buildDir = System.IO.Path.Combine(folder, "build");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(buildDir);
        var target = System.IO.Path.Combine(folder, "template.db");

        var result = NewDatabaseFile.CreateAsync(target, buildDir).GetAwaiter().GetResult();
        if (!result.Succeeded)
            throw new InvalidOperationException($"The harness template database could not be created: {result.Outcome}", result.Error);

        using (var conn = new SqliteConnection($"Data Source={target};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "PRAGMA journal_mode=DELETE;" +
                $"CREATE TABLE IF NOT EXISTS {World.EditsTable}(id TEXT PRIMARY KEY, actor TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        };
        return target;
    }
}

/// <summary>
/// A lost-lock demotion in progress (invariant I6, clause b): the process it belongs to and the database (the normalised source path)
/// the session was on when the demotion's first <c>Demote.*</c> point was reached. A later point that finds the session on a different
/// database means the demotion has carried on against the wrong one (finding F16).
/// </summary>
internal sealed class DemotionRecord
{
    /// <summary>Creates the record.</summary>
    /// <param name="life">The process whose session is being demoted.</param>
    /// <param name="database">The database the session was on when the demotion started (see <c>World.DatabaseKey</c>).</param>
    public DemotionRecord(ProcessLife life, string database)
    {
        Life     = life;
        Database = database;
    }

    /// <summary>The process whose session is being demoted.</summary>
    public ProcessLife Life { get; }

    /// <summary>The database the session was on when the demotion started (lower-case full path; empty when unknown).</summary>
    public string Database { get; }
}
