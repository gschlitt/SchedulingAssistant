using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the open-path half of spec item 9b: after the stale-lock prompt the takeover also needs the
/// holder's heartbeat to have been seen UNCHANGED for <see cref="WriteLockService.StaleConfirmationSeconds"/>
/// of the reader's own time, whatever the two computers' clocks say.
///
/// <para>The clock comparison is only a HINT that makes an obviously dead lock show the prompt at once;
/// <see cref="CheckoutService.GetTakeOverWait"/> reports how much of the confirmation window is left
/// when "Take over" is clicked, <see cref="CheckoutService.WaitForTakeOverConfirmationAsync"/> waits it
/// out without blocking, and <see cref="CheckoutService.ForceCheckoutAsync"/>'s 9a re-check then catches a
/// heartbeat that moved in the meantime. Time is a <see cref="FakeTimeProvider"/>, so none of these
/// tests really waits.</para>
///
/// <para>Each test uses isolated temporary folders for D and the working copies, a private
/// <see cref="WriteLockService"/> on the fake clock, and a synchronous dispatcher, like
/// <see cref="CheckoutServiceTests"/>. Hand-written lock files name a remote machine with no process id,
/// which forces the heartbeat path (the same-machine process-liveness logic is covered elsewhere).</para>
/// </summary>
public sealed class CheckoutServiceTakeOverWaitTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _workingDir;

    /// <summary>How long a test lets a task that should already be finished take before failing.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>The confirmation window as a TimeSpan.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(WriteLockService.StaleConfirmationSeconds);

    /// <summary>Creates isolated temporary directories for source databases and working copies.</summary>
    public CheckoutServiceTakeOverWaitTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _tempDir    = Path.Combine(Path.GetTempPath(), $"cstw_src_{id}");
        _workingDir = Path.Combine(Path.GetTempPath(), $"cstw_wrk_{id}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    /// <summary>Deletes both temporary directories (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir,    recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a fake database file D and returns its path.</summary>
    private string CreateDb()
    {
        var db = Path.Combine(_tempDir, "test.db");
        File.WriteAllText(db, "dummy-database-content");
        return db;
    }

    /// <summary>Path of the lock file that sits beside <paramref name="db"/>.</summary>
    private static string LockFileOf(string db) => Path.ChangeExtension(db, ".lock");

    /// <summary>
    /// Writes (or rewrites) the lock file as another computer's writer would: any session, any heartbeat
    /// value (so a wrong clock can be modelled), a remote machine name and no process id.
    /// </summary>
    private static void WriteLock(string db, string session, DateTime heartbeatUtc)
    {
        var data = new LockFileData
        {
            Username    = "external_user",
            Machine     = "REMOTE-PC",
            SessionGuid = session,
            Acquired    = heartbeatUtc,
            Heartbeat   = heartbeatUtc,
        };
        File.WriteAllText(LockFileOf(db), JsonSerializer.Serialize(data));
    }

    /// <summary>Creates a checkout service whose lock service runs on <paramref name="clock"/>.</summary>
    private (CheckoutService svc, WriteLockService lockSvc) CreateService(FakeTimeProvider clock)
    {
        var lockSvc = new WriteLockService(clock);
        var svc = new CheckoutService(
            lockService: lockSvc,
            logger:      new NullLogger(),
            dispatch:    action => action(),
            workingDir:  _workingDir);
        return (svc, lockSvc);
    }

    /// <summary>
    /// Opens D against a lock the clock hint calls long dead (heartbeat 20 minutes old by the fake
    /// clock) and returns the service positioned at the stale prompt (<see cref="CheckoutOutcome.StaleHolder"/>).
    /// </summary>
    private async Task<(CheckoutService svc, WriteLockService lockSvc, string db, DateTime heartbeat)> OpenAtStalePromptAsync(
        FakeTimeProvider clock)
    {
        var (svc, lockSvc) = CreateService(clock);
        var db = CreateDb();
        var heartbeat = clock.GetUtcNow().UtcDateTime.AddMinutes(-20);
        WriteLock(db, "dead-or-asleep", heartbeat);

        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db)); // precondition
        return (svc, lockSvc, db, heartbeat);
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

    // ── How long is left ──────────────────────────────────────────────────────

    /// <summary>The constant is the heartbeat interval plus a 10 second margin: one full renewal cycle.</summary>
    [Fact]
    public void StaleConfirmationSeconds_IsOneHeartbeatIntervalPlusTenSeconds()
    {
        Assert.Equal(70, WriteLockService.StaleConfirmationSeconds);
        Assert.Equal(WriteLockService.HeartbeatIntervalSeconds + 10, WriteLockService.StaleConfirmationSeconds);
    }

    /// <summary>With no stale judgement behind it there is nothing to wait for.</summary>
    [Fact]
    public void GetTakeOverWait_WithoutAStaleHolderCheckout_IsZero()
    {
        var (svc, _) = CreateService(new FakeTimeProvider());

        Assert.Equal(TimeSpan.Zero, svc.GetTakeOverWait());
    }

    /// <summary>Clicking "Take over" the moment the prompt appears leaves the whole window to wait.</summary>
    [Fact]
    public async Task GetTakeOverWait_ClickedImmediately_IsTheFullWindow()
    {
        var clock = new FakeTimeProvider();
        var (svc, _, _, _) = await OpenAtStalePromptAsync(clock);

        Assert.Equal(Window, svc.GetTakeOverWait());
    }

    /// <summary>Clicked 10 seconds after the lock was first seen: 60 seconds are left.</summary>
    [Fact]
    public async Task GetTakeOverWait_ClickedAt10Seconds_Is60Seconds()
    {
        var clock = new FakeTimeProvider();
        var (svc, _, _, _) = await OpenAtStalePromptAsync(clock);

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(60), svc.GetTakeOverWait());
    }

    /// <summary>Boundary: one second short of the window leaves one second; the full window leaves none.</summary>
    [Fact]
    public async Task GetTakeOverWait_Boundary_69Seconds_Leaves1_70Seconds_LeavesNone()
    {
        var clock = new FakeTimeProvider();
        var (svc, _, _, _) = await OpenAtStalePromptAsync(clock);

        clock.Advance(TimeSpan.FromSeconds(69));
        Assert.Equal(TimeSpan.FromSeconds(1), svc.GetTakeOverWait());

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, svc.GetTakeOverWait());
    }

    /// <summary>
    /// Clicked at 75 seconds (reading the prompt took a while): no wait at all, the takeover goes
    /// straight to the 9a re-check and succeeds.
    /// </summary>
    [Fact]
    public async Task ClickedAt75Seconds_NoWait_TakesOverImmediately()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, db, _) = await OpenAtStalePromptAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(75));

        Assert.Equal(TimeSpan.Zero, svc.GetTakeOverWait());
        Assert.True(await svc.WaitForTakeOverConfirmationAsync().WaitAsync(Patience), "nothing to wait for");
        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
        var written = JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(LockFileOf(db)))!;
        Assert.Equal(lockSvc.SessionGuid, written.SessionGuid);
        lockSvc.Dispose();
    }

    // ── Waiting it out ────────────────────────────────────────────────────────

    /// <summary>
    /// Clicked at +10 seconds: the wait is the remaining 60 seconds and does not finish a second early.
    /// After the (fake) wait with no change in the heartbeat, the takeover goes ahead.
    /// </summary>
    [Fact]
    public async Task ClickedAt10Seconds_WaitsTheRemaining60Seconds_ThenTakesOver()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, _, _) = await OpenAtStalePromptAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(60), svc.GetTakeOverWait());

        var wait = svc.WaitForTakeOverConfirmationAsync();
        Assert.False(wait.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(wait.IsCompleted, "one second still to go");

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await wait.WaitAsync(Patience));
        Assert.Equal(TimeSpan.Zero, svc.GetTakeOverWait());

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.WriteAccess, outcome);
        Assert.True(lockSvc.IsWriter);
        lockSvc.Dispose();
    }

    /// <summary>
    /// A heartbeat that changes DURING the wait means the holder is alive after all. The wait itself
    /// completes (it only measures time), but 9a's click-time re-check then refuses: HolderActiveAgain,
    /// the lock file byte-for-byte intact, no working copy made.
    /// </summary>
    [Fact]
    public async Task HeartbeatRenewedDuringTheWait_TakeoverIsRefused_AndTheLockIsIntact()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, db, heartbeat) = await OpenAtStalePromptAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(10));
        var wait = svc.WaitForTakeOverConfirmationAsync();

        clock.Advance(TimeSpan.FromSeconds(30));
        WriteLock(db, "dead-or-asleep", heartbeat.AddSeconds(60));   // the holder renewed
        var lockBytes = File.ReadAllBytes(LockFileOf(db));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await wait.WaitAsync(Patience));

        var outcome = await svc.ForceCheckoutAsync();

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, outcome);
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
        Assert.Empty(Directory.GetFiles(_workingDir));
        lockSvc.Dispose();
    }

    /// <summary>
    /// THE 9b BUG: a writer whose clock is far BEHIND this computer's (every heartbeat it writes looks
    /// 20 minutes old) but which keeps renewing every minute. The clock hint offers the prompt, yet the
    /// takeover is refused because the heartbeat moved inside the confirmation window.
    /// </summary>
    [Fact]
    public async Task SkewedBehindButLiveWriter_IsOfferedByTheHint_ButNeverTakenOver()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, db, heartbeat) = await OpenAtStalePromptAsync(clock);

        // The user clicks Take over at once; during the full 70 s the live writer beats at 30 s and 60 s
        // (its stamps still look 20 minutes old: the writer's clock is behind).
        var wait = svc.WaitForTakeOverConfirmationAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        WriteLock(db, "dead-or-asleep", heartbeat.AddSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(30));
        WriteLock(db, "dead-or-asleep", heartbeat.AddSeconds(60));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(await wait.WaitAsync(Patience));

        Assert.Equal(CheckoutOutcome.HolderActiveAgain, await svc.ForceCheckoutAsync());
        Assert.False(lockSvc.IsWriter);
        lockSvc.Dispose();
    }

    /// <summary>Cancelling the wait gives up: it returns false and the lock file is not touched.</summary>
    [Fact]
    public async Task CancellingTheWait_ReturnsFalse_AndLeavesTheLockAlone()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, db, _) = await OpenAtStalePromptAsync(clock);
        var lockBytes = File.ReadAllBytes(LockFileOf(db));
        using var cancellation = new CancellationTokenSource();

        var wait = svc.WaitForTakeOverConfirmationAsync(cancellation.Token);
        Assert.False(wait.IsCompleted);
        cancellation.Cancel();

        Assert.False(await wait.WaitAsync(Patience));
        Assert.False(lockSvc.IsWriter);
        Assert.Equal(lockBytes, File.ReadAllBytes(LockFileOf(db)));
        lockSvc.Dispose();
    }

    // ── The hint alone is not enough, and neither is a lock that looks alive ──

    /// <summary>
    /// A dead writer whose clock is AHEAD (its last heartbeat looks an hour in the future): the open
    /// path calls it alive (read-only, polling), the polling path finds it dead after 180 seconds of the
    /// reader's own time, and the follow-up "Take over as writer" (a fresh checkout) then offers it
    /// with the confirmation already satisfied — no extra wait.
    /// </summary>
    [Fact]
    public async Task SkewedAheadAndDeadWriter_IsFoundByPolling_ThenTakenOverWithoutAnotherWait()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc) = CreateService(clock);
        var db = CreateDb();
        WriteLock(db, "dead-clock-ahead", clock.GetUtcNow().UtcDateTime.AddHours(1));

        // Open path: the clock says the lock is perfectly fresh, so the session is read-only and polls.
        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));
        Assert.True(lockSvc.IsPolling);

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        await lockSvc.PollLockFile();
        Assert.False(lockSvc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(1));
        await lockSvc.PollLockFile();
        Assert.True(lockSvc.WriteLockBecameAvailable, "180 s of an unchanged heartbeat");

        // The user clicks "Take over as writer": release the read-only session, check the database out again.
        await svc.ReleaseAsync(saveFirst: false);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));
        Assert.Equal(TimeSpan.Zero, svc.GetTakeOverWait());

        Assert.Equal(CheckoutOutcome.WriteAccess, await svc.ForceCheckoutAsync());
        Assert.True(lockSvc.IsWriter);
        lockSvc.Dispose();
    }

    /// <summary>
    /// A lock the clock calls fresh, opened read-only, is NOT offered at the prompt: with the clock hint
    /// quiet and the heartbeat only just seen, nothing says it is abandoned.
    /// </summary>
    [Fact]
    public async Task LockThatLooksFreshAndJustSeen_OpensReadOnly_NoPrompt()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc) = CreateService(clock);
        var db = CreateDb();
        WriteLock(db, "live", clock.GetUtcNow().UtcDateTime);

        Assert.Equal(CheckoutOutcome.ReadOnly, await svc.CheckoutAsync(db));
        lockSvc.Dispose();
    }

    /// <summary>
    /// The wait is measured on the lock service's own time source: a fresh checkout of the very same
    /// unchanged lock after the window has passed has nothing left to wait for, even though that
    /// checkout is a second, separate prompt (the observation carries over, keyed by session + heartbeat).
    /// </summary>
    [Fact]
    public async Task SecondCheckoutOfTheSameUnchangedLock_CarriesTheObservationOver()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, db, _) = await OpenAtStalePromptAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(80));

        await svc.ReleaseAsync(saveFirst: false);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));

        Assert.Equal(TimeSpan.Zero, svc.GetTakeOverWait());
        lockSvc.Dispose();
    }

    /// <summary>
    /// If the heartbeat changed between two prompts, the second prompt starts a NEW window: the earlier
    /// observation of the old heartbeat must not count towards it.
    /// </summary>
    [Fact]
    public async Task HeartbeatChangedBetweenPrompts_StartsANewWindow()
    {
        var clock = new FakeTimeProvider();
        var (svc, lockSvc, db, heartbeat) = await OpenAtStalePromptAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(80));

        WriteLock(db, "dead-or-asleep", heartbeat.AddSeconds(1));     // it changed, though it still looks old
        await svc.ReleaseAsync(saveFirst: false);
        Assert.Equal(CheckoutOutcome.StaleHolder, await svc.CheckoutAsync(db));

        Assert.Equal(Window, svc.GetTakeOverWait());
        lockSvc.Dispose();
    }
}
