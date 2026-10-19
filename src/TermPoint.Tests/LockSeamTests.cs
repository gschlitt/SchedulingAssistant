using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the three test-only seams of spec step 9.2a, which let several simulated computers share one
/// test process: (1) a per-service <see cref="LockIdentity"/> (machine, user, process id, start time and
/// process lookup), (2) every time the lock service reads or stamps going through its
/// <see cref="TimeProvider"/>, and (3) <c>startTimers: false</c> plus the <c>Tick*</c> methods, which run one
/// timer body on demand with no background timer. Also pins that the DEFAULTS still reproduce the real
/// <see cref="Environment"/> values and the real timers, since that is what the app uses.
///
/// <para>Each test uses its own temporary folder and a <see cref="FakeTimeProvider"/>, so minutes of lock
/// staleness take no real time.</para>
/// </summary>
public sealed class LockSeamTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _workingDir;

    /// <summary>The fixed instant the fake clock starts at.</summary>
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A process start time used by the simulated holders.</summary>
    private static readonly DateTime HolderStart = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates isolated temporary folders for the database and for working copies.</summary>
    public LockSeamTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"lst_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"lst_wrk_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Deletes both temporary folders (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir,    recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a real, empty SQLite database D and returns its path.</summary>
    private string CreateDb()
    {
        var db = Path.Combine(_tempDir, "test.db");
        using var conn = new SqliteConnection($"Data Source={db};Pooling=False");
        conn.Open();
        return db;
    }

    /// <summary>Path of the lock file beside <paramref name="db"/>.</summary>
    private static string LockFileOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>Reads and parses the lock file beside <paramref name="db"/>.</summary>
    private static LockFileData ReadLock(string db)
        => JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockFileOf(db)))!;

    /// <summary>
    /// A simulated process for <see cref="LockIdentity.LookupProcess"/>: reports a fixed start time, or throws
    /// when that time is null (the "start time unreadable" case).
    /// </summary>
    private sealed class FakeProcess : ILockProcess
    {
        private readonly DateTime? _start;

        /// <summary>Creates a fake process.</summary>
        /// <param name="start">The start time it reports; null makes <see cref="StartTimeUtc"/> throw.</param>
        public FakeProcess(DateTime? start) => _start = start;

        /// <inheritdoc/>
        public DateTime StartTimeUtc => _start ?? throw new InvalidOperationException("start time unreadable");

        /// <inheritdoc/>
        public void Dispose() { }
    }

    /// <summary>
    /// Builds an identity for a simulated computer.
    /// </summary>
    /// <param name="machine">Machine name.</param>
    /// <param name="user">User name.</param>
    /// <param name="pid">Process id.</param>
    /// <param name="lookup">How this computer finds other processes; null means "never called".</param>
    private static LockIdentity Identity(string machine, string user, int pid, Func<int, ILockProcess>? lookup = null)
        => new(machine, user, pid, HolderStart, lookup ?? (p => throw new InvalidOperationException($"unexpected process lookup for {p}")));

    /// <summary>Creates a lock service with a timer-less, fake-clock configuration.</summary>
    private static WriteLockService NewService(FakeTimeProvider clock, LockIdentity identity)
        => new(clock, identity, startTimers: false);

    /// <summary>Reads a private field of a lock service by reflection (test code only).</summary>
    private static object? Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);

    // ── Seam 1: identity ──────────────────────────────────────────────────────

    /// <summary>
    /// A holder on another machine is never inspected as a process; it counts as live while its heartbeat is
    /// fresh and becomes stale only after fake time passes the threshold with no renewal.
    /// </summary>
    [Fact]
    public void DifferentMachines_SecondSeesFirstAsLiveOtherMachine_ThenStale()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var laptop  = NewService(clock, Identity("LAPTOP",  "bob", 100));
        using var desktop = NewService(clock, Identity("DESKTOP", "bob", 100)); // same PID on purpose

        laptop.TryAcquire(db);
        desktop.TryAcquire(db);

        Assert.True(laptop.IsWriter);
        Assert.False(desktop.IsWriter);
        Assert.False(desktop.HolderIsLiveSameMachine);
        Assert.False(desktop.ReclaimedDeadSession);
        Assert.False(desktop.IsStaleLock);
        Assert.Equal("LAPTOP", desktop.CurrentHolder!.Machine);

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds + 1));
        desktop.TryAcquire(db); // the laptop has not renewed the heartbeat

        Assert.False(desktop.IsWriter);
        Assert.True(desktop.IsStaleLock);
        Assert.False(desktop.HolderIsLiveSameMachine);
    }

    /// <summary>Same machine, and the identity's lookup says the holder's process no longer exists: reclaimed.</summary>
    [Fact]
    public void SameMachine_HolderProcessDead_IsReclaimed()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        int? asked = null;
        using var first  = NewService(clock, Identity("PC", "alice", 1001));
        using var second = NewService(clock, Identity("PC", "bob", 2002, pid =>
        {
            asked = pid;
            throw new ArgumentException("no such process");
        }));

        first.TryAcquire(db);
        second.TryAcquire(db);

        Assert.Equal(1001, asked);
        Assert.True(second.IsWriter);
        Assert.True(second.ReclaimedDeadSession);
        Assert.Equal(2002, ReadLock(db).ProcessId);
    }

    /// <summary>Same machine and the lookup reports the holder alive with the recorded start time: read-only.</summary>
    [Fact]
    public void SameMachine_HolderProcessAlive_StaysReadOnly()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var first  = NewService(clock, Identity("PC", "alice", 1001));
        using var second = NewService(clock, Identity("PC", "bob", 2002, _ => new FakeProcess(HolderStart)));

        first.TryAcquire(db);
        second.TryAcquire(db);

        Assert.False(second.IsWriter);
        Assert.True(second.HolderIsLiveSameMachine);
        Assert.False(second.ReclaimedDeadSession);
        Assert.Equal(1001, ReadLock(db).ProcessId); // the lock is still the first one's
        Assert.True(second.IsPolling, "a read-only session still watches the lock, even with no timer object");
        Assert.Null(Field(second, "_pollTimer"));
    }

    /// <summary>The pid is in use but started more than 2 s from the recorded start: PID reuse, so reclaimed.</summary>
    [Fact]
    public void SameMachine_PidReused_IsReclaimed()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var first  = NewService(clock, Identity("PC", "alice", 1001));
        using var second = NewService(clock, Identity("PC", "bob", 2002, _ => new FakeProcess(HolderStart.AddSeconds(30))));

        first.TryAcquire(db);
        second.TryAcquire(db);

        Assert.True(second.IsWriter);
        Assert.True(second.ReclaimedDeadSession);
    }

    /// <summary>A start time within 2 s of the recorded one is the same process: still alive.</summary>
    [Fact]
    public void SameMachine_StartTimeWithinTwoSeconds_IsStillAlive()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var first  = NewService(clock, Identity("PC", "alice", 1001));
        using var second = NewService(clock, Identity("PC", "bob", 2002, _ => new FakeProcess(HolderStart.AddSeconds(1.5))));

        first.TryAcquire(db);
        second.TryAcquire(db);

        Assert.False(second.IsWriter);
        Assert.True(second.HolderIsLiveSameMachine);
    }

    /// <summary>An unreadable start time means "cannot tell": never reclaimed, and not reported as a live sibling either.</summary>
    [Fact]
    public void SameMachine_StartTimeUnreadable_IsNotReclaimed()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var first  = NewService(clock, Identity("PC", "alice", 1001));
        using var second = NewService(clock, Identity("PC", "bob", 2002, _ => new FakeProcess(null)));

        first.TryAcquire(db);
        second.TryAcquire(db);

        Assert.False(second.IsWriter);
        Assert.False(second.ReclaimedDeadSession);
        Assert.False(second.HolderIsLiveSameMachine);
    }

    /// <summary>Any other lookup failure (access denied, say) also means "cannot tell": never reclaimed.</summary>
    [Fact]
    public void SameMachine_LookupFailsOtherwise_IsNotReclaimed()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var first  = NewService(clock, Identity("PC", "alice", 1001));
        using var second = NewService(clock, Identity("PC", "bob", 2002, _ => throw new UnauthorizedAccessException("denied")));

        first.TryAcquire(db);
        second.TryAcquire(db);

        Assert.False(second.IsWriter);
        Assert.False(second.ReclaimedDeadSession);
        Assert.False(second.HolderIsLiveSameMachine);
    }

    /// <summary>The lock file carries the service's own identity and the fake clock's time, not the real ones.</summary>
    [Fact]
    public void LockFile_CarriesCustomIdentityAndFakeClockTimes()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var svc = NewService(clock, Identity("LAPTOP", "carol", 4321));

        svc.TryAcquire(db);

        var data = ReadLock(db);
        Assert.Equal("LAPTOP", data.Machine);
        Assert.Equal("carol", data.Username);
        Assert.Equal(4321, data.ProcessId);
        Assert.Equal(HolderStart, data.ProcessStartTimeUtc);
        Assert.Equal(Start.UtcDateTime, data.Acquired);
        Assert.Equal(Start.UtcDateTime, data.Heartbeat);
        Assert.Equal(svc.SessionGuid, data.SessionGuid);
    }

    // ── Seam 3: manual ticks ──────────────────────────────────────────────────

    /// <summary>With timers off, acquiring starts no timer, yet one heartbeat tick renews to the (advanced) fake time.</summary>
    [Fact]
    public async Task NoTimers_AcquireStartsNoTimer_TickHeartbeatRenews()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var svc = NewService(clock, Identity("LAPTOP", "bob", 100));

        svc.TryAcquire(db);

        Assert.True(svc.IsWriter);
        Assert.Null(Field(svc, "_heartbeatTimer"));
        Assert.Null(Field(svc, "_wakeDetectionTimer"));

        clock.Advance(TimeSpan.FromSeconds(75));
        await svc.TickHeartbeatAsync();

        var data = ReadLock(db);
        Assert.Equal(Start.UtcDateTime.AddSeconds(75), data.Heartbeat);
        Assert.Equal(Start.UtcDateTime, data.Acquired); // renewal preserves the original acquisition time
    }

    /// <summary>A timer-less reader notices a released lock on one poll tick.</summary>
    [Fact]
    public async Task NoTimers_TickPoll_NoticesReleasedLock()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var writer = NewService(clock, Identity("LAPTOP", "bob", 100));
        using var reader = NewService(clock, Identity("DESKTOP", "bob", 200));
        writer.TryAcquire(db);
        reader.TryAcquire(db);
        Assert.False(reader.IsWriter);
        Assert.True(reader.IsPolling);
        Assert.Null(Field(reader, "_pollTimer"));

        await reader.TickPollAsync();
        Assert.False(reader.WriteLockBecameAvailable); // the lock is still held

        File.Delete(LockFileOf(db)); // the holder released it
        await reader.TickPollAsync();

        Assert.True(reader.WriteLockBecameAvailable);
        Assert.False(reader.IsPolling, "noticing the release ends the polling, as it does with the timer");
    }

    /// <summary>A wake check after a gap over 90 s of fake time raises WakeDetected; shorter gaps do not.</summary>
    [Fact]
    public void NoTimers_TickWakeCheck_RaisesOnlyAfterALongGap()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        using var svc = NewService(clock, Identity("LAPTOP", "bob", 100));
        svc.TryAcquire(db);
        int raised = 0;
        svc.WakeDetected += () => raised++;

        clock.Advance(TimeSpan.FromSeconds(60));
        svc.TickWakeCheck();
        Assert.Equal(0, raised);

        clock.Advance(TimeSpan.FromSeconds(89));
        svc.TickWakeCheck();
        Assert.Equal(0, raised);

        clock.Advance(TimeSpan.FromSeconds(100));
        svc.TickWakeCheck();
        Assert.Equal(1, raised);

        clock.Advance(TimeSpan.FromSeconds(30)); // the gap is measured from the previous tick
        svc.TickWakeCheck();
        Assert.Equal(1, raised);
    }

    /// <summary>CheckoutService with timers off creates no autosave timer, and one tick saves a dirty working copy.</summary>
    [Fact]
    public async Task CheckoutNoTimers_TickAutoSave_SavesDirtyWorkingCopy()
    {
        var clock = new FakeTimeProvider(Start);
        var db = CreateDb();
        var lockSvc = NewService(clock, Identity("LAPTOP", "bob", 100));
        using var svc = new CheckoutService(lockSvc, new NullLogger(), a => a(), _workingDir, startTimers: false);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        svc.StartAutoSave();

        Assert.True(svc.IsAutoSaveRunning);
        Assert.Null(Field(svc, "_autoSaveTimer"));

        InsertValue(svc.WorkingPath, "edited");
        svc.MarkDirty();
        await svc.TickAutoSaveAsync();

        Assert.Equal("edited", ReadValue(db));

        svc.StopAutoSave();
        Assert.False(svc.IsAutoSaveRunning);
        lockSvc.Dispose();
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    /// <summary>The default identity is exactly today's Environment / Process values.</summary>
    [Fact]
    public void DefaultIdentity_EqualsTheRealEnvironmentValues()
    {
        var id = LockIdentity.Current;

        Assert.Equal(Environment.MachineName, id.MachineName);
        Assert.Equal(Environment.UserName, id.UserName);
        Assert.Equal(Environment.ProcessId, id.ProcessId);
        var realStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        Assert.NotNull(id.ProcessStartTimeUtc);
        Assert.True(Math.Abs((id.ProcessStartTimeUtc!.Value - realStart).TotalSeconds) < 1);

        using var self = id.LookupProcess(Environment.ProcessId);
        Assert.True(Math.Abs((self.StartTimeUtc - realStart).TotalSeconds) < 1);
        Assert.Throws<ArgumentException>(() => id.LookupProcess(int.MaxValue - 7));
    }

    /// <summary>
    /// A default-constructed service writes the real Environment values into the lock file and starts the real
    /// heartbeat and wake timers (and no poll timer for a writer).
    /// </summary>
    [Fact]
    public void DefaultService_WritesRealIdentityAndStartsRealTimers()
    {
        var db = CreateDb();
        using var svc = new WriteLockService();

        svc.TryAcquire(db);
        try
        {
            Assert.True(svc.IsWriter);
            Assert.NotNull(Field(svc, "_heartbeatTimer"));
            Assert.NotNull(Field(svc, "_wakeDetectionTimer"));
            Assert.Null(Field(svc, "_pollTimer"));
            var data = ReadLock(db);
            Assert.Equal(Environment.MachineName, data.Machine);
            Assert.Equal(Environment.UserName, data.Username);
            Assert.Equal(Environment.ProcessId, data.ProcessId);
        }
        finally
        {
            svc.Release();
        }
        Assert.Null(Field(svc, "_heartbeatTimer"));
    }

    /// <summary>A default-constructed CheckoutService still starts the real autosave timer.</summary>
    [Fact]
    public async Task DefaultCheckout_StartAutoSaveCreatesRealTimer()
    {
        var db = CreateDb();
        var lockSvc = new WriteLockService();
        using var svc = new CheckoutService(lockSvc, new NullLogger(), a => a(), _workingDir);
        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.CheckoutAsync(db));

        svc.StartAutoSave();
        try
        {
            Assert.NotNull(Field(svc, "_autoSaveTimer"));
            Assert.True(svc.IsAutoSaveRunning);
        }
        finally
        {
            svc.StopAutoSave();
        }
        Assert.Null(Field(svc, "_autoSaveTimer"));
        Assert.False(svc.IsAutoSaveRunning);
        lockSvc.Dispose();
    }

    // ── Small SQLite helpers ──────────────────────────────────────────────────

    /// <summary>Inserts one value into a <c>_t</c> table of the database at <paramref name="path"/>.</summary>
    private static void InsertValue(string path, string value)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _t(val TEXT); INSERT INTO _t VALUES(@v)";
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Reads the newest value from the <c>_t</c> table, or null if there is none.</summary>
    private static string? ReadValue(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT val FROM _t ORDER BY rowid DESC LIMIT 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>No-op logger so tests have no file-system side effects from logging.</summary>
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
}
