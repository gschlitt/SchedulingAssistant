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
/// The shared fixture of the <see cref="SessionLifecycle"/> characterisation tests (spec item 9.1, steps 9.1b and
/// 9.1c): the temp share / working / guard folders, a real <see cref="CheckoutService"/>, <see cref="WriteLockService"/>
/// and <see cref="DatabaseInstanceGuard"/>, the recording logger, the scripted prompts, the fake host, the lifecycle
/// itself, and the helpers for building databases and leaving crash state behind. Moved verbatim out of
/// <c>SessionLifecycleTests</c> so the leave-group tests (<c>SessionLifecycleLeaveTests</c>) share it instead of forking it;
/// the only change is that members are <c>internal</c> (the fixture uses internal fakes, so they cannot be
/// <c>protected</c> on a public class). See <c>SessionLifecycleTests</c> for the full description of the set-up.
/// </summary>
public abstract class SessionLifecycleTestBase : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>The share: holds D, its lock file and any exported copy.</summary>
    internal readonly string _shareDir;

    /// <summary>The local working folder: holds D', D'' and the dirty marker.</summary>
    internal readonly string _workingDir;

    /// <summary>The single-instance guard folder (never the real per-user one).</summary>
    internal readonly string _guardDir;

    /// <summary>Protects the developer's real settings file for the lifetime of the test.</summary>
    internal readonly SettingsFileGuard _settingsGuard;

    /// <summary>The logger every service in the test writes to.</summary>
    internal readonly RecordingAppLogger _logger = new();

    /// <summary>The lock service of the session under test.</summary>
    internal readonly WriteLockService _lockSvc = new();

    /// <summary>The checkout service of the session under test.</summary>
    internal readonly CheckoutService _checkout;

    /// <summary>The single-instance guard of the process under test.</summary>
    internal readonly DatabaseInstanceGuard _guard;

    /// <summary>Guards standing in for "another TermPoint process" (disposed with the test).</summary>
    internal readonly List<DatabaseInstanceGuard> _otherGuards = new();

    /// <summary>The ordered trace of host calls, prompts, licence evaluations and saves.</summary>
    internal readonly List<string> _trace = new();

    /// <summary>Every path the lifecycle asked the licence to be evaluated for.</summary>
    internal readonly List<string> _licenseCalls = new();

    /// <summary>The scripted dialogs.</summary>
    internal readonly ScriptedPrompts _prompts;

    /// <summary>The fake window and container.</summary>
    internal readonly FakeSessionHost _host;

    /// <summary>The class under test.</summary>
    internal readonly SessionLifecycle _lifecycle;

    /// <summary>Optional hook run inside the licence evaluation (to observe the world at that moment).</summary>
    internal Action? _onLicense;

    /// <summary>The integrity fault plan the open-time gate and the shared re-check consult (null = no fault).</summary>
    internal IntegrityFaultPlan? _integrityFaults;

    /// <summary>The value a prior session writes into D' as its unsaved change.</summary>
    internal const string UnsavedValue = "unsaved-edit";

    /// <summary>The value a colleague writes into D while another session's changes are stranded.</summary>
    internal const string ColleagueValue = "someone-elses-edit";

    /// <summary>Wait budget for the lock service's background lock-file deletion to settle.</summary>
    internal static readonly TimeSpan LockSettleTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Creates the folders, protects the settings file and wires the lifecycle to its fakes.</summary>
    protected SessionLifecycleTestBase()
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
    internal void EvaluateLicense(string dbPath)
    {
        _licenseCalls.Add(dbPath);
        _onLicense?.Invoke();
        _trace.Add($"license:{Path.GetFileName(dbPath)}:guardHeld={_guard.IsHeld(dbPath)}");
    }

    /// <summary>Runs <see cref="SessionLifecycle.OpenStartupDatabaseAsync"/> with the process settings.</summary>
    /// <param name="db">The database to open.</param>
    internal Task<string?> StartupAsync(string db) => _lifecycle.OpenStartupDatabaseAsync(db, AppSettings.Current);

    /// <summary>
    /// Opens <paramref name="db"/> through the startup path with an empty script (it must ask nothing), asserts it
    /// opened, and clears the recorded calls so a following step starts from a clean slate.
    /// </summary>
    /// <param name="db">The database to open.</param>
    internal async Task OpenCleanAsync(string db)
    {
        Assert.Null(await StartupAsync(db));
        Assert.True(_host.IsDatabaseOpen);
        Assert.Equal(CheckoutMode.WriteAccess, _checkout.Mode);
        AssertScriptConsumed();
        ResetRecords();
    }

    /// <summary>Clears the trace, the recorded prompts, the licence calls and the host's recorded calls (not the files or the open connection).</summary>
    internal void ResetRecords()
    {
        _host.ClearRecords();
        _trace.Clear();
        _prompts.Calls.Clear();
        _licenseCalls.Clear();
    }

    /// <summary>Asserts the lifecycle asked nothing unexpected and consumed the whole script.</summary>
    internal void AssertScriptConsumed()
    {
        Assert.Empty(_prompts.Violations);
        Assert.Equal(0, _prompts.Remaining);
    }

    /// <summary>Asserts the exact sequence of prompts, as "Kind:Title".</summary>
    /// <param name="expected">The expected kinds and titles in order.</param>
    internal void AssertPrompts(params string[] expected)
        => Assert.Equal(expected, _prompts.Calls.Select(c => $"{c.Kind}:{c.Title}").ToArray());

    /// <summary>The host calls in the trace (lines that start with "host:").</summary>
    internal List<string> HostTrace() => _trace.Where(t => t.StartsWith("host:")).ToList();

    /// <summary>Asserts that <paramref name="expected"/> appears in <paramref name="actual"/> in this order (other lines may lie between).</summary>
    /// <param name="actual">The trace.</param>
    /// <param name="expected">The lines that must appear, in order.</param>
    internal static void AssertInOrder(IReadOnlyList<string> actual, params string[] expected)
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
    internal string NewDb(string name = "dept.db", string seed = "original")
    {
        var path = Path.Combine(_shareDir, name);
        InsertValue(path, seed);
        return path;
    }

    /// <summary>Inserts a value into the <c>_test</c> table of the database at <paramref name="dbPath"/> (creating the table first).</summary>
    /// <param name="dbPath">The database file.</param>
    /// <param name="value">The value to insert.</param>
    internal static void InsertValue(string dbPath, string value)
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
    internal static string? NewestValue(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _test ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>The dirty-marker path of a working copy.</summary>
    /// <param name="workingPath">D' or D''.</param>
    internal static string MarkerOf(string workingPath) => workingPath + ".dirty";

    /// <summary>The lock-file path beside a database.</summary>
    /// <param name="db">D.</param>
    internal static string LockOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>
    /// The path D' gets for a database: the same formula the checkout service uses
    /// (first 8 hex characters of SHA-256 of the normalised source path, an underscore, the file name, in the
    /// working folder). Duplicated here so a test can name D' before the checkout has made it; the first test that
    /// opens a database asserts it equals the real <see cref="CheckoutService.WorkingPath"/>.
    /// </summary>
    /// <param name="db">D.</param>
    internal string WorkingPathOf(string db)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(CheckoutService.NormalizeSourcePath(db))))[..8];
        return Path.Combine(_workingDir, $"{hash}_{Path.GetFileName(db)}");
    }

    /// <summary>Waits for the lock service's background lock-file deletion to finish.</summary>
    internal void SettleLocks() => _lockSvc.PendingRelease?.Wait(LockSettleTimeout);

    /// <summary>Takes the guard for <paramref name="db"/> with a second guard object — a faithful "another TermPoint process".</summary>
    /// <param name="db">The database whose guard the other process holds.</param>
    internal void HoldGuardLikeAnotherProcess(string db)
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
    internal async Task<string> LeaveKeptChangesAsync(string db, bool colleagueChangedD)
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
    internal static void WriteExternalStaleLock(string db)
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
    internal static void WriteExternalFreshLock(string db)
    {
        var now = DateTime.UtcNow;
        File.WriteAllText(LockOf(db), JsonSerializer.Serialize(new LockFileData
        {
            Username = "other_user", Machine = "OTHER-PC", SessionGuid = Guid.NewGuid().ToString(),
            Acquired = now, Heartbeat = now,
        }));
    }

}
