using System.Collections.Concurrent;
using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for "a lock-file check counts for 2 seconds" (<see cref="WriteLockService.LockCheckFreshness"/>; 1.2.4 items 4.12 and 4.14,
/// findings F19 and F22). The lock service reads the lock file, finds it ours (or, for a taker, unchanged) and then acts on that
/// (writes the temporary file, moves it onto the lock, deletes the lock). A computer that freezes or sleeps between the read and the
/// act may find, when it wakes, that another computer took the lock over: the act must then NOT go ahead on the old reading.
///
/// <para>Each test freezes the simulated computer at a point between the check and the act (the crash-point hooks
/// <c>Heartbeat.AfterTmpWritten</c> and <c>Release.AfterOwnershipCheck</c>, or, where a method has none, the lock service's own
/// call to the process lookup or to the steady clock) by advancing a <see cref="FakeTimeProvider"/>, and, in most tests, by
/// placing another computer's lock file. Every such test fails if the check is not repeated: the foreign lock is then moved over or
/// deleted. The crash-point hook and the network-fault recorder are static: the tests reset them in a <c>finally</c>, and the suite
/// does not run tests in parallel.</para>
/// </summary>
public sealed class WriteLockServiceFreshnessTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>The fixed instant the fake clock starts at.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>How long a frozen computer is held: far longer than <see cref="WriteLockService.LockCheckFreshness"/>.</summary>
    private static readonly TimeSpan Freeze = TimeSpan.FromSeconds(10);

    /// <summary>The temporary folder holding the lock file of this test.</summary>
    private readonly string _dir;

    /// <summary>Path of the (fake) database; only the lock file beside it is used.</summary>
    private readonly string _db;

    /// <summary>The lock file's path.</summary>
    private string LockPath => Path.ChangeExtension(_db, ".lock");

    /// <summary>Creates an isolated folder for the test.</summary>
    public WriteLockServiceFreshnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"wlf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "dept.db");
    }

    /// <summary>Resets the static hooks and deletes the folder (best effort).</summary>
    public void Dispose()
    {
        CrashPoints.Hook     = null;
        NetworkFaults.Decide = null;
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a timer-less lock service for a simulated computer named <paramref name="machine"/>.</summary>
    /// <param name="clock">The clock it reads and stamps with.</param>
    /// <param name="machine">Its machine name.</param>
    /// <param name="lookup">How it finds other processes; null means "never called".</param>
    private static WriteLockService NewService(TimeProvider clock, string machine, Func<int, ILockProcess>? lookup = null)
        => new(clock,
               new LockIdentity(machine, "user-" + machine, 100, Start.UtcDateTime,
                   lookup ?? (p => throw new InvalidOperationException($"unexpected process lookup for {p}"))),
               startTimers: false);

    /// <summary>Writes another computer's lock file (a live holder named alice), as if it took the lock over.</summary>
    /// <param name="now">The current UTC time, which the holder's heartbeat shows.</param>
    private void PlaceAlicesLock(DateTime now)
        => File.WriteAllText(LockPath, JsonSerializer.Serialize(new LockFileData
        {
            Username = "alice", Machine = "ALICE-PC", SessionGuid = "alice-session", Acquired = now, Heartbeat = now
        }));

    /// <summary>Reads and parses the lock file.</summary>
    private LockFileData ReadLock() => JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockPath))!;

    /// <summary>Records the label of every share operation on the lock file (File.ReadAllText, File.Move, ...), in order.</summary>
    /// <returns>The list that fills as operations run.</returns>
    private ConcurrentQueue<string> RecordLockFileOperations()
    {
        var seen = new ConcurrentQueue<string>();
        NetworkFaults.Decide = request =>
        {
            if (request.Path is not null && request.Path.StartsWith(Path.ChangeExtension(_db, ".lock"), StringComparison.OrdinalIgnoreCase))
                seen.Enqueue(request.Label);
            return NetworkFaultDecision.None;
        };
        return seen;
    }

    /// <summary>Counts how many times <paramref name="label"/> is in the recorded operations.</summary>
    /// <param name="seen">The recorded operations.</param>
    /// <param name="label">An operation label such as "File.ReadAllText".</param>
    private static int CountOf(ConcurrentQueue<string> seen, string label) => seen.Count(l => l == label);

    /// <summary>Has bob acquire the lock for the test database.</summary>
    /// <param name="clock">The clock bob uses.</param>
    private WriteLockService BobWithTheLock(TimeProvider clock)
    {
        var bob = NewService(clock, "BOB-PC");
        bob.TryAcquire(_db);
        Assert.True(bob.IsWriter);
        return bob;
    }

    /// <summary>Waits for the background lock-file delete queued by a release to finish.</summary>
    /// <param name="svc">The service that released.</param>
    private static void SettleRelease(WriteLockService svc) => svc.PendingRelease?.Wait(TimeSpan.FromSeconds(10));

    /// <summary>
    /// A clock that runs an action in the middle of a lock-service method that has no crash point: on the n-th reading of the steady clock
    /// after <see cref="Arm"/>, the action runs first (it advances the inner clock and may change files), so that reading, and every later one,
    /// is after the action. The service stamps a check with one reading and tests its age with the next, so arming for 2 readings freezes the
    /// computer between the stamp and the test.
    /// </summary>
    private sealed class JumpingClock : TimeProvider
    {
        private int _readingsLeft;
        private Action? _action;

        /// <summary>The fake clock the readings come from; the action advances it.</summary>
        public FakeTimeProvider Inner { get; } = new(Start);

        /// <summary>Arms the action.</summary>
        /// <param name="readings">Runs the action on this many readings of the steady clock from now (1 = the very next reading).</param>
        /// <param name="action">What happens while the computer is frozen.</param>
        public void Arm(int readings, Action action)
        {
            _readingsLeft = readings;
            _action = action;
        }

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();

        /// <inheritdoc/>
        public override long GetTimestamp()
        {
            if (_action is not null && --_readingsLeft == 0)
            {
                var action = _action;
                _action = null;
                action();
            }
            return Inner.GetTimestamp();
        }

        /// <inheritdoc/>
        public override long TimestampFrequency => Inner.TimestampFrequency;

        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => Inner.CreateTimer(callback, state, dueTime, period);
    }

    // ── The constant ──────────────────────────────────────────────────────────

    /// <summary>The freshness is 2 seconds and is not a lock timer: no scaled value of it exists.</summary>
    [Fact]
    public void LockCheckFreshness_IsTwoSeconds_AndIsNotScaledByTestTimings()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), WriteLockService.LockCheckFreshness);
        Assert.Equal(TimeSpan.FromSeconds(2), WriteLockService.LockCheckFreshness / LockTimings.Normal.Scale);
        Assert.DoesNotContain(typeof(LockTimings).GetProperties(), p => p.Name.Contains("Freshness"));
    }

    // ── Heartbeat (F19) ───────────────────────────────────────────────────────

    /// <summary>
    /// Both clocks say 10 s passed between the heartbeat's temporary-file write and its move, and meanwhile another computer took the lock: the
    /// heartbeat reads the lock again, finds alice's name, moves NOTHING and asks for the ownership check at once. Alice's lock is intact.
    /// </summary>
    [Fact]
    public async Task Heartbeat_FrozenAfterTheTmpWrite_WhileAnotherComputerTookTheLock_MovesNothing_AndAsksForTheOwnershipCheck()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var asked = 0;
        bob.HeartbeatFailed += () => asked++;
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name =>
        {
            if (name != "Heartbeat.AfterTmpWritten") return;
            clock.Advance(Freeze);
            PlaceAlicesLock(clock.GetUtcNow().UtcDateTime);
        };

        await bob.TickHeartbeatAsync();

        Assert.Equal("alice-session", ReadLock().SessionGuid);
        Assert.Equal(0, CountOf(seen, "File.Move"));
        Assert.Equal(1, asked);
        Assert.Equal(2, CountOf(seen, "File.ReadAllText"));   // the check was made again
    }

    /// <summary>
    /// The same freeze, but the lock is still bob's: the heartbeat reads it again, finds it ours, and the renewal goes ahead (one move,
    /// two reads) with no failure raised.
    /// </summary>
    [Fact]
    public async Task Heartbeat_FrozenAfterTheTmpWrite_LockStillOurs_ReReadsAndRenews()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var asked = 0;
        bob.HeartbeatFailed += () => asked++;
        clock.Advance(TimeSpan.FromSeconds(30));
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name => { if (name == "Heartbeat.AfterTmpWritten") clock.Advance(Freeze); };

        await bob.TickHeartbeatAsync();

        var data = ReadLock();
        Assert.Equal(bob.SessionGuid, data.SessionGuid);
        Assert.Equal(Start.UtcDateTime.AddSeconds(30), data.Heartbeat);   // renewed (the tmp was built before the freeze)
        Assert.Equal(1, CountOf(seen, "File.Move"));
        Assert.Equal(2, CountOf(seen, "File.ReadAllText"));
        Assert.Equal(0, asked);
    }

    /// <summary>
    /// Only the WALL clock jumps (the steady clock does not move): either clock noticing is enough, so the check is repeated and the foreign
    /// lock is left alone.
    /// </summary>
    [Fact]
    public async Task Heartbeat_OnlyTheWallClockJumpsForward_ChecksAgain_AndLeavesTheForeignLockAlone()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name =>
        {
            if (name != "Heartbeat.AfterTmpWritten") return;
            clock.JumpWallClock(Freeze);
            PlaceAlicesLock(clock.GetUtcNow().UtcDateTime);
        };

        await bob.TickHeartbeatAsync();

        Assert.Equal("alice-session", ReadLock().SessionGuid);
        Assert.Equal(0, CountOf(seen, "File.Move"));
    }

    /// <summary>Only the STEADY clock advances (the wall clock does not): the check is repeated and the foreign lock is left alone.</summary>
    [Fact]
    public async Task Heartbeat_OnlyTheSteadyClockAdvances_ChecksAgain_AndLeavesTheForeignLockAlone()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name =>
        {
            if (name != "Heartbeat.AfterTmpWritten") return;
            clock.AdvanceSteadyClockOnly(Freeze);
            PlaceAlicesLock(clock.GetUtcNow().UtcDateTime);
        };

        await bob.TickHeartbeatAsync();

        Assert.Equal("alice-session", ReadLock().SessionGuid);
        Assert.Equal(0, CountOf(seen, "File.Move"));
    }

    /// <summary>
    /// A wall clock that jumps BACKWARDS counts as no time passed (the steady clock, which did not move, speaks): the check is fresh, it is not
    /// repeated (one read, one move), and a wall-clock set-back cannot make the heartbeat re-read forever.
    /// </summary>
    [Fact]
    public async Task Heartbeat_WallClockJumpsBack_CountsAsNoTimePassed_AndDoesNotReRead()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name => { if (name == "Heartbeat.AfterTmpWritten") clock.JumpWallClock(-Freeze); };

        await bob.TickHeartbeatAsync();

        Assert.Equal(1, CountOf(seen, "File.ReadAllText"));
        Assert.Equal(1, CountOf(seen, "File.Move"));
    }

    /// <summary>A freeze shorter than 2 seconds is inside the freshness: nothing is repeated (the residual milliseconds a file share cannot close).</summary>
    [Fact]
    public async Task Heartbeat_ShortFreeze_StaysFresh_AndDoesNotReRead()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name => { if (name == "Heartbeat.AfterTmpWritten") clock.Advance(TimeSpan.FromSeconds(1.5)); };

        await bob.TickHeartbeatAsync();

        Assert.Equal(1, CountOf(seen, "File.ReadAllText"));
        Assert.Equal(1, CountOf(seen, "File.Move"));
    }

    // ── Release (F22) ─────────────────────────────────────────────────────────

    /// <summary>
    /// The release has checked that the lock is bob's and is frozen before its delete (10 s pass); another computer takes the lock over. The
    /// release reads again and does NOT delete alice's lock.
    /// </summary>
    [Fact]
    public void Release_FrozenAfterItsOwnershipCheck_WhileAnotherComputerTookTheLock_DoesNotDeleteTheForeignLock()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name =>
        {
            if (name != "Release.AfterOwnershipCheck") return;
            clock.Advance(Freeze);
            PlaceAlicesLock(clock.GetUtcNow().UtcDateTime);
        };

        bob.Release();
        SettleRelease(bob);

        Assert.True(File.Exists(LockPath));
        Assert.Equal("alice-session", ReadLock().SessionGuid);
        Assert.Equal(0, CountOf(seen, "File.Delete"));
    }

    /// <summary>The same freeze but the lock is still bob's: the release reads again, finds it ours, and deletes it.</summary>
    [Fact]
    public void Release_FrozenAfterItsOwnershipCheck_LockStillOurs_ReReadsAndDeletes()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();
        CrashPoints.Hook = name => { if (name == "Release.AfterOwnershipCheck") clock.Advance(Freeze); };

        bob.Release();
        SettleRelease(bob);

        Assert.False(File.Exists(LockPath));
        Assert.Equal(2, CountOf(seen, "File.ReadAllText"));
        Assert.Equal(1, CountOf(seen, "File.Delete"));
    }

    /// <summary>A release that is not frozen reads once and deletes (the common case costs nothing extra).</summary>
    [Fact]
    public void Release_NotFrozen_ReadsOnce_AndDeletes()
    {
        var clock = new FakeTimeProvider(Start);
        var bob = BobWithTheLock(clock);
        var seen = RecordLockFileOperations();

        bob.Release();
        SettleRelease(bob);

        Assert.False(File.Exists(LockPath));
        Assert.Equal(1, CountOf(seen, "File.ReadAllText"));
        Assert.Equal(1, CountOf(seen, "File.Delete"));
    }

    // ── Taker side: ForceAcquire and TryReclaimDeadHolder ─────────────────────

    /// <summary>
    /// ForceAcquire has re-checked that the stale lock is unchanged and is about to delete it; the computer freezes there (10 s pass) and another
    /// computer takes the lock over. The check is made again, finds the change, and nothing is deleted: <c>HolderActiveAgain</c>.
    /// </summary>
    [Fact]
    public void ForceAcquire_FrozenAfterItsRecheck_WhileAnotherComputerTookTheLock_DeletesNothing()
    {
        var clock = new JumpingClock();
        using var carol = NewService(clock, "CAROL-PC");
        File.WriteAllText(LockPath, JsonSerializer.Serialize(new LockFileData
        {
            Username = "dave", Machine = "DAVE-PC", SessionGuid = "dave-session",
            Acquired = Start.UtcDateTime.AddMinutes(-30), Heartbeat = Start.UtcDateTime.AddMinutes(-30)
        }));
        carol.TryAcquire(_db);
        Assert.True(carol.IsStaleLock);
        var judged = carol.CurrentHolder!;

        // The stamp is the 1st reading of the steady clock in ForceAcquire, the test of its age the 2nd: freeze between them.
        clock.Arm(2, () =>
        {
            clock.Inner.Advance(Freeze);
            PlaceAlicesLock(clock.Inner.GetUtcNow().UtcDateTime);
        });

        var result = carol.ForceAcquire(judged);

        Assert.Equal(ForceAcquireResult.HolderActiveAgain, result);
        Assert.False(carol.IsWriter);
        Assert.Equal("alice-session", ReadLock().SessionGuid);
    }

    /// <summary>The same freeze, but the stale lock is still exactly the one judged: the check is made again and the takeover goes ahead.</summary>
    [Fact]
    public void ForceAcquire_FrozenAfterItsRecheck_LockUnchanged_StillTakesOver()
    {
        var clock = new JumpingClock();
        using var carol = NewService(clock, "CAROL-PC");
        File.WriteAllText(LockPath, JsonSerializer.Serialize(new LockFileData
        {
            Username = "dave", Machine = "DAVE-PC", SessionGuid = "dave-session",
            Acquired = Start.UtcDateTime.AddMinutes(-30), Heartbeat = Start.UtcDateTime.AddMinutes(-30)
        }));
        carol.TryAcquire(_db);
        Assert.True(carol.IsStaleLock);
        var judged = carol.CurrentHolder!;
        clock.Arm(2, () => clock.Inner.Advance(Freeze));

        var result = carol.ForceAcquire(judged);

        Assert.Equal(ForceAcquireResult.Acquired, result);
        Assert.True(carol.IsWriter);
        Assert.Equal(carol.SessionGuid, ReadLock().SessionGuid);
    }

    /// <summary>
    /// The open path read a lock whose holder is provably dead (same machine, no such process). The computer freezes before the delete (10 s
    /// pass inside the process lookup) and another computer takes the lock over. The lock is read again, is no longer the dead session, and is NOT
    /// deleted: this instance stays read-only.
    /// </summary>
    [Fact]
    public void TryReclaimDeadHolder_FrozenBeforeTheDelete_WhileAnotherComputerTookTheLock_DeletesNothing()
    {
        var clock = new FakeTimeProvider(Start);
        using var carol = NewService(clock, "CAROL-PC", pid =>
        {
            clock.Advance(Freeze);
            PlaceAlicesLock(clock.GetUtcNow().UtcDateTime);
            throw new ArgumentException("no such process");   // the dead holder: what Process.GetProcessById throws
        });
        File.WriteAllText(LockPath, JsonSerializer.Serialize(new LockFileData
        {
            Username = "user-CAROL-PC", Machine = "CAROL-PC", SessionGuid = "dead-session", ProcessId = 4242,
            Acquired = Start.UtcDateTime, Heartbeat = Start.UtcDateTime
        }));

        carol.TryAcquire(_db);

        Assert.False(carol.IsWriter);
        Assert.False(carol.ReclaimedDeadSession);
        Assert.Equal("alice-session", ReadLock().SessionGuid);
    }

    /// <summary>The same freeze, but the dead session's lock is unchanged: it is read again, still the dead session, and reclaimed as before.</summary>
    [Fact]
    public void TryReclaimDeadHolder_FrozenBeforeTheDelete_LockUnchanged_StillReclaims()
    {
        var clock = new FakeTimeProvider(Start);
        using var carol = NewService(clock, "CAROL-PC", pid =>
        {
            clock.Advance(Freeze);
            throw new ArgumentException("no such process");
        });
        File.WriteAllText(LockPath, JsonSerializer.Serialize(new LockFileData
        {
            Username = "user-CAROL-PC", Machine = "CAROL-PC", SessionGuid = "dead-session", ProcessId = 4242,
            Acquired = Start.UtcDateTime, Heartbeat = Start.UtcDateTime
        }));

        carol.TryAcquire(_db);

        Assert.True(carol.IsWriter);
        Assert.True(carol.ReclaimedDeadSession);
        Assert.Equal(carol.SessionGuid, ReadLock().SessionGuid);
    }

    // ── Our own leftover lock (finding F20) ───────────────────────────────────

    /// <summary>A process the identity's lookup reports as running, started at <see cref="Start"/>.</summary>
    private sealed class RunningProcess : ILockProcess
    {
        /// <inheritdoc/>
        public DateTime StartTimeUtc => Start.UtcDateTime;

        /// <inheritdoc/>
        public void Dispose() { }
    }

    /// <summary>
    /// F20: a lock parked by <c>Suspend</c> (a failed final save) names this very process. The next acquire takes it back and is
    /// the writer, with no "previous session closed unexpectedly" flag and no read-only "another window" flag. The process lookup is
    /// never consulted (the default lookup in these tests throws if it is).
    /// </summary>
    [Fact]
    public void OwnLeftoverLock_ParkedBySuspend_IsTakenBack_WithoutTheDeadSessionNotice()
    {
        var clock = new FakeTimeProvider(Start);
        using var bob = NewService(clock, "BOB-PC");
        bob.TryAcquire(_db);
        Assert.True(bob.IsWriter);
        bob.Suspend();
        Assert.Equal(bob.SessionGuid, ReadLock().SessionGuid);   // the lock is left behind, naming bob's own session

        bob.TryAcquire(_db);

        Assert.True(bob.IsWriter);
        Assert.False(bob.ReclaimedDeadSession);
        Assert.False(bob.HolderIsLiveSameMachine);
        Assert.Equal(bob.SessionGuid, ReadLock().SessionGuid);
    }

    /// <summary>
    /// A lock naming ANOTHER session, written by a process with this process's id (a genuine second instance cannot have our GUID),
    /// keeps today's behaviour: the holder is alive on this machine, so this instance is read-only and says so.
    /// </summary>
    [Fact]
    public void LockOfAnotherSession_WithOurProcessId_StaysReadOnly()
    {
        var clock = new FakeTimeProvider(Start);
        using var bob = NewService(clock, "BOB-PC", _ => new RunningProcess());
        File.WriteAllText(LockPath, JsonSerializer.Serialize(new LockFileData
        {
            Username = "user-BOB-PC", Machine = "BOB-PC", SessionGuid = "someone-elses-session", ProcessId = 100,
            ProcessStartTimeUtc = Start.UtcDateTime, Acquired = Start.UtcDateTime, Heartbeat = Start.UtcDateTime
        }));

        bob.TryAcquire(_db);

        Assert.False(bob.IsWriter);
        Assert.True(bob.HolderIsLiveSameMachine);
        Assert.False(bob.ReclaimedDeadSession);
        Assert.Equal("someone-elses-session", ReadLock().SessionGuid);
    }

    /// <summary>
    /// Our own leftover was read, then the computer froze (10 s) and another computer took over the stale lock before the delete.
    /// The check is made again, the lock is no longer ours, and NOTHING is deleted: bob stays read-only behind the new holder.
    /// </summary>
    [Fact]
    public void OwnLeftoverLock_ReplacedByAnotherSessionBeforeTheDelete_DeletesNothing()
    {
        var clock = new JumpingClock();
        using var bob = NewService(clock, "BOB-PC");
        bob.TryAcquire(_db);
        bob.Suspend();

        // The read of the leftover stamps the check with the 1st steady-clock reading, its age test is the 2nd: freeze between them.
        clock.Arm(2, () =>
        {
            clock.Inner.Advance(Freeze);
            PlaceAlicesLock(clock.Inner.GetUtcNow().UtcDateTime);
        });

        bob.TryAcquire(_db);

        Assert.False(bob.IsWriter);
        Assert.False(bob.ReclaimedDeadSession);
        Assert.Equal("alice-session", ReadLock().SessionGuid);
    }

    /// <summary>The same freeze, but the leftover is still exactly ours: it is checked again and taken back as usual.</summary>
    [Fact]
    public void OwnLeftoverLock_FrozenBeforeTheDelete_LockUnchanged_StillTakenBack()
    {
        var clock = new JumpingClock();
        using var bob = NewService(clock, "BOB-PC");
        bob.TryAcquire(_db);
        bob.Suspend();
        clock.Arm(2, () => clock.Inner.Advance(Freeze));

        bob.TryAcquire(_db);

        Assert.True(bob.IsWriter);
        Assert.Equal(bob.SessionGuid, ReadLock().SessionGuid);
    }

    /// <summary>
    /// A release of ours is still running in the background when the leftover is judged: nothing is taken back (its delete could land on
    /// the lock we create), and the ordinary path leaves the session read-only. Calls the private step directly, because
    /// <c>TryAcquire</c> itself already waits for a pending release before it gets this far.
    /// </summary>
    [Fact]
    public void OwnLeftoverLock_WhileOurOwnReleaseIsStillPending_IsNotTakenBack()
    {
        var clock = new FakeTimeProvider(Start);
        using var bob = NewService(clock, "BOB-PC");
        bob.TryAcquire(_db);
        bob.Suspend();
        bob.OwnLeftoverReleaseWait = TimeSpan.FromMilliseconds(100);

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WriteLockService).GetField("_lockFilePath", flags)!.SetValue(bob, LockPath);
        typeof(WriteLockService).GetField("_pendingReleaseTask", flags)!
            .SetValue(bob, new TaskCompletionSource().Task);   // never completes
        var taken = (bool)typeof(WriteLockService).GetMethod("TryCreateLockFile", flags)!.Invoke(bob, null)!;

        Assert.False(taken);
        Assert.False(bob.IsWriter);
        Assert.Equal(bob.SessionGuid, ReadLock().SessionGuid);   // the leftover was not touched
    }
}
