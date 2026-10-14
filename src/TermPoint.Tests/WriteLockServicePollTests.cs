using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for the reader-side lock polling in <see cref="WriteLockService.PollLockFile"/>.
///
/// <para>Regression focus: a transient read failure (sharing violation from the writer's
/// heartbeat rename, or a momentary not-found) must SKIP the poll cycle — it must never be
/// classified as "infinitely stale". Before the fix, an unreadable lock file set
/// <c>CurrentHolder = null</c>, the age computation treated null as <c>double.MaxValue</c>,
/// and a single 5&#160;ms collision permanently flipped <see cref="WriteLockService.WriteLockBecameAvailable"/>
/// (the poll timer is disposed on the first available=true) — offering this reader a takeover
/// of a LIVE writer's lock.</para>
///
/// <para>Each test uses an isolated temp directory. <see cref="WriteLockService.EnterReaderMode"/>
/// puts the service into reader mode without contending for the lock, and
/// <see cref="WriteLockService.PollLockFile"/> (internal, exposed for tests) is invoked
/// directly, bypassing the poll timer and UI-thread dispatch.</para>
/// </summary>
public sealed class WriteLockServicePollTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly string _tempDir;

    /// <summary>Creates an isolated temporary directory for the fake database and lock file.</summary>
    public WriteLockServicePollTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wlsp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Deletes the temporary directory and all files created during the test.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Path of the fake database file inside the temp directory.</summary>
    private string DbPath => Path.Combine(_tempDir, "test.db");

    /// <summary>Path of the lock file that sits alongside <see cref="DbPath"/>.</summary>
    private string LockPath => Path.ChangeExtension(DbPath, ".lock");

    /// <summary>
    /// Writes a lock file owned by a foreign session with the given heartbeat timestamp.
    /// A recent heartbeat simulates a live remote writer; an old one simulates a crashed writer.
    /// </summary>
    private void WriteForeignLock(DateTime heartbeatUtc)
    {
        var data = new LockFileData
        {
            Username    = "other_user",
            Machine     = "OTHER-PC",
            SessionGuid = Guid.NewGuid().ToString(),
            Acquired    = heartbeatUtc,
            Heartbeat   = heartbeatUtc,
        };
        File.WriteAllText(LockPath, JsonSerializer.Serialize(data));
    }

    /// <summary>
    /// Writes a lock file with full control over the session and the heartbeat value, as another
    /// computer's writer would. The heartbeat can be set to anything (an hour behind or ahead of this
    /// computer's clock) to model a writer whose clock is wrong.
    /// </summary>
    private void WriteLock(string sessionGuid, DateTime heartbeatUtc)
    {
        var data = new LockFileData
        {
            Username    = "other_user",
            Machine     = "OTHER-PC",
            SessionGuid = sessionGuid,
            Acquired    = heartbeatUtc,
            Heartbeat   = heartbeatUtc,
        };
        File.WriteAllText(LockPath, JsonSerializer.Serialize(data));
    }

    /// <summary>
    /// Creates a reader-mode <see cref="WriteLockService"/> pointed at <see cref="DbPath"/>.
    /// </summary>
    /// <param name="clock">Fake time source, or null for the real clock.</param>
    private WriteLockService CreateReader(TimeProvider? clock = null)
    {
        File.WriteAllText(DbPath, "dummy-database-content");
        var svc = new WriteLockService(clock);
        svc.EnterReaderMode(DbPath);
        Assert.False(svc.IsWriter); // precondition: reader mode
        return svc;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression: when the lock file exists but the read is refused with a sharing
    /// violation (here forced deterministically by holding it open with
    /// <see cref="FileShare.None"/>, mimicking the writer's heartbeat rename window),
    /// the poll must skip the cycle — NOT offer write access. Before the fix this
    /// flipped a permanent "switch to edit mode" offer against a live writer.
    /// </summary>
    [Fact]
    public async Task PollLockFile_LockReadContended_SkipsCycle_NoFalseAvailability()
    {
        WriteForeignLock(DateTime.UtcNow); // fresh heartbeat — a LIVE remote writer
        using var svc = CreateReader();

        using (var holder = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await svc.PollLockFile();
        }

        Assert.False(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// Control: a readable lock with a FRESH heartbeat is respected — no availability offer.
    /// Confirms the contended-read test above isn't passing vacuously.
    /// </summary>
    [Fact]
    public async Task PollLockFile_FreshLock_NoAvailabilityOffer()
    {
        WriteForeignLock(DateTime.UtcNow);
        using var svc = CreateReader();

        await svc.PollLockFile();

        Assert.False(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// Control: a readable lock whose heartbeat has stood still for the stale threshold DOES
    /// offer write access. Confirms the skip-on-contention fix did not suppress the
    /// legitimate stale-lock detection path. (Spec item 9b: "stood still" is measured on the
    /// reader's own clock, here a fake one, not read off the timestamp inside the file.)
    /// </summary>
    [Fact]
    public async Task PollLockFile_LockUnchangedForTheThreshold_OffersWriteAccess()
    {
        var clock = new FakeTimeProvider();
        WriteForeignLock(DateTime.UtcNow.AddSeconds(-(WriteLockService.StaleLockThresholdSeconds + 30)));
        using var svc = CreateReader(clock);

        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable, "Just seen — nothing yet proves it stood still.");

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds));
        await svc.PollLockFile();

        Assert.True(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// A readable but CORRUPT lock file is still offered for takeover (an unusable lock is not
    /// respected forever) — but, like any lock, only after it has stood unchanged for the threshold,
    /// so one garbled read (a half-synced file, say) cannot flip a permanent offer.
    /// </summary>
    [Fact]
    public async Task PollLockFile_CorruptLock_OffersWriteAccessOnlyAfterTheThreshold()
    {
        var clock = new FakeTimeProvider();
        File.WriteAllText(LockPath, "{ this is not valid json !!");
        using var svc = CreateReader(clock);

        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds));
        await svc.PollLockFile();

        Assert.True(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// A lock that is garbled for a moment and then readable again was a transient: becoming readable
    /// is a change, so the measurement restarts and no takeover is offered.
    /// </summary>
    [Fact]
    public async Task PollLockFile_GarbledThenReadableAgain_RestartsTheMeasurement()
    {
        var clock = new FakeTimeProvider();
        var session = Guid.NewGuid().ToString();
        File.WriteAllText(LockPath, "");                       // e.g. a half-synced, empty file
        using var svc = CreateReader(clock);
        await svc.PollLockFile();

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        WriteLock(session, DateTime.UtcNow);                    // the real lock arrives
        await svc.PollLockFile();
        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        await svc.PollLockFile();

        Assert.False(svc.WriteLockBecameAvailable);
    }

    // ── Spec item 9b: judged by an unchanged heartbeat on the local clock ─────

    /// <summary>
    /// Boundary: unchanged for one second short of the threshold is not stale; at the threshold it is.
    /// </summary>
    [Fact]
    public async Task PollLockFile_UnchangedFor179Seconds_NotStale_At180_Stale()
    {
        var clock = new FakeTimeProvider();
        WriteLock("s1", DateTime.UtcNow);
        using var svc = CreateReader(clock);
        await svc.PollLockFile();                                   // first sight, t = 0

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(1));
        await svc.PollLockFile();
        Assert.True(svc.WriteLockBecameAvailable);
    }

    /// <summary>
    /// A writer whose clock is far BEHIND this computer's (every heartbeat looks an hour old) but which
    /// keeps renewing it every minute is alive, and must never be offered for takeover. This is the
    /// bug: comparing the two clocks made such a writer look dead.
    /// </summary>
    [Fact]
    public async Task PollLockFile_SkewedBehindButRenewing_IsNeverStale()
    {
        var clock = new FakeTimeProvider();
        var behind = DateTime.UtcNow.AddHours(-1);
        WriteLock("live", behind);
        using var svc = CreateReader(clock);

        for (var minute = 0; minute < 30; minute++)
        {
            await svc.PollLockFile();
            Assert.False(svc.WriteLockBecameAvailable, $"offered for takeover after {minute} minutes");

            clock.Advance(TimeSpan.FromSeconds(WriteLockService.HeartbeatIntervalSeconds));
            behind = behind.AddSeconds(WriteLockService.HeartbeatIntervalSeconds);
            WriteLock("live", behind);                              // renewed — still an hour "old"
        }
    }

    /// <summary>
    /// A writer whose clock is far AHEAD (its last heartbeat looks an hour in the future) that has died
    /// is still found out: the heartbeat stops changing, and 180 seconds of the reader's own time later
    /// the takeover is offered. Comparing clocks would have called it alive for another hour.
    /// </summary>
    [Fact]
    public async Task PollLockFile_SkewedAheadAndDead_BecomesStaleAfterTheThreshold()
    {
        var clock = new FakeTimeProvider();
        WriteLock("dead", DateTime.UtcNow.AddHours(1));
        using var svc = CreateReader(clock);
        await svc.PollLockFile();

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(1));
        await svc.PollLockFile();
        Assert.True(svc.WriteLockBecameAvailable);
    }

    /// <summary>A heartbeat change at 170 seconds restarts the count: another 179 seconds is still not enough.</summary>
    [Fact]
    public async Task PollLockFile_HeartbeatChangeAt170Seconds_ResetsTheTimer()
    {
        var clock = new FakeTimeProvider();
        var t0 = DateTime.UtcNow;
        WriteLock("s1", t0);
        using var svc = CreateReader(clock);
        await svc.PollLockFile();

        clock.Advance(TimeSpan.FromSeconds(170));
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);
        WriteLock("s1", t0.AddSeconds(170));                        // the writer renewed
        await svc.PollLockFile();                                   // sees the change, restarts at 0

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(1));
        await svc.PollLockFile();
        Assert.True(svc.WriteLockBecameAvailable);
    }

    /// <summary>A different session with the SAME heartbeat value (another machine took over) also resets the timer.</summary>
    [Fact]
    public async Task PollLockFile_SessionChange_ResetsTheTimer()
    {
        var clock = new FakeTimeProvider();
        var heartbeat = DateTime.UtcNow;
        WriteLock("first", heartbeat);
        using var svc = CreateReader(clock);
        await svc.PollLockFile();

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        WriteLock("second", heartbeat);                             // new session, identical heartbeat value
        await svc.PollLockFile();                                   // restarts at 0

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        await svc.PollLockFile();

        Assert.False(svc.WriteLockBecameAvailable);
    }

    /// <summary>A missing lock file is "available" at once, with no waiting, even mid-measurement.</summary>
    [Fact]
    public async Task PollLockFile_LockFileGone_IsAvailableImmediately_EvenMidMeasurement()
    {
        var clock = new FakeTimeProvider();
        WriteLock("s1", DateTime.UtcNow);
        using var svc = CreateReader(clock);
        await svc.PollLockFile();
        clock.Advance(TimeSpan.FromSeconds(10));
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        File.Delete(LockPath);
        await svc.PollLockFile();

        Assert.True(svc.WriteLockBecameAvailable);
    }

    // ── Spec item 9b: the open path (TryAcquire) and the takeover confirmation ──

    /// <summary>
    /// The browser demo registers <see cref="WriteLockService"/> with <c>AddSingleton&lt;WriteLockService&gt;()</c>
    /// and no <see cref="TimeProvider"/>: the constructor's optional time-source parameter must not stop the
    /// container from building it (it falls back to the system clock).
    /// </summary>
    [Fact]
    public void WriteLockService_ResolvesFromTheContainer_WithoutATimeProviderRegistered()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<WriteLockService>()
            .BuildServiceProvider();

        var svc = provider.GetRequiredService<WriteLockService>();

        Assert.NotNull(svc);
        Assert.Same(TimeProvider.System, svc.Time);
    }

    /// <summary>
    /// Open path, writer's clock ahead: a heartbeat an hour in the FUTURE is not offered by the clock
    /// hint, so the open is read-only and polls — it is the polling path that later finds it dead.
    /// </summary>
    [Fact]
    public void TryAcquire_HeartbeatFarInTheFuture_IsNotHintedStale_AndStartsPolling()
    {
        var clock = new FakeTimeProvider();
        File.WriteAllText(DbPath, "dummy-database-content");
        WriteLock("ahead", DateTime.UtcNow.AddHours(1));
        using var svc = new WriteLockService(clock);

        svc.TryAcquire(DbPath);

        Assert.False(svc.IsWriter);
        Assert.False(svc.IsStaleLock);
        Assert.True(svc.IsPolling);
    }

    /// <summary>
    /// The same future-dated heartbeat, still unchanged 180 seconds later by the reader's own clock, IS
    /// offered when the user re-opens (the "Take over as writer" click): the open path accepts the local
    /// observation as well as the clock hint.
    /// </summary>
    [Fact]
    public void TryAcquire_SameHeartbeatUnchangedForTheThreshold_IsStale_EvenThoughTheClockSaysFresh()
    {
        var clock = new FakeTimeProvider();
        File.WriteAllText(DbPath, "dummy-database-content");
        WriteLock("ahead", DateTime.UtcNow.AddHours(1));
        using var svc = new WriteLockService(clock);
        svc.TryAcquire(DbPath);
        Assert.False(svc.IsStaleLock, "precondition: first sight, clock says fresh");

        clock.Advance(TimeSpan.FromSeconds(WriteLockService.StaleLockThresholdSeconds - 1));
        svc.TryAcquire(DbPath);
        Assert.False(svc.IsStaleLock);

        clock.Advance(TimeSpan.FromSeconds(1));
        svc.TryAcquire(DbPath);
        Assert.True(svc.IsStaleLock);
    }

    /// <summary>
    /// The confirmation window counts down on the local clock from the first sighting, is full for a
    /// snapshot that is not being timed, and never goes below zero.
    /// </summary>
    [Fact]
    public void StaleConfirmationRemaining_CountsDownFromTheFirstSighting()
    {
        var clock = new FakeTimeProvider();
        var window = TimeSpan.FromSeconds(WriteLockService.StaleConfirmationSeconds);
        File.WriteAllText(DbPath, "dummy-database-content");
        WriteLock("old", DateTime.UtcNow.AddMinutes(-20));
        using var svc = new WriteLockService(clock);
        svc.TryAcquire(DbPath);
        Assert.True(svc.IsStaleLock);
        var judged = svc.CurrentHolder!;

        Assert.Equal(window, svc.StaleConfirmationRemaining(judged));
        clock.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal(window - TimeSpan.FromSeconds(25), svc.StaleConfirmationRemaining(judged));
        clock.Advance(window);
        Assert.Equal(TimeSpan.Zero, svc.StaleConfirmationRemaining(judged));

        // A snapshot the service is not timing has the whole window ahead of it.
        var other = new LockFileData { SessionGuid = "someone-else", Heartbeat = DateTime.UtcNow };
        Assert.Equal(window, svc.StaleConfirmationRemaining(other));
    }

    /// <summary>
    /// The 180 seconds are counted from the first time THIS process saw the heartbeat — at the open-time
    /// stale prompt — not from when read-only polling began. Here the open-time look is 100 seconds
    /// before the poll's first tick, so only 80 more seconds are needed.
    /// </summary>
    [Fact]
    public async Task PollLockFile_SeededFromTheOpenTimeObservation_CountsFromTheFirstSighting()
    {
        var clock = new FakeTimeProvider();
        File.WriteAllText(DbPath, "dummy-database-content");
        WriteLock("old", DateTime.UtcNow.AddMinutes(-20));          // looks long dead by the clock: the prompt case
        using var svc = new WriteLockService(clock);
        svc.TryAcquire(DbPath);                                     // open path: reads and records the holder
        Assert.True(svc.IsStaleLock, "precondition: the clock hint offered the takeover");

        clock.Advance(TimeSpan.FromSeconds(100));                   // the user read the prompt, declined
        svc.EnterReaderMode(DbPath);                                // read-only; polling starts
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(79));                    // 179 s since the first sighting
        await svc.PollLockFile();
        Assert.False(svc.WriteLockBecameAvailable);

        clock.Advance(TimeSpan.FromSeconds(1));                     // 180 s
        await svc.PollLockFile();
        Assert.True(svc.WriteLockBecameAvailable);
    }
}
