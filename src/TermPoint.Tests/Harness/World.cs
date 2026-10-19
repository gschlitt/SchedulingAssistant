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
/// <para><b>Observers.</b> <see cref="CrashPoints.Hook"/> is installed as an OBSERVER: it never throws on its own. At
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

    /// <summary>Number of top-level funnel steps started so far; the number of the current step.</summary>
    private int _step;

    /// <summary>How many funnel calls are running inside each other (0 between steps).</summary>
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

    /// <summary>The ledger at the end of the previous step (what things looked like before the current step).</summary>
    public Ledger LastLedger => _last;

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
        var nested = _depth > 0;
        if (!nested) BeginStep();
        var entry = _story.Add(_step, nested ? 1 : 0, storyLine);

        _depth++;
        T result;
        try { result = body(); }
        catch (Exception ex)
        {
            entry.Text += $" — it threw {ex.GetType().Name}";
            throw;
        }
        finally { _depth--; }

        if (describe is not null) entry.Text += " — " + describe(result);
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
        var nested = _depth > 0;
        if (!nested) BeginStep();
        var entry = _story.Add(_step, nested ? 1 : 0, storyLine);

        _depth++;
        T result;
        try { result = await body(); }
        catch (Exception ex)
        {
            entry.Text += $" — it threw {ex.GetType().Name}";
            throw;
        }
        finally { _depth--; }

        if (describe is not null) entry.Text += " — " + describe(result);
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
        => Do(null, $"{computer.DisplayName}'s computer crashed", () => CrashCore(computer));

    /// <summary>The body of <see cref="CrashComputer"/> (see there).</summary>
    /// <param name="computer">The computer whose process is killed.</param>
    private void CrashCore(Computer computer)
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
    /// Called when a banner appears on a computer's screen (the lock-lost banner, a save-failure banner): writes it into
    /// the story and hands it to the promise tracker.
    /// </summary>
    /// <param name="computer">The computer whose screen shows the banner.</param>
    /// <param name="text">The banner text.</param>
    public void ObserveBanner(Computer computer, string text)
    {
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
    /// The <see cref="CrashPoints.Hook"/>: runs the walk's extra code for the point (once), then, at the points just before
    /// a rename onto the shared database, checks invariant I4. Never throws by itself.
    /// </summary>
    /// <param name="name">The crash point being passed.</param>
    private void OnCrashPoint(string name)
    {
        List<(Action Action, string StoryLine)>? due = null;
        lock (_gate)
        {
            if (_crashPointActions.TryGetValue(name, out var list) && list.Count > 0)
            {
                due = list.ToList();
                list.Clear();
            }
        }

        if (due is not null)
        {
            foreach (var (action, storyLine) in due)
            {
                _story.Add(_step, 1, storyLine);
                action();
            }
        }

        if (name is "Save.AfterTmpVerify" or "Restore.AfterTmpVerified") CheckWriterAtRename(name);
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
        var actor = _currentActor;
        if (actor is null) return;

        var problems = new List<string>();

        // The lock must be the saver's.
        try
        {
            if (!File.Exists(LockFilePath))
            {
                problems.Add("the lock file does not exist");
            }
            else
            {
                var json = ReadSharedText(LockFilePath);
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
            if (actor.PendingSaveHashInMarker() is { } pending) expected.Add(pending);

            var current = HashFile(DatabasePath);
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
