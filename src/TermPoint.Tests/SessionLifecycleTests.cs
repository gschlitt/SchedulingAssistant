using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Exceptions;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Characterisation tests for <see cref="SessionLifecycle"/> (spec item 9.1, step 9.1b, phase 2): they pin what the
/// open and switch groups did when they lived in <c>MainWindow</c> — the prompts the user sees (title, text,
/// buttons, in order), the order of the host calls, the result returned, and the files left on disk — so a later
/// step that changes the behaviour changes these tests visibly and on purpose.
///
/// <para><b>Real parts.</b> A real <see cref="CheckoutService"/> and <see cref="WriteLockService"/> run on private
/// temp folders (<c>_shareDir</c> stands in for the network share holding D, the lock file and exported copies;
/// <c>_workingDir</c> for the local folder holding D', D'' and the dirty marker; <c>_guardDir</c> for the
/// single-instance guard folder), a real <see cref="DatabaseInstanceGuard"/> over its own folder, and the real
/// open-time integrity gate (inside <see cref="FakeSessionHost.OpenServices"/>). The dispatcher is synchronous
/// (<c>a =&gt; a()</c>) and the logger is a <see cref="RecordingAppLogger"/>.</para>
///
/// <para><b>Fake parts.</b> <see cref="ScriptedPrompts"/> answers every dialog from a script and fails the test on
/// any dialog the script did not expect; <see cref="FakeSessionHost"/> stands in for the window and the service
/// container and records every call. Faults use the existing internal seams only
/// (<see cref="CheckoutService.FaultPlan"/>, <see cref="IntegrityFaultPlan"/>,
/// <see cref="CheckoutService.BeforeTmpVerifyAsync"/>).</para>
///
/// <para><b>Settings.</b> The lifecycle records the database in <see cref="AppSettings"/> (recent list, last path),
/// and <see cref="AppSettings.Save"/> writes the developer's real <c>settings.json</c> (there is no injectable path).
/// Every test therefore runs under a <see cref="SettingsFileGuard"/> (created in the constructor, disposed with the
/// test), which puts the real file back exactly as it was and reloads <see cref="AppSettings.Current"/>.
/// <see cref="AppSettings.Current"/> is also reset to a known state first (observer mode off, recent list empty),
/// so the result never depends on the developer's own settings.</para>
///
/// <para><b>Known-bad behaviour is pinned on purpose</b> and named with the finding id from
/// <c>designs/data-persistence-review-2026-10.md</c> section 3 (for example <c>Pins_F1</c>): the test documents
/// today's behaviour; the step that fixes the finding is expected to change it.</para>
/// </summary>
public sealed class SessionLifecycleTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>The share: holds D, its lock file and any exported copy.</summary>
    private readonly string _shareDir;

    /// <summary>The local working folder: holds D', D'' and the dirty marker.</summary>
    private readonly string _workingDir;

    /// <summary>The single-instance guard folder (never the real per-user one).</summary>
    private readonly string _guardDir;

    /// <summary>Protects the developer's real settings file for the lifetime of the test.</summary>
    private readonly SettingsFileGuard _settingsGuard;

    /// <summary>The logger every service in the test writes to.</summary>
    private readonly RecordingAppLogger _logger = new();

    /// <summary>The lock service of the session under test.</summary>
    private readonly WriteLockService _lockSvc = new();

    /// <summary>The checkout service of the session under test.</summary>
    private readonly CheckoutService _checkout;

    /// <summary>The single-instance guard of the process under test.</summary>
    private readonly DatabaseInstanceGuard _guard;

    /// <summary>Guards standing in for "another TermPoint process" (disposed with the test).</summary>
    private readonly List<DatabaseInstanceGuard> _otherGuards = new();

    /// <summary>The ordered trace of host calls, prompts, licence evaluations and saves.</summary>
    private readonly List<string> _trace = new();

    /// <summary>Every path the lifecycle asked the licence to be evaluated for.</summary>
    private readonly List<string> _licenseCalls = new();

    /// <summary>The scripted dialogs.</summary>
    private readonly ScriptedPrompts _prompts;

    /// <summary>The fake window and container.</summary>
    private readonly FakeSessionHost _host;

    /// <summary>The class under test.</summary>
    private readonly SessionLifecycle _lifecycle;

    /// <summary>Optional hook run inside the licence evaluation (to observe the world at that moment).</summary>
    private Action? _onLicense;

    /// <summary>The integrity fault plan the open-time gate and the shared re-check consult (null = no fault).</summary>
    private IntegrityFaultPlan? _integrityFaults;

    /// <summary>The value a prior session writes into D' as its unsaved change.</summary>
    private const string UnsavedValue = "unsaved-edit";

    /// <summary>The value a colleague writes into D while another session's changes are stranded.</summary>
    private const string ColleagueValue = "someone-elses-edit";

    /// <summary>Wait budget for the lock service's background lock-file deletion to settle.</summary>
    private static readonly TimeSpan LockSettleTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Creates the folders, protects the settings file and wires the lifecycle to its fakes.</summary>
    public SessionLifecycleTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir   = Path.Combine(Path.GetTempPath(), $"slc_share_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"slc_work_{id}");
        _guardDir   = Path.Combine(Path.GetTempPath(), $"slc_guard_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_workingDir);
        Directory.CreateDirectory(_guardDir);

        // Protect the real settings file FIRST, then put the in-memory settings in a known state.
        _settingsGuard = new SettingsFileGuard();
        AppSettings.Current.OpenInReaderMode = false;
        AppSettings.Current.DatabasePath     = null;
        AppSettings.Current.RecentDatabases.Clear();

        _checkout = new CheckoutService(
            lockService: _lockSvc,
            logger:      _logger,
            dispatch:    action => action(),
            workingDir:  _workingDir);
        _checkout.SaveCompleted += () => _trace.Add("checkout:SaveCompleted");

        _guard   = new DatabaseInstanceGuard(_guardDir, _logger);
        _prompts = new ScriptedPrompts(_trace.Add);
        _host    = new FakeSessionHost(_trace.Add, _logger, _checkout, () => _integrityFaults);

        _lifecycle = new SessionLifecycle(
            _checkout, _guard, () => _logger, () => AppSettings.Current,
            EvaluateLicense, () => _integrityFaults, _prompts, _host);
    }

    /// <summary>Releases everything, restores the settings file and removes the folders (best effort).</summary>
    public void Dispose()
    {
        _host.Dispose();
        _guard.Dispose();
        foreach (var g in _otherGuards) g.Dispose();
        _checkout.Dispose();
        _lockSvc.Dispose();
        SqliteConnection.ClearAllPools();
        _settingsGuard.Dispose();
        try { Directory.Delete(_shareDir,   recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_guardDir,   recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers: the lifecycle's inputs ───────────────────────────────────────

    /// <summary>
    /// The licence evaluation the lifecycle is given: records the call in the trace together with whether the
    /// single-instance guard for the path was already held at that moment (so the order guard → licence is pinned).
    /// </summary>
    /// <param name="dbPath">The database the licence is evaluated for.</param>
    private void EvaluateLicense(string dbPath)
    {
        _licenseCalls.Add(dbPath);
        _onLicense?.Invoke();
        _trace.Add($"license:{Path.GetFileName(dbPath)}:guardHeld={_guard.IsHeld(dbPath)}");
    }

    /// <summary>Runs <see cref="SessionLifecycle.OpenStartupDatabaseAsync"/> with the process settings.</summary>
    /// <param name="db">The database to open.</param>
    private Task<string?> StartupAsync(string db) => _lifecycle.OpenStartupDatabaseAsync(db, AppSettings.Current);

    /// <summary>
    /// Opens <paramref name="db"/> through the startup path with an empty script (it must ask nothing), asserts it
    /// opened, and clears the recorded calls so a following step starts from a clean slate.
    /// </summary>
    /// <param name="db">The database to open.</param>
    private async Task OpenCleanAsync(string db)
    {
        Assert.Null(await StartupAsync(db));
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        AssertScriptConsumed();
        ResetRecords();
    }

    /// <summary>Clears the trace, the recorded prompts, the licence calls and the host's recorded calls (not the files or the open connection).</summary>
    private void ResetRecords()
    {
        _host.ClearRecords();
        _trace.Clear();
        _prompts.Calls.Clear();
        _licenseCalls.Clear();
    }

    /// <summary>Asserts the lifecycle asked nothing unexpected and consumed the whole script.</summary>
    private void AssertScriptConsumed()
    {
        Assert.Empty(_prompts.Violations);
        Assert.Equal(0, _prompts.Remaining);
    }

    /// <summary>Asserts the exact sequence of prompts, as "Kind:Title".</summary>
    /// <param name="expected">The expected kinds and titles in order.</param>
    private void AssertPrompts(params string[] expected)
        => Assert.Equal(expected, _prompts.Calls.Select(c => $"{c.Kind}:{c.Title}").ToArray());

    /// <summary>The host calls in the trace (lines that start with "host:").</summary>
    private List<string> HostTrace() => _trace.Where(t => t.StartsWith("host:")).ToList();

    /// <summary>Asserts that <paramref name="expected"/> appears in <paramref name="actual"/> in this order (other lines may lie between).</summary>
    /// <param name="actual">The trace.</param>
    /// <param name="expected">The lines that must appear, in order.</param>
    private static void AssertInOrder(IReadOnlyList<string> actual, params string[] expected)
    {
        var from = 0;
        foreach (var line in expected)
        {
            var at = -1;
            for (var i = from; i < actual.Count; i++)
                if (actual[i] == line) { at = i; break; }
            Assert.True(at >= 0, $"'{line}' not found (in order, after position {from}) in:\n  " + string.Join("\n  ", actual));
            from = at + 1;
        }
    }

    // ── Helpers: files and databases ──────────────────────────────────────────

    /// <summary>Creates a valid database on the share holding one seed row in a <c>_test</c> table, and returns its path.</summary>
    /// <param name="name">File name on the share.</param>
    /// <param name="seed">The seed row's value.</param>
    private string NewDb(string name = "dept.db", string seed = "original")
    {
        var path = Path.Combine(_shareDir, name);
        InsertValue(path, seed);
        return path;
    }

    /// <summary>Inserts a value into the <c>_test</c> table of the database at <paramref name="dbPath"/> (creating the table first).</summary>
    /// <param name="dbPath">The database file.</param>
    /// <param name="value">The value to insert.</param>
    private static void InsertValue(string dbPath, string value)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _test(val TEXT); INSERT INTO _test VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the newest value of the <c>_test</c> table of the database at <paramref name="dbPath"/>, or null when empty.</summary>
    /// <param name="dbPath">The database file.</param>
    private static string? NewestValue(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>The dirty-marker path of a working copy.</summary>
    /// <param name="workingPath">D' or D''.</param>
    private static string MarkerOf(string workingPath) => workingPath + ".dirty";

    /// <summary>The lock-file path beside a database.</summary>
    /// <param name="db">D.</param>
    private static string LockOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>
    /// The path D' gets for a database: the same formula the checkout service uses
    /// (first 8 hex characters of SHA-256 of the normalised source path, an underscore, the file name, in the
    /// working folder). Duplicated here so a test can name D' before the checkout has made it; the first test that
    /// opens a database asserts it equals the real <see cref="CheckoutService.WorkingPath"/>.
    /// </summary>
    /// <param name="db">D.</param>
    private string WorkingPathOf(string db)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(CheckoutService.NormalizeSourcePath(db))))[..8];
        return Path.Combine(_workingDir, $"{hash}_{Path.GetFileName(db)}");
    }

    /// <summary>Waits for the lock service's background lock-file deletion to finish.</summary>
    private void SettleLocks() => _lockSvc.PendingRelease?.Wait(LockSettleTimeout);

    /// <summary>Takes the guard for <paramref name="db"/> with a second guard object — a faithful "another TermPoint process".</summary>
    /// <param name="db">The database whose guard the other process holds.</param>
    private void HoldGuardLikeAnotherProcess(string db)
    {
        var other = new DatabaseInstanceGuard(_guardDir, new RecordingAppLogger());
        _otherGuards.Add(other);
        Assert.Equal(InstanceGuardResult.Acquired, other.TryAcquire(db));
    }

    /// <summary>
    /// Leaves the state a crashed session leaves behind: D' holding an unsaved change, with its dirty marker, no
    /// lock (a crashed process's lock goes stale and is cleared), and — if asked — a colleague's change to D made
    /// afterwards. A SEPARATE checkout and lock service play the crashed session, so the session under test starts
    /// from nothing but the files.
    /// </summary>
    /// <param name="db">D.</param>
    /// <param name="colleagueChangedD">True when a colleague saves to D after the crash (recovery will then conflict).</param>
    /// <returns>The path of the kept D'.</returns>
    private async Task<string> LeaveKeptChangesAsync(string db, bool colleagueChangedD)
    {
        var lockSvc = new WriteLockService();
        var svc = new CheckoutService(lockSvc, new RecordingAppLogger(), action => action(), _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));
        InsertValue(svc.WorkingPath, UnsavedValue);
        svc.MarkDirty();

        lockSvc.Suspend();                 // the crashed process stops heartbeating...
        File.Delete(LockOf(db));           // ...and its lock is gone

        if (colleagueChangedD)
            InsertValue(db, ColleagueValue);

        Assert.True(File.Exists(svc.WorkingPath));
        Assert.True(File.Exists(MarkerOf(svc.WorkingPath)));
        return svc.WorkingPath;
    }

    /// <summary>Writes a lock file with a stale heartbeat beside <paramref name="db"/> (a crashed remote writer).</summary>
    /// <param name="db">D.</param>
    private static void WriteExternalStaleLock(string db)
    {
        var old = DateTime.UtcNow.AddSeconds(-(WriteLockService.StaleLockThresholdSeconds + 10));
        File.WriteAllText(LockOf(db), JsonSerializer.Serialize(new LockFileData
        {
            Username = "external_user", Machine = "REMOTE-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = old, Heartbeat = old,
        }));
    }

    /// <summary>Writes a lock file with a fresh heartbeat beside <paramref name="db"/> (a live remote writer).</summary>
    /// <param name="db">D.</param>
    private static void WriteExternalFreshLock(string db)
    {
        var now = DateTime.UtcNow;
        File.WriteAllText(LockOf(db), JsonSerializer.Serialize(new LockFileData
        {
            Username = "other_user", Machine = "OTHER-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = now, Heartbeat = now,
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // OPEN — OpenStartupDatabaseAsync
    // ═════════════════════════════════════════════════════════════════════════

    // ── 1. The single-instance guard ──────────────────────────────────────────

    /// <summary>
    /// Another live TermPoint holds the guard: the user is told, the chooser follows (Recovery mode, reason
    /// AlreadyOpen, naming the refused path) and — when they leave it — NOTHING else has happened: no sweep (an
    /// orphaned save snapshot beside D' is still there), no lock, no copy, no licence evaluation, no recent-list
    /// entry, no host call at all.
    /// </summary>
    [Fact]
    public async Task Open_GuardHeldByAnotherProcess_ShowsAlreadyOpen_ThenChooser_AndTouchesNothing()
    {
        var db = NewDb();
        HoldGuardLikeAnotherProcess(db);
        var orphan = WorkingPathOf(db) + ".savetmp";
        File.WriteAllText(orphan, "orphaned save snapshot");
        _prompts.ExpectMessage("TermPoint Is Already Open", DatabaseInstanceGuard.AlreadyOpenMessage)
                .ExpectChooser(null, $"Recovery|AlreadyOpen|{db}");

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Message:TermPoint Is Already Open", "Chooser:");
        Assert.Empty(_trace.Where(t => !t.StartsWith("prompt:")));
        Assert.True(File.Exists(orphan), "the sweep must not run while another process holds the guard");
        Assert.False(File.Exists(LockOf(db)));
        Assert.Empty(Directory.GetFiles(_workingDir).Where(f => f != orphan));
        Assert.Empty(_licenseCalls);
        Assert.DoesNotContain(db, AppSettings.Current.RecentDatabases);
        Assert.False(_guard.IsHeld(db));
        Assert.False(_host.IsDatabaseOpen);
        AssertScriptConsumed();
    }

    /// <summary>
    /// The user picks a different database in the "already open" chooser: the SAME call goes on to open that one
    /// (the guard loop returns the path to open), and the refused one is never touched.
    /// </summary>
    [Fact]
    public async Task Open_GuardHeldByAnotherProcess_ChooserPicksAnother_OpensThatOne()
    {
        var refused = NewDb("refused.db");
        var other   = NewDb("other.db");
        HoldGuardLikeAnotherProcess(refused);
        _prompts.ExpectMessage("TermPoint Is Already Open")
                .ExpectChooser(other, $"Recovery|AlreadyOpen|{refused}");

        var next = await StartupAsync(refused);

        Assert.Null(next);
        Assert.Equal(other, _host.SetupCalls.Single().DbPath);
        Assert.True(_guard.IsHeld(other));
        Assert.False(_guard.IsHeld(refused));
        Assert.Equal(new[] { other }, _licenseCalls);
        Assert.False(File.Exists(LockOf(refused)));
        Assert.True(File.Exists(LockOf(other)));
        Assert.Equal(Path.GetFullPath(other), AppSettings.Current.RecentDatabases[0]);
        AssertScriptConsumed();
    }

    // ── 2. A clean open ───────────────────────────────────────────────────────

    /// <summary>
    /// The ordinary open, with its order pinned: the guard is taken, then the licence is evaluated (while the
    /// orphaned save snapshot is still on disk), then the sweep removes it, then the checkout makes D' and takes the
    /// lock, then the services open on D', then the database name is set, then the window is wired up. Nothing is
    /// asked, nothing is saved, the window is not closed, and the database is recorded as recent.
    /// </summary>
    [Fact]
    public async Task Open_Clean_GuardThenLicenceThenSweepThenCheckoutThenServicesThenNameThenWindow()
    {
        var db = NewDb();
        var orphan = WorkingPathOf(db) + ".savetmp";
        File.WriteAllText(orphan, "orphaned save snapshot");
        bool? orphanAtLicence = null;
        _onLicense = () => orphanAtLicence = File.Exists(orphan);

        var next = await StartupAsync(db);

        Assert.Null(next);
        Assert.Equal(
            new[]
            {
                "license:dept.db:guardHeld=True",
                "host:OpenServices",
                "opened:SetDatabaseName(dept, " + db + ")",
                "host:SetupWindow",
            },
            _trace);
        Assert.True(orphanAtLicence, "the licence is evaluated before the sweep");
        Assert.False(File.Exists(orphan), "the sweep removed the orphaned save snapshot");

        // State at the moment the services were opened: checkout done, on D'.
        var open = Assert.Single(_host.OpenCalls);
        Assert.Equal(CheckoutMode.WriteAccess, open.Mode);
        Assert.Equal(_checkout.WorkingPath, open.WorkingPath);
        Assert.Equal(WorkingPathOf(db), _checkout.WorkingPath);
        Assert.Null(open.YearId);
        Assert.Null(open.SemesterIds);
        Assert.NotEqual(db, open.WorkingPath);

        Assert.Equal(db, _host.SetupCalls.Single().DbPath);
        Assert.Same(_host.Opened.Single(), _host.SetupCalls.Single().Opened);
        Assert.True(_host.IsDatabaseOpen);
        Assert.Empty(_prompts.Calls);
        Assert.True(File.Exists(_checkout.WorkingPath));
        Assert.True(File.Exists(LockOf(db)));
        Assert.True(_lockSvc.IsWriter);
        Assert.True(_guard.IsHeld(db));
        Assert.Equal(new[] { db }, _licenseCalls);
        Assert.Equal(Path.GetFullPath(db), AppSettings.Current.RecentDatabases[0]);
        Assert.DoesNotContain("host:CloseWindow", _trace);
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        AssertScriptConsumed();
    }

    // ── 3-8. Kept changes at open ─────────────────────────────────────────────

    /// <summary>
    /// Kept changes, the user chooses Restore, D is unchanged since: the recovery checkout adopts the kept D', the
    /// services open on it, and — because changes were restored — a save runs right after the window is wired up,
    /// so the restored changes reach D.
    /// </summary>
    [Fact]
    public async Task Open_KeptChanges_Restore_DUnchanged_RecoversAndSavesAfterTheWindowIsUp()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        _prompts.ExpectChoice(
            "Restore Unsaved Changes?", true,
            "Unsaved changes from a previous session with this database were kept.\n\nWould you like to restore them?");

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Restore Unsaved Changes?");
        Assert.Equal("Restore|Discard", _prompts.Calls.Single().Buttons);
        Assert.Equal(kept, _checkout.WorkingPath);                       // the SAME D' was adopted
        Assert.Equal(UnsavedValue, NewestValue(_host.OpenCalls.Single().WorkingPath));
        AssertInOrder(_trace, "host:OpenServices", "host:SetupWindow", "checkout:SaveCompleted");
        Assert.Equal(UnsavedValue, NewestValue(db));                     // the save carried the restored change into D
        Assert.False(File.Exists(MarkerOf(kept)), "after the save the changes are in D, so the marker is gone");
        Assert.True(_lockSvc.IsWriter);
        AssertScriptConsumed();
    }

    /// <summary>
    /// Kept changes, the user chooses Discard: the kept D' and its marker are deleted (no confirmation — F1 covers
    /// that Discard itself is unconfirmed) and the database then opens normally from a fresh copy of D. No save runs.
    /// </summary>
    [Fact]
    public async Task Open_KeptChanges_Discard_DeletesKeptFiles_ThenOpensNormally_Pins_F1()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        _prompts.ExpectChoice("Restore Unsaved Changes?", false);

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Restore Unsaved Changes?");
        Assert.False(File.Exists(MarkerOf(kept)));
        Assert.Equal("original", NewestValue(_host.OpenCalls.Single().WorkingPath));   // a fresh copy of D, not the kept one
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.Equal("original", NewestValue(db));
        Assert.True(_lockSvc.IsWriter);
        AssertScriptConsumed();
    }

    /// <summary>
    /// F1: closing "Restore Unsaved Changes?" with the window's X counts as the negative button, so it behaves
    /// exactly like Discard — the kept changes are deleted at once and the database opens from a fresh copy. The
    /// scripted answer is <c>false</c>, which is what the production dialog returns for the X.
    /// </summary>
    [Fact]
    public async Task Open_KeptChanges_XOnRestorePrompt_DeletesThem_Pins_F1()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        _prompts.ExpectChoice("Restore Unsaved Changes?", false /* the X */);

        await StartupAsync(db);

        // The unsaved change exists nowhere any more: not in D, not in D', not in a kept file.
        Assert.Equal("original", NewestValue(_host.OpenCalls.Single().WorkingPath));
        Assert.Equal("original", NewestValue(db));
        Assert.False(File.Exists(MarkerOf(kept)));
        Assert.Empty(Directory.GetFiles(_workingDir, "*corrupt*"));
        AssertScriptConsumed();
    }

    /// <summary>
    /// Kept changes, Restore, but D changed since the crash: the recovery cannot be applied automatically, so the
    /// "Can't Restore Unsaved Changes" choice follows; Save a Copy… opens the picker (title and suggested name from
    /// the prompt class, extension "db"), the copy is exported to the chosen path, and only then are the kept files
    /// deleted and the database opened normally.
    /// </summary>
    [Fact]
    public async Task Open_KeptChanges_Restore_DChanged_CantRestore_SaveACopy_ExportsThenOpensNormally()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: true);
        var export = Path.Combine(_shareDir, "recovered.db");
        _prompts.ExpectChoice("Restore Unsaved Changes?", true)
                .ExpectChoice("Can't Restore Unsaved Changes", true)
                .ExpectPick("Save Recovered Copy", export);

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Restore Unsaved Changes?", "Choice:Can't Restore Unsaved Changes", "PickSaveFile:Save Recovered Copy");
        Assert.Equal("Save a Copy…|Discard", _prompts.Calls[1].Buttons);
        Assert.Equal(RecoveryConflictPrompt.SuggestedFileName(db) + "|db", _prompts.Calls[2].Text);
        Assert.True(File.Exists(export));
        Assert.Equal(UnsavedValue, NewestValue(export));                         // the export holds the kept change
        Assert.False(File.Exists(MarkerOf(kept)));                               // kept files deleted only after the copy landed
        Assert.Equal(ColleagueValue, NewestValue(_host.OpenCalls.Single().WorkingPath));   // normal open: a fresh copy of the changed D

        // Quirk, pinned as it is today: `recoverUnsaved` stays true after the user chose Restore even though the
        // conflict made the checkout run again as a normal one, so the "make restored changes durable" save still
        // runs after the window is wired up. It copies the fresh D' (identical to D) back over D: needless but
        // harmless, and D still holds the colleague's change.
        AssertInOrder(_trace, "host:SetupWindow", "checkout:SaveCompleted");
        Assert.Equal(ColleagueValue, NewestValue(db));
        Assert.True(_lockSvc.IsWriter);
        AssertScriptConsumed();
    }

    /// <summary>
    /// F1: X on "Can't Restore Unsaved Changes" counts as Discard — the kept changes are deleted with no export and
    /// no confirmation, and the database opens normally.
    /// </summary>
    [Fact]
    public async Task Open_CantRestore_XOnTheChoice_DeletesTheKeptChangesWithoutExport_Pins_F1()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: true);
        _prompts.ExpectChoice("Restore Unsaved Changes?", true)
                .ExpectChoice("Can't Restore Unsaved Changes", false /* the X */);

        await StartupAsync(db);

        AssertPrompts("Choice:Restore Unsaved Changes?", "Choice:Can't Restore Unsaved Changes");
        Assert.False(File.Exists(MarkerOf(kept)));
        Assert.Equal(ColleagueValue, NewestValue(_host.OpenCalls.Single().WorkingPath));
        Assert.Empty(Directory.GetFiles(_shareDir, "*.db").Where(f => f != db));    // nothing was exported
        AssertScriptConsumed();
    }

    /// <summary>
    /// Save a Copy… with a cancelled picker does not discard: the choice is asked again, and only a copy that
    /// actually landed ends the loop.
    /// </summary>
    [Fact]
    public async Task Open_CantRestore_SaveACopy_PickerCancelled_AsksAgain_ThenExports()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: true);
        var export = Path.Combine(_shareDir, "recovered.db");
        _prompts.ExpectChoice("Restore Unsaved Changes?", true)
                .ExpectChoice("Can't Restore Unsaved Changes", true)
                .ExpectPick("Save Recovered Copy", null)                      // cancelled
                .ExpectChoice("Can't Restore Unsaved Changes", true)          // asked again
                .ExpectPick("Save Recovered Copy", export);

        await StartupAsync(db);

        AssertPrompts(
            "Choice:Restore Unsaved Changes?",
            "Choice:Can't Restore Unsaved Changes", "PickSaveFile:Save Recovered Copy",
            "Choice:Can't Restore Unsaved Changes", "PickSaveFile:Save Recovered Copy");
        Assert.Equal(UnsavedValue, NewestValue(export));
        Assert.False(File.Exists(MarkerOf(kept)));
        AssertScriptConsumed();
    }

    /// <summary>
    /// A name that is already taken is refused (never overwritten) with "Choose a New Name", and the choice is asked
    /// again; the kept changes are still there until a copy lands.
    /// </summary>
    [Fact]
    public async Task Open_CantRestore_SaveACopy_NameTaken_ShowsChooseNewName_AndAsksAgain()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: true);
        var taken  = Path.Combine(_shareDir, "taken.db");
        File.WriteAllText(taken, "somebody else's file");
        var export = Path.Combine(_shareDir, "recovered.db");
        _prompts.ExpectChoice("Restore Unsaved Changes?", true)
                .ExpectChoice("Can't Restore Unsaved Changes", true)
                .ExpectPick("Save Recovered Copy", taken)
                .ExpectMessage("Choose a New Name")
                .ExpectChoice("Can't Restore Unsaved Changes", true)
                .ExpectPick("Save Recovered Copy", export);

        await StartupAsync(db);

        Assert.Equal("somebody else's file", File.ReadAllText(taken));           // never overwritten
        Assert.Equal(UnsavedValue, NewestValue(export));
        Assert.False(File.Exists(MarkerOf(kept)));
        AssertScriptConsumed();
    }

    // ── 9-10. Kept changes that cannot be offered or restored ─────────────────

    /// <summary>
    /// F10: with "open as observer" on, recovery is never offered and never mentioned. The session opens read-only
    /// on a snapshot (D''); the kept D' and its marker are left exactly as they were; no lock is taken; nothing is
    /// asked.
    /// </summary>
    [Fact]
    public async Task Open_ObserverMode_KeptChanges_NoOffer_ReadOnly_KeptFilesUntouched_Pins_F10()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        var keptHash = DbFileTestHelpers.HashOf(kept);
        var markerBytes = File.ReadAllBytes(MarkerOf(kept));
        AppSettings.Current.OpenInReaderMode = true;

        var next = await StartupAsync(db);

        Assert.Null(next);
        Assert.Empty(_prompts.Calls);
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.NotEqual(kept, _checkout.WorkingPath);
        Assert.Equal(_checkout.ReadOnlyWorkingPath, _host.OpenCalls.Single().WorkingPath);   // opened on D'', not D'
        Assert.True(File.Exists(kept));
        Assert.Equal(keptHash, DbFileTestHelpers.HashOf(kept));
        Assert.Equal(markerBytes, File.ReadAllBytes(MarkerOf(kept)));
        Assert.False(File.Exists(LockOf(db)));
        Assert.True(_host.IsDatabaseOpen);
        AssertScriptConsumed();
    }

    /// <summary>
    /// F6: the user chooses Restore, but a live colleague holds the write lock, so the session can only be
    /// read-only. "Unsaved Changes Kept" promises the restore will be offered next time — the promise the review
    /// calls confusing. Pinned today: the message, a read-only open on D'', the kept files untouched, and no save.
    /// </summary>
    [Fact]
    public async Task Open_KeptChanges_Restore_ButLockHeldByLiveOther_UnsavedChangesKeptMessage_ReadOnly_Pins_F6()
    {
        var db = NewDb();
        var kept = await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        WriteExternalFreshLock(db);
        _prompts.ExpectChoice("Restore Unsaved Changes?", true)
                .ExpectMessage(
                    "Unsaved Changes Kept",
                    "Someone else is currently editing this database, so your unsaved changes " +
                    "can't be restored right now. They have been kept, and TermPoint will offer " +
                    "to restore them the next time you open this database with write access.");

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Restore Unsaved Changes?", "Message:Unsaved Changes Kept");
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Equal(_checkout.ReadOnlyWorkingPath, _host.OpenCalls.Single().WorkingPath);
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(MarkerOf(kept)));
        Assert.Equal(UnsavedValue, NewestValue(kept));
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.False(_lockSvc.IsWriter);
        AssertScriptConsumed();
    }

    // ── 11-12. A stale lock ───────────────────────────────────────────────────

    /// <summary>
    /// A stale lock, the user chooses "Open read-only" (or closes the dialog): the prompt names the holder, no
    /// wait is shown, the session opens read-only on D'' and the lock file is then watched so a later release can
    /// offer "Take over as writer".
    /// </summary>
    [Fact]
    public async Task Open_StaleLock_OpenReadOnly_OpensOnSnapshot_AndStartsReaderPolling()
    {
        var db = NewDb();
        WriteExternalStaleLock(db);
        _prompts.ExpectChoice("Database Lock Is Inactive", false);

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Database Lock Is Inactive");
        Assert.Equal("Take over|Open read-only", _prompts.Calls.Single().Buttons);
        Assert.Contains("external_user on REMOTE-PC", _prompts.Calls.Single().Text);
        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Equal(_checkout.ReadOnlyWorkingPath, _host.OpenCalls.Single().WorkingPath);
        Assert.True(_lockSvc.IsPolling, "the lock file is watched so 'Take over as writer' can be offered later");
        Assert.False(_lockSvc.IsWriter);
        Assert.Equal("external_user", JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockOf(db)))!.Username);
        Assert.True(_host.IsDatabaseOpen);
        AssertScriptConsumed();
    }

    /// <summary>
    /// A stale lock, the user chooses "Take over": because the holder's heartbeat has not yet been seen standing
    /// still for the whole confirmation window, the cancellable wait is shown (with the remaining seconds); when it
    /// runs to the end the lock is broken and the session opens with write access on D'.
    /// </summary>
    [Fact]
    public async Task Open_StaleLock_TakeOver_ShowsTheConfirmationWait_ThenOpensWithWriteAccess()
    {
        var db = NewDb();
        WriteExternalStaleLock(db);
        _prompts.ExpectChoice("Database Lock Is Inactive", true)
                .ExpectStaleLockWait(true);

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Database Lock Is Inactive", "StaleLockWait:");
        Assert.InRange(int.Parse(_prompts.Calls[1].Text), 1, WriteLockService.StaleConfirmationSeconds);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.Equal(_checkout.WorkingPath, _host.OpenCalls.Single().WorkingPath);
        Assert.True(_lockSvc.IsWriter);
        Assert.NotEqual("external_user", JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockOf(db)))!.Username);
        AssertScriptConsumed();
    }

    /// <summary>Cancelling (or closing) the takeover wait is exactly "Open read-only": the lock is left alone.</summary>
    [Fact]
    public async Task Open_StaleLock_TakeOver_WaitCancelled_BehavesLikeOpenReadOnly()
    {
        var db = NewDb();
        WriteExternalStaleLock(db);
        _prompts.ExpectChoice("Database Lock Is Inactive", true)
                .ExpectStaleLockWait(false);

        await StartupAsync(db);

        Assert.Equal(CheckoutMode.ReadOnly, _checkout.Mode);
        Assert.Equal(_checkout.ReadOnlyWorkingPath, _host.OpenCalls.Single().WorkingPath);
        Assert.True(_lockSvc.IsPolling);
        Assert.Equal("external_user", JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockOf(db)))!.Username);
        AssertScriptConsumed();
    }

    // ── 13-14. The checkout cannot proceed ────────────────────────────────────

    /// <summary>
    /// The share is unreachable (injected through the checkout fault plan): "Network Unreachable" is shown, the
    /// failed attempt is forgotten (the checkout no longer names a source or working copy) and the window is
    /// closed — the startup path has nothing to fall back to. No services are ever opened. The guard taken for the
    /// database is NOT given back on this path (the application is closing).
    /// </summary>
    [Fact]
    public async Task Open_ShareUnreachable_ShowsMessage_ResetsTheCheckout_AndClosesTheWindow()
    {
        var db = NewDb();
        _checkout.FaultPlan = CheckoutFaultPlan.Parse("unreachable");
        _prompts.ExpectMessage(
            "Network Unreachable", "Cannot reach the database — check your network connection and try again.");

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Message:Network Unreachable");
        Assert.Equal(new[] { "host:CloseWindow" }, HostTrace());
        Assert.Empty(_host.OpenCalls);
        Assert.Equal(string.Empty, _checkout.SourcePath);
        Assert.Equal(string.Empty, _checkout.WorkingPath);
        Assert.False(_host.IsDatabaseOpen);
        Assert.True(_guard.IsHeld(db));
        Assert.False(File.Exists(LockOf(db)));
        AssertScriptConsumed();
    }

    /// <summary>
    /// D is missing when the checkout looks (the startup path does no pre-check of its own): "Database Not Found"
    /// naming the path, the failed attempt forgotten, the window closed. Nothing is created or locked.
    /// </summary>
    [Fact]
    public async Task Open_SourceMissing_ShowsDatabaseNotFound_AndClosesTheWindow()
    {
        var db = Path.Combine(_shareDir, "gone.db");
        _prompts.ExpectMessage("Database Not Found", DatabaseSwitchPrecheck.MissingMessage(db));

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Message:Database Not Found");
        Assert.Equal(new[] { "host:CloseWindow" }, HostTrace());
        Assert.Equal(string.Empty, _checkout.SourcePath);
        Assert.False(File.Exists(db));
        Assert.False(File.Exists(LockOf(db)));
        Assert.Empty(Directory.GetFiles(_workingDir));
        AssertScriptConsumed();
    }

    // ── 15-17. The open-time integrity check of the local copy ────────────────

    /// <summary>
    /// The local copy is held open by another program on all three attempts: "Couldn't Open the Database" with
    /// Try again / Close; Close gives up WITHOUT saving — the half-built services are released, the lock is given
    /// back, the unedited D' is deleted and forgotten — and the window is closed.
    /// </summary>
    [Fact]
    public async Task Open_LocalCopyInUse_Close_EndsTheSessionWithoutSaving_AndClosesTheWindow()
    {
        var db = NewDb();
        _integrityFaults = IntegrityFaultPlan.Parse("locked:3");
        _prompts.ExpectChoice(
            "Couldn't Open the Database", false, LocalCopyCheckPrompt.InUseMessage);

        var next = await StartupAsync(db);
        SettleLocks();

        Assert.Null(next);
        AssertPrompts("Choice:Couldn't Open the Database");
        Assert.Equal("Try again|Close", _prompts.Calls.Single().Buttons);
        Assert.Equal(
            new[] { "host:OpenServices", "host:ReleaseServices(built=True, connection=False)", "host:CloseWindow" },
            HostTrace());
        Assert.Empty(_host.SetupCalls);
        Assert.False(_host.IsDatabaseOpen);
        Assert.Equal(string.Empty, _checkout.WorkingPath);
        Assert.False(File.Exists(WorkingPathOf(db)), "an unedited D' is deleted");
        Assert.False(File.Exists(LockOf(db)), "the lock is given back");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        AssertScriptConsumed();
    }

    /// <summary>"Try again" on the in-use prompt runs the open again (with the gate's quiet retries); this time it passes.</summary>
    [Fact]
    public async Task Open_LocalCopyInUse_TryAgain_OpensOnTheSecondRun()
    {
        var db = NewDb();
        _integrityFaults = IntegrityFaultPlan.Parse("locked:3");     // the first whole open fails, the next passes
        _prompts.ExpectChoice("Couldn't Open the Database", true);

        var next = await StartupAsync(db);

        Assert.Null(next);
        Assert.Equal(2, _host.OpenCalls.Count);
        Assert.True(_host.IsDatabaseOpen);
        Assert.DoesNotContain("host:CloseWindow", _trace);
        Assert.True(_lockSvc.IsWriter);
        AssertScriptConsumed();
    }

    /// <summary>
    /// The local copy is damaged once and the shared database is fine: with nothing to lose, ONE fresh copy is made
    /// and the open runs again — silently (no dialog at all), logging that it opened with a fresh copy.
    /// </summary>
    [Fact]
    public async Task Open_LocalCopyDamagedOnce_SharedFine_MakesOneFreshCopy_AndOpensSilently()
    {
        var db = NewDb();
        _integrityFaults = IntegrityFaultPlan.Parse("damaged:1");

        var next = await StartupAsync(db);

        Assert.Null(next);
        Assert.Empty(_prompts.Calls);
        Assert.Equal(
            new[]
            {
                "host:OpenServices", "host:ReleaseServices(built=True, connection=False)",
                "host:OpenServices", "host:SetupWindow",
            },
            HostTrace());
        Assert.Contains(_logger.Infos, l => l == "LocalCopyCheck: the database opened normally with a fresh copy.");
        Assert.True(_host.IsDatabaseOpen);
        Assert.True(_lockSvc.IsWriter);
        Assert.Equal("original", NewestValue(_host.OpenCalls[1].WorkingPath));
        AssertScriptConsumed();
    }

    /// <summary>
    /// The user restored kept changes and the adopted copy turns out to be damaged: the damaged file is set aside
    /// (kept, with the unsaved change still inside it), "Unsaved Changes Lost" says so honestly, and the open goes
    /// on with a fresh copy of D — which does not contain the lost change.
    /// </summary>
    [Fact]
    public async Task Open_DamagedCopyHoldingRecoveredChanges_IsSetAside_UnsavedChangesLostMessage_ThenOpensFresh()
    {
        var db = NewDb();
        await LeaveKeptChangesAsync(db, colleagueChangedD: false);
        _integrityFaults = IntegrityFaultPlan.Parse("damaged:1");
        _prompts.ExpectChoice("Restore Unsaved Changes?", true)
                .ExpectMessage("Unsaved Changes Lost", LocalCopyCheckPrompt.UnsavedChangesLostMessage);

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Choice:Restore Unsaved Changes?", "Message:Unsaved Changes Lost");
        var archived = Assert.Single(Directory.GetFiles(_workingDir, "*.db.corrupt-*").Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm")));
        Assert.Equal(UnsavedValue, NewestValue(archived));
        Assert.Equal("original", NewestValue(_host.OpenCalls.Last().WorkingPath));
        Assert.Equal("original", NewestValue(db));
        Assert.True(_host.IsDatabaseOpen);
        AssertScriptConsumed();
    }

    // ── 18. The folder refuses writes ─────────────────────────────────────────

    /// <summary>
    /// The services cannot be built because the database's folder refuses writes: the user is told, offered the
    /// technical details for IT (Yes shows them), and the application is shut down. Pinned as today: this path does
    /// NOT release the services, reset the checkout, give back the lock or close the window — only the shutdown —
    /// and the window is never wired up.
    /// </summary>
    [Fact]
    public async Task Open_FolderNotWritable_MessageThenTechnicalDetailsYes_ThenShutsDown()
    {
        var db = NewDb();
        _host.OpenFailures.Enqueue(new DatabaseFolderNotWritableException(db, "The folder is protected.", "IT detail text", null));
        _prompts.ExpectMessage("Can't Save to This Folder", "The folder is protected.")
                .ExpectChoice("Technical Details", true, "Would you like to see technical details for your IT support?")
                .ExpectMessage("Technical Details (for IT)", "IT detail text");

        var next = await StartupAsync(db);

        Assert.Null(next);
        AssertPrompts("Message:Can't Save to This Folder", "Choice:Technical Details", "Message:Technical Details (for IT)");
        Assert.Equal("Yes|No", _prompts.Calls[1].Buttons);
        Assert.Equal(new[] { "host:OpenServices", "host:ShutDown" }, HostTrace());
        Assert.Empty(_host.SetupCalls);
        Assert.False(_host.IsDatabaseOpen);
        AssertScriptConsumed();
    }

    /// <summary>Answering No to the technical-details question skips the IT message; the application still shuts down.</summary>
    [Fact]
    public async Task Open_FolderNotWritable_TechnicalDetailsNo_SkipsTheItMessage_ThenShutsDown()
    {
        var db = NewDb();
        _host.OpenFailures.Enqueue(new DatabaseFolderNotWritableException(db, "The folder is protected.", "IT detail text", null));
        _prompts.ExpectMessage("Can't Save to This Folder")
                .ExpectChoice("Technical Details", false);

        await StartupAsync(db);

        AssertPrompts("Message:Can't Save to This Folder", "Choice:Technical Details");
        Assert.Equal(new[] { "host:OpenServices", "host:ShutDown" }, HostTrace());
        AssertScriptConsumed();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SWITCH — SwitchDatabaseAsync
    // ═════════════════════════════════════════════════════════════════════════

    // ── 19-20. Refusals before anything is touched ────────────────────────────

    /// <summary>
    /// The pre-check refuses a missing target: "Database Not Found" naming the path, the result is false, and the
    /// open database is untouched — still open, still checked out with its working copy and lock, its guard still
    /// held, the target's guard given back, the settings not pointed at the target.
    /// </summary>
    [Fact]
    public async Task Switch_PrecheckRefusesMissingTarget_ShowsMessage_AndStaysOnTheCurrentDatabase()
    {
        var a = NewDb("a.db");
        var missing = Path.Combine(_shareDir, "missing.db");
        await OpenCleanAsync(a);
        var aWorking = _checkout.WorkingPath;
        _prompts.ExpectMessage("Database Not Found", DatabaseSwitchPrecheck.MissingMessage(missing));

        var opened = await _lifecycle.SwitchDatabaseAsync(missing);

        Assert.False(opened);
        AssertPrompts("Message:Database Not Found");
        Assert.Empty(HostTrace());
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.Equal(a, _checkout.SourcePath);
        Assert.True(File.Exists(aWorking));
        Assert.True(File.Exists(LockOf(a)));
        Assert.True(_lockSvc.IsWriter);
        Assert.True(_guard.IsHeld(a));
        Assert.False(_guard.IsHeld(missing));
        Assert.NotEqual(missing, AppSettings.Current.DatabasePath);
        AssertScriptConsumed();
    }

    /// <summary>
    /// The target's guard is held by another TermPoint process: "TermPoint Is Already Open", the result is false and
    /// the open database stays exactly as it was (no chooser, because a database is open).
    /// </summary>
    [Fact]
    public async Task Switch_TargetHeldByAnotherProcess_ShowsAlreadyOpen_AndStays()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db");
        await OpenCleanAsync(a);
        HoldGuardLikeAnotherProcess(b);
        _prompts.ExpectMessage("TermPoint Is Already Open", DatabaseInstanceGuard.AlreadyOpenMessage);

        var opened = await _lifecycle.SwitchDatabaseAsync(b);

        Assert.False(opened);
        AssertPrompts("Message:TermPoint Is Already Open");
        Assert.Empty(HostTrace());
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(a, _checkout.SourcePath);
        Assert.True(_guard.IsHeld(a));
        Assert.False(_guard.IsHeld(b));
        Assert.Empty(_licenseCalls);
        AssertScriptConsumed();
    }


    // ── 21. A normal switch ───────────────────────────────────────────────────

    /// <summary>
    /// The ordinary switch from A (edited) to B, with its order pinned: the old session is saved into A, the
    /// semester selection captured, the backup timer stopped, the services closed, A's working copy and marker
    /// deleted (their content is safely in A), A's guard given back, the licence evaluated for B, B checked out, the
    /// services opened on B' with the captured selection passed along, the name set, the window wired up, and the
    /// selection restored on the opened database. Returns true; nothing is asked.
    /// </summary>
    [Fact]
    public async Task Switch_Normal_SavesTheOldSession_CleansItUp_OpensTheNewOne_AndRestoresTheSelection()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db", "b-original");
        await OpenCleanAsync(a);
        var aWorking = _checkout.WorkingPath;
        _host.Edit("edit-in-a");
        _host.CapturedSelection = ("year-1", new HashSet<string> { "sem-1", "sem-2" });
        ResetRecords();

        var opened = await _lifecycle.SwitchDatabaseAsync(b);
        SettleLocks();

        Assert.True(opened);
        Assert.Empty(_prompts.Calls);
        Assert.Equal(
            new[]
            {
                "checkout:SaveCompleted",
                "host:TryCaptureSemesterSelection(captured=True)",
                "host:StopBackupSession",
                "host:ReleaseServices(built=True, connection=True)",
                "license:b.db:guardHeld=True",
                "host:OpenServices",
                "opened:SetDatabaseName(b, " + b + ")",
                "host:SetupWindow",
                "opened:RestoreSemesterSelection(year-1, sem-1,sem-2)",
            },
            _trace);

        // The old session: saved into A, then cleaned up.
        Assert.Equal("edit-in-a", NewestValue(a));
        Assert.False(File.Exists(aWorking));
        Assert.False(File.Exists(MarkerOf(aWorking)));
        Assert.False(File.Exists(LockOf(a)));

        // The new session.
        var open = Assert.Single(_host.OpenCalls);
        Assert.Equal("year-1", open.YearId);
        Assert.Equal(new HashSet<string> { "sem-1", "sem-2" }, open.SemesterIds);
        Assert.Equal(_checkout.WorkingPath, open.WorkingPath);
        Assert.Equal(b, _checkout.SourcePath);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.True(File.Exists(LockOf(b)));
        Assert.True(_host.IsDatabaseOpen);
        Assert.False(_guard.IsHeld(a));
        Assert.True(_guard.IsHeld(b));
        Assert.Equal(b, AppSettings.Current.DatabasePath);
        Assert.Equal(Path.GetFullPath(b), AppSettings.Current.RecentDatabases[0]);
        AssertScriptConsumed();
    }

    // ── 22. The old session cannot be saved ───────────────────────────────────

    /// <summary>
    /// F6: the old session's final save fails (the network copy is lost between the copy and the verify): "Changes
    /// Not Saved" promises the changes will be offered back — the promise the review calls confusing. Pinned today:
    /// the message comes after the services are closed, A's working copy and marker are KEPT, A itself is unchanged,
    /// A's lock file is left in place (parked), and the switch still goes on to open B.
    /// </summary>
    [Fact]
    public async Task Switch_OldSessionSaveFails_ChangesNotSavedMessage_KeepsTheOldWorkingCopy_ThenOpensTheNewOne_Pins_F6()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db");
        await OpenCleanAsync(a);
        var aWorking = _checkout.WorkingPath;
        _host.Edit("edit-in-a");
        _checkout.BeforeTmpVerifyAsync = path => { File.Delete(path); return Task.CompletedTask; };
        ResetRecords();
        _prompts.ExpectMessage(
            "Changes Not Saved",
            "Your latest changes to \"a\" could not be saved. " +
            "They have been kept, and TermPoint will offer to restore them " +
            "the next time you open that database.");

        var opened = await _lifecycle.SwitchDatabaseAsync(b);
        _checkout.BeforeTmpVerifyAsync = null;

        Assert.True(opened);
        AssertPrompts("Message:Changes Not Saved");
        AssertInOrder(_trace, "host:ReleaseServices(built=True, connection=True)", "prompt:Message:Changes Not Saved", "host:OpenServices");
        Assert.DoesNotContain("checkout:SaveCompleted", _trace);
        Assert.True(File.Exists(aWorking));
        Assert.True(File.Exists(MarkerOf(aWorking)));
        Assert.Equal("edit-in-a", NewestValue(aWorking));
        Assert.Equal("original", NewestValue(a));
        Assert.True(File.Exists(LockOf(a)), "the lock is parked, not deleted");
        Assert.Equal(b, _checkout.SourcePath);
        Assert.True(_host.IsDatabaseOpen);
        Assert.False(_guard.IsHeld(a));
        AssertScriptConsumed();
    }

    // ── 23. A failure after the old database was closed ───────────────────────

    /// <summary>
    /// The target turns out unreachable only at checkout, after A was already saved and closed: "Network
    /// Unreachable" is shown, the failed switch is cleaned up (services released, checkout reset, every guard given
    /// back), the window is covered, and the chooser appears in Recovery mode (reason SwitchFailed, naming B). The
    /// user picks C, which opens through the same switch code. The call still returns FALSE — the original request
    /// failed even though another database is now open.
    /// </summary>
    [Fact]
    public async Task Switch_TargetUnreachableAtCheckout_AbandonsTheSwitch_ChooserOffered_PickOpensAnotherDatabase()
    {
        var a = NewDb("a.db");
        var b = NewDb("b.db");
        var c = NewDb("c.db");
        await OpenCleanAsync(a);
        _checkout.FaultPlan = CheckoutFaultPlan.Parse("unreachable:1");
        _prompts.ExpectMessage("Network Unreachable", "Cannot reach the database — check your network connection and try again.")
                .ExpectChooser(c, $"Recovery|SwitchFailed|{b}");

        var opened = await _lifecycle.SwitchDatabaseAsync(b);
        SettleLocks();

        Assert.False(opened);
        AssertPrompts("Message:Network Unreachable", "Chooser:");
        AssertInOrder(
            HostTrace(),
            "host:ReleaseServices(built=True, connection=True)",     // A torn down
            "host:ReleaseServices(built=False, connection=False)",   // the abandoned attempt cleaned up
            "host:CoverWindowForNoDatabase",                           // the window is covered before the chooser
            "host:OpenServices",                                       // C
            "host:SetupWindow");
        AssertInOrder(_trace, "prompt:Message:Network Unreachable", "host:CoverWindowForNoDatabase", "prompt:Chooser:");
        Assert.Equal(c, _host.SetupCalls.Single().DbPath);
        Assert.Equal(c, _checkout.SourcePath);
        Assert.True(_host.IsDatabaseOpen);
        Assert.False(_guard.IsHeld(a));
        Assert.False(_guard.IsHeld(b));
        Assert.True(_guard.IsHeld(c));
        Assert.Contains(Path.GetFullPath(b), AppSettings.Current.RecentDatabases);   // recorded by the pre-check, before the failure
        Assert.Equal(Path.GetFullPath(c), AppSettings.Current.RecentDatabases[0]);
        AssertScriptConsumed();
    }

    // ── 24. File → New ────────────────────────────────────────────────────────

    /// <summary>
    /// File → New: the target does not exist and the caller says createIfMissing, so the old session is closed, the
    /// database is created on the share, and it is opened for EDITING even though "open as observer" is on (a
    /// database the user has just created must be writable straight away). Returns true.
    /// </summary>
    [Fact]
    public async Task Switch_CreateIfMissing_CreatesTheDatabase_ThenOpensItForEditing_EvenInObserverMode()
    {
        var a = NewDb("a.db");
        var created = Path.Combine(_shareDir, "brand-new.db");
        await OpenCleanAsync(a);
        AppSettings.Current.OpenInReaderMode = true;

        var opened = await _lifecycle.SwitchDatabaseAsync(created, createIfMissing: true);
        SettleLocks();

        Assert.True(opened);
        Assert.Empty(_prompts.Calls);
        Assert.True(File.Exists(created));
        Assert.Equal(created, _checkout.SourcePath);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        Assert.True(_lockSvc.IsWriter);
        Assert.Equal(_checkout.WorkingPath, _host.OpenCalls.Single().WorkingPath);
        AssertInOrder(_trace, "host:StopBackupSession", "host:ReleaseServices(built=True, connection=True)",
            "license:brand-new.db:guardHeld=True", "host:OpenServices", "host:SetupWindow");
        Assert.Equal(Path.GetFullPath(created), AppSettings.Current.RecentDatabases[0]);
        AssertScriptConsumed();
    }

    /// <summary>File → New never adopts an existing file: the pre-check refuses it ("Database Already Exists") and the open database stays.</summary>
    [Fact]
    public async Task Switch_CreateIfMissing_TargetAlreadyExists_Refuses_AndStays()
    {
        var a = NewDb("a.db");
        var existing = NewDb("existing.db");
        await OpenCleanAsync(a);
        _prompts.ExpectMessage("Database Already Exists", DatabaseSwitchPrecheck.AlreadyExistsMessage(existing));

        var opened = await _lifecycle.SwitchDatabaseAsync(existing, createIfMissing: true);

        Assert.False(opened);
        Assert.Empty(HostTrace());
        Assert.Equal(a, _checkout.SourcePath);
        Assert.True(_host.IsDatabaseOpen);
        Assert.False(_guard.IsHeld(existing));
        AssertScriptConsumed();
    }

    // ── 25. The save-conflict resolution path ─────────────────────────────────

    /// <summary>
    /// The save-conflict resolution (spec item 10, step 10a) is a switch to the SAME database with a callback: the
    /// old session is released WITHOUT saving (D unchanged), its services — and so its connection to the changes —
    /// are closed, and the callback then runs INSTEAD of the usual cleanup. At that moment D' and its marker still
    /// exist (the lifecycle deleted nothing). Here the callback discards the changes, after which the database is
    /// reopened normally from a fresh copy of D.
    /// </summary>
    [Fact]
    public async Task Switch_ResolveKeptChanges_ReleasesWithoutSaving_RunsTheCallbackInsteadOfCleanup_ThenReopens()
    {
        var a = NewDb("a.db");
        await OpenCleanAsync(a);
        var aWorking = _checkout.WorkingPath;
        _host.Edit("kept-edit");
        ResetRecords();

        string? seenSource = null;
        string? dAtCallback = null;
        bool workingExists = false, markerExists = false, connectionOpen = true;
        CheckoutMode? modeAtCallback = null;
        List<string>? traceAtCallback = null;
        Task Callback(string source)
        {
            seenSource      = source;
            dAtCallback     = NewestValue(a);
            workingExists   = File.Exists(aWorking);
            markerExists    = File.Exists(MarkerOf(aWorking));
            connectionOpen  = _host.ConnectionOpen;
            modeAtCallback  = _checkout.Mode;
            traceAtCallback = _trace.ToList();
            _checkout.DiscardCrash(source);          // the user chose Discard Changes
            return Task.CompletedTask;
        }

        var opened = await _lifecycle.SwitchDatabaseAsync(a, resolveKeptChanges: Callback);
        SettleLocks();

        Assert.True(opened);
        Assert.Equal(a, seenSource);
        Assert.Equal("original", dAtCallback);                       // nothing was saved
        Assert.True(workingExists);                                  // D' still there: the lifecycle deleted nothing
        Assert.True(markerExists);
        Assert.False(connectionOpen);                                // services closed before the callback
        Assert.Equal(CheckoutMode.ReadOnly, modeAtCallback);         // the session was released
        Assert.Contains("host:ReleaseServices(built=True, connection=True)", traceAtCallback!);
        Assert.DoesNotContain("checkout:SaveCompleted", traceAtCallback!);
        Assert.Empty(_prompts.Calls);
        Assert.Equal("original", NewestValue(a));
        Assert.Equal("original", NewestValue(_host.OpenCalls.Single().WorkingPath));   // reopened from a fresh copy of D
        Assert.False(File.Exists(MarkerOf(_checkout.WorkingPath)));
        Assert.True(_guard.IsHeld(a));
        Assert.True(_host.IsDatabaseOpen);
        AssertScriptConsumed();
    }

    /// <summary>
    /// If the callback leaves the changes alone (for example a failed export), the lifecycle does not delete them
    /// either: the reopen's crash recovery finds D' and its marker and OFFERS THEM AGAIN. Restoring adopts them, and
    /// the restored changes are then saved into D.
    /// </summary>
    [Fact]
    public async Task Switch_ResolveKeptChanges_CallbackLeavesTheChanges_TheReopenOffersThemAgain()
    {
        var a = NewDb("a.db");
        await OpenCleanAsync(a);
        _host.Edit("kept-edit");
        ResetRecords();
        _prompts.ExpectChoice("Restore Unsaved Changes?", true);

        var opened = await _lifecycle.SwitchDatabaseAsync(a, resolveKeptChanges: _ => Task.CompletedTask);

        Assert.True(opened);
        AssertPrompts("Choice:Restore Unsaved Changes?");
        Assert.Equal("kept-edit", NewestValue(_host.OpenCalls.Single().WorkingPath));
        Assert.Equal("kept-edit", NewestValue(a));                   // saved right after the reopen
        AssertScriptConsumed();
    }
}
