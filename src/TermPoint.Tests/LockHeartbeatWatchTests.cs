using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="LockHeartbeatWatch"/> (spec item 9b): it times how long the lock holder's
/// snapshot (session + heartbeat) has stood unchanged on a LOCAL monotonic clock, and restarts on any
/// change. The heartbeat's value is never compared with the local clock, which these tests prove by
/// using heartbeats an hour in the past and in the future.
/// </summary>
public sealed class LockHeartbeatWatchTests
{
    private const string LockPath = @"C:\share\test.lock";

    /// <summary>Builds a holder snapshot with the given session and heartbeat.</summary>
    private static LockFileData Holder(string session, DateTime heartbeat) => new()
    {
        Username    = "other_user",
        Machine     = "OTHER-PC",
        SessionGuid = session,
        Acquired    = heartbeat,
        Heartbeat   = heartbeat,
    };

    private static (LockHeartbeatWatch watch, FakeTimeProvider clock) Create()
    {
        var clock = new FakeTimeProvider();
        return (new LockHeartbeatWatch(clock), clock);
    }

    /// <summary>The first sighting of a snapshot starts the measurement at zero.</summary>
    [Fact]
    public void Observe_FirstSighting_ReturnsZero()
    {
        var (watch, _) = Create();

        Assert.Equal(TimeSpan.Zero, watch.Observe(LockPath, Holder("a", DateTime.UtcNow)));
    }

    /// <summary>Seeing the identical snapshot again reports how long it has stood, in local time.</summary>
    [Fact]
    public void Observe_SameSnapshotLater_ReportsLocalElapsedTime()
    {
        var (watch, clock) = Create();
        var holder = Holder("a", DateTime.UtcNow);
        watch.Observe(LockPath, holder);

        clock.Advance(TimeSpan.FromSeconds(95));

        Assert.Equal(TimeSpan.FromSeconds(95), watch.Observe(LockPath, holder));
    }

    /// <summary>
    /// The heartbeat's VALUE is irrelevant: one an hour in the past and one an hour in the future both
    /// measure from the moment they were first seen, never against the local clock.
    /// </summary>
    [Theory]
    [InlineData(-3600)]
    [InlineData(+3600)]
    public void Observe_HeartbeatFarFromTheLocalClock_IsStillMeasuredFromFirstSight(int heartbeatOffsetSeconds)
    {
        var (watch, clock) = Create();
        var holder = Holder("a", DateTime.UtcNow.AddSeconds(heartbeatOffsetSeconds));

        Assert.Equal(TimeSpan.Zero, watch.Observe(LockPath, holder));
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), watch.Observe(LockPath, holder));
    }

    /// <summary>A new heartbeat (the writer renewed) restarts the measurement from zero.</summary>
    [Fact]
    public void Observe_HeartbeatChanged_RestartsFromZero()
    {
        var (watch, clock) = Create();
        var t0 = DateTime.UtcNow;
        watch.Observe(LockPath, Holder("a", t0));
        clock.Advance(TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.Zero, watch.Observe(LockPath, Holder("a", t0.AddSeconds(60))));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), watch.Observe(LockPath, Holder("a", t0.AddSeconds(60))));
    }

    /// <summary>A different session (another machine took the lock) restarts the measurement.</summary>
    [Fact]
    public void Observe_SessionChanged_RestartsFromZero()
    {
        var (watch, clock) = Create();
        var t0 = DateTime.UtcNow;
        watch.Observe(LockPath, Holder("a", t0));
        clock.Advance(TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.Zero, watch.Observe(LockPath, Holder("b", t0)));   // same heartbeat, new session
    }

    /// <summary>A different lock file path (another database) restarts the measurement.</summary>
    [Fact]
    public void Observe_PathChanged_RestartsFromZero()
    {
        var (watch, clock) = Create();
        var holder = Holder("a", DateTime.UtcNow);
        watch.Observe(LockPath, holder);
        clock.Advance(TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.Zero, watch.Observe(@"C:\share\other.lock", holder));
    }

    /// <summary>UnchangedFor reports the elapsed time of the snapshot being timed without recording anything.</summary>
    [Fact]
    public void UnchangedFor_TimedSnapshot_ReportsElapsed_AndForeignSnapshotIsZero()
    {
        var (watch, clock) = Create();
        var t0 = DateTime.UtcNow;
        var holder = Holder("a", t0);
        watch.Observe(LockPath, holder);
        clock.Advance(TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), watch.UnchangedFor(LockPath, holder));
        Assert.Equal(TimeSpan.Zero, watch.UnchangedFor(LockPath, Holder("a", t0.AddSeconds(60))));
        // The foreign lookup recorded nothing: the original is still timed from its first sight.
        Assert.Equal(TimeSpan.FromSeconds(42), watch.UnchangedFor(LockPath, holder));
    }

    /// <summary>A snapshot that was never observed has stood for zero time.</summary>
    [Fact]
    public void UnchangedFor_NothingObserved_IsZero()
    {
        var (watch, _) = Create();

        Assert.Equal(TimeSpan.Zero, watch.UnchangedFor(LockPath, Holder("a", DateTime.UtcNow)));
    }

    /// <summary>Clear forgets the observation, so the same snapshot is timed from scratch afterwards.</summary>
    [Fact]
    public void Clear_ForgetsTheObservation()
    {
        var (watch, clock) = Create();
        var holder = Holder("a", DateTime.UtcNow);
        watch.Observe(LockPath, holder);
        clock.Advance(TimeSpan.FromSeconds(100));

        watch.Clear();

        Assert.Equal(TimeSpan.Zero, watch.UnchangedFor(LockPath, holder));
        Assert.Equal(TimeSpan.Zero, watch.Observe(LockPath, holder));
    }

    /// <summary>
    /// Elapsed time comes from the monotonic stopwatch, not the UTC clock: moving the wall clock
    /// without moving the stopwatch (a clock adjustment) changes nothing.
    /// </summary>
    [Fact]
    public void Observe_IsUnaffectedByTheWallClock_OnlyByTheStopwatch()
    {
        // A provider whose UTC clock jumps by an hour while its stopwatch has not moved.
        var clock = new JumpingWallClock();
        var watch = new LockHeartbeatWatch(clock);
        var holder = Holder("a", DateTime.UtcNow);
        watch.Observe(LockPath, holder);

        clock.JumpWallClock(TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.Zero, watch.Observe(LockPath, holder));
    }

    /// <summary>A time provider whose wall clock can be moved without moving the stopwatch.</summary>
    private sealed class JumpingWallClock : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public void JumpWallClock(TimeSpan by) => _utcNow += by;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => 0;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
