using System.Text.Json;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for spec item 8.7, the one scale factor of the lock timers (<see cref="LockTimings"/>): the production values at factor 1, when the
/// factor is above 1, the new <see cref="LockFileData.TimingScale"/> field, and the rule that a lock is judged stale at the HOLDER's speed.
/// Time is a <see cref="FakeTimeProvider"/>; no timers run and no real database is opened (only the lock file mechanics).
/// </summary>
public sealed class LockTimingsTests : IDisposable
{
    /// <summary>A fixed "now" for the fake clock.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A start time for the simulated holders' processes.</summary>
    private static readonly DateTime ProcessStart = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Folder holding the fake database path and its lock file.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lt_" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the folder.</summary>
    public LockTimingsTests() => Directory.CreateDirectory(_dir);

    /// <summary>Removes the folder (best effort).</summary>
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Production values ────────────────────────────────────────────────────

    /// <summary>At factor 1 every timer is exactly its production value: heartbeat 60, poll 60, stale 180, confirmation 170, wake check 30, wake threshold 90.</summary>
    [Fact]
    public void FactorOne_IsExactlyTheProductionValues()
    {
        var t = LockTimings.Normal;
        Assert.Equal(1, t.Scale);
        Assert.False(t.IsScaled);
        Assert.Equal(TimeSpan.FromSeconds(60),  t.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(60),  t.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(180), t.StaleLockThreshold);
        Assert.Equal(TimeSpan.FromSeconds(170), t.StaleConfirmation);
        Assert.Equal(TimeSpan.FromSeconds(30),  t.WakeCheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(90),  t.WakeThreshold);
        Assert.Equal(string.Empty, t.TitleSuffix);

        // The public constants stay the production values the rest of the code and tests read.
        Assert.Equal(60,  WriteLockService.HeartbeatIntervalSeconds);
        Assert.Equal(60,  WriteLockService.PollIntervalSeconds);
        Assert.Equal(180, WriteLockService.StaleLockThresholdSeconds);
        Assert.Equal(170, WriteLockService.StaleConfirmationSeconds);

        // The things that are never scaled.
        Assert.Equal(5000, NetworkFileOps.TimeoutMs);
        Assert.Equal(150,  NetworkFileOps.MoveRetryDelayMs);
    }

    /// <summary>The test host is a Development install with no variable set: factor 1.</summary>
    [Fact]
    public void TheTestHost_RunsAtFactorOne()
    {
        Assert.Equal(1, LockTimings.Current.Scale);
    }

    /// <summary>Factors 2 and 3 divide every timer by the factor; the ratios stay the same.</summary>
    [Theory]
    [InlineData(2, 30, 30, 90, 85, 15, 45)]
    [InlineData(3, 20, 20, 60, 170.0 / 3, 10, 30)]
    public void ScaledTimings_AreTheProductionValuesDividedByTheFactor(
        int scale, double heartbeat, double poll, double stale, double confirmation, double wakeCheck, double wakeThreshold)
    {
        var t = LockTimings.ForScale(scale);
        Assert.Equal(scale, t.Scale);
        Assert.True(t.IsScaled);
        Assert.Equal(heartbeat,     t.HeartbeatInterval.TotalSeconds, 6);
        Assert.Equal(poll,          t.PollInterval.TotalSeconds, 6);
        Assert.Equal(stale,         t.StaleLockThreshold.TotalSeconds, 6);
        Assert.Equal(confirmation,  t.StaleConfirmation.TotalSeconds, 6);
        Assert.Equal(wakeCheck,     t.WakeCheckInterval.TotalSeconds, 6);
        Assert.Equal(wakeThreshold, t.WakeThreshold.TotalSeconds, 6);
        Assert.Equal($" (Test timings ×{scale})", t.TitleSuffix);

        // The ratios carry the safety: stale is still three heartbeats, the wake threshold three wake checks.
        Assert.Equal(3.0, t.StaleLockThreshold / t.HeartbeatInterval, 6);
        Assert.Equal(3.0, t.WakeThreshold / t.WakeCheckInterval, 6);
    }

    /// <summary>A factor outside 1 to 3 (a corrupt or hostile lock file) is treated as 1.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(4)]
    [InlineData(1000)]
    public void ForScale_OutOfRange_IsFactorOne(int scale) => Assert.Same(LockTimings.Normal, LockTimings.ForScale(scale));

    // ── When the factor is above 1 ───────────────────────────────────────────

    /// <summary>The Store and the installer never run fast, whatever the variable says, and the log line says so.</summary>
    [Theory]
    [InlineData(InstallChannel.Store, "2")]
    [InlineData(InstallChannel.Store, "3")]
    [InlineData(InstallChannel.DirectDownload, "2")]
    [InlineData(InstallChannel.DirectDownload, "3")]
    public void StoreAndDirectDownload_AreAlwaysFactorOne(InstallChannel channel, string value)
    {
        Assert.Equal(1, LockTimings.Resolve(channel, value, out var note));
        Assert.NotNull(note);
    }

    /// <summary>Sideload and Development honour 2 and 3.</summary>
    [Theory]
    [InlineData(InstallChannel.Sideload, "2", 2)]
    [InlineData(InstallChannel.Sideload, "3", 3)]
    [InlineData(InstallChannel.Development, "2", 2)]
    [InlineData(InstallChannel.Development, "3", 3)]
    [InlineData(InstallChannel.Development, " 3 ", 3)]
    public void SideloadAndDevelopment_HonourTwoAndThree(InstallChannel channel, string value, int expected)
    {
        Assert.Equal(expected, LockTimings.Resolve(channel, value, out var note));
        Assert.Null(note);
    }

    /// <summary>Anything else is factor 1 with a log line (above 3 included); an unset variable is factor 1 without one.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("4")]
    [InlineData("6")]
    [InlineData("100")]
    [InlineData("-3")]
    [InlineData("+3")]
    [InlineData("2.5")]
    [InlineData("three")]
    [InlineData("99999999999999999999")]
    public void InvalidOrTooLargeValues_AreFactorOne_AndLogged(string value)
    {
        Assert.Equal(1, LockTimings.Resolve(InstallChannel.Development, value, out var note));
        Assert.NotNull(note);
        Assert.Equal(1, LockTimings.Resolve(InstallChannel.Sideload, value, out note));
        Assert.NotNull(note);
    }

    /// <summary>An unset or empty variable is factor 1 and nothing is logged.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoValue_IsFactorOne_Quietly(string? value)
    {
        Assert.Equal(1, LockTimings.Resolve(InstallChannel.Development, value, out var note));
        Assert.Null(note);
    }

    // ── The lock file's new field ────────────────────────────────────────────

    /// <summary>A normal build writes no TimingScale at all (the file is what 1.2.3 wrote); a scaled build records its factor; both read back.</summary>
    [Fact]
    public void TimingScale_IsOmittedAtFactorOne_AndRoundTripsAboveIt()
    {
        var plain = JsonSerializer.Serialize(new LockFileData { Username = "zqxu", Machine = "zqxm" });
        Assert.DoesNotContain("TimingScale", plain);
        Assert.Null(JsonSerializer.Deserialize<LockFileData>(plain)!.TimingScale);

        var scaled = JsonSerializer.Serialize(new LockFileData { Username = "zqxu", Machine = "zqxm", TimingScale = 3 });
        Assert.Contains("\"TimingScale\":3", scaled);
        Assert.Equal(3, JsonSerializer.Deserialize<LockFileData>(scaled)!.TimingScale);
        Assert.Equal(3, WriteLockService.ParseLockFile(scaled)!.TimingScale);
    }

    /// <summary>A lock file written by 1.2.3 (no TimingScale) reads as factor 1.</summary>
    [Fact]
    public void ALockFileWithoutTheField_ReadsAsFactorOne()
    {
        const string old123 = "{\"Username\":\"zqxu\",\"Machine\":\"zqxm\",\"SessionGuid\":\"g\",\"Acquired\":\"2026-10-01T10:00:00Z\"," +
                              "\"Heartbeat\":\"2026-10-01T10:00:00Z\",\"ProcessId\":7,\"ProcessStartTimeUtc\":null}";
        var holder = WriteLockService.ParseLockFile(old123)!;
        Assert.Null(holder.TimingScale);
        Assert.Same(LockTimings.Normal, LockTimings.ForHolder(holder));
        Assert.Same(LockTimings.Normal, LockTimings.ForHolder(null));
    }

    /// <summary>
    /// 1.2.3 reads a lock file that has the new field: its <see cref="LockFileData"/> has no such member, and the default
    /// System.Text.Json options skip unknown members. Modelled here with a copy of the 1.2.3 class shape.
    /// </summary>
    [Fact]
    public void AnOlderReader_IgnoresTheUnknownField()
    {
        var json = JsonSerializer.Serialize(new LockFileData { Username = "zqxu", Machine = "zqxm", SessionGuid = "g", TimingScale = 3 });
        var old = JsonSerializer.Deserialize<Version123LockFile>(json)!;
        Assert.Equal("zqxu", old.Username);
        Assert.Equal("g", old.SessionGuid);
    }

    /// <summary>The lock file as 1.2.3 reads it (no TimingScale member).</summary>
    private sealed class Version123LockFile
    {
        /// <summary>The user name.</summary>
        public string Username { get; set; } = "";

        /// <summary>The session id.</summary>
        public string SessionGuid { get; set; } = "";
    }

    /// <summary>A scaled service writes its factor into its lock file; a normal service writes none.</summary>
    [Fact]
    public void AService_WritesItsOwnFactorIntoItsLockFile()
    {
        var clock = new FakeTimeProvider(Start);
        var scaled = Path.Combine(_dir, "scaled.db");
        var plain  = Path.Combine(_dir, "plain.db");
        using var a = NewService(clock, "ZQX-PC-A", 3);
        using var b = NewService(clock, "ZQX-PC-B", 1);
        a.TryAcquire(scaled);
        Assert.True(a.IsWriter);
        b.TryAcquire(plain);
        Assert.True(b.IsWriter);

        Assert.Equal(3, JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(Path.ChangeExtension(scaled, ".lock")))!.TimingScale);
        Assert.DoesNotContain("TimingScale", File.ReadAllText(Path.ChangeExtension(plain, ".lock")));
    }

    // ── Judged at the holder's speed ─────────────────────────────────────────

    /// <summary>Creates a timer-less service on its own machine with a given factor.</summary>
    /// <param name="clock">The shared fake clock.</param>
    /// <param name="machine">Machine name.</param>
    /// <param name="scale">The service's own factor.</param>
    private static WriteLockService NewService(FakeTimeProvider clock, string machine, int scale)
        => new(clock,
               new LockIdentity(machine, "zqxuser", 100, ProcessStart, p => throw new InvalidOperationException($"unexpected lookup {p}")),
               startTimers: false,
               timings: LockTimings.ForScale(scale));

    /// <summary>
    /// Has a holder of one factor take the lock, a judge of another factor look at it, and returns whether the judge calls it stale after the
    /// given time with the holder silent (no heartbeat).
    /// </summary>
    /// <param name="holderScale">The holder's factor.</param>
    /// <param name="judgeScale">The judge's factor.</param>
    /// <param name="silentSeconds">How long the holder has been silent when the judge looks again.</param>
    /// <returns>True when the judge's open path calls the lock stale.</returns>
    private bool JudgeCallsStale(int holderScale, int judgeScale, double silentSeconds)
    {
        var clock = new FakeTimeProvider(Start);
        var db    = Path.Combine(_dir, $"j{holderScale}{judgeScale}{(int)(silentSeconds * 10)}.db");
        using var holder = NewService(clock, "ZQX-HOLDER-PC", holderScale);
        using var judge  = NewService(clock, "ZQX-JUDGE-PC",  judgeScale);

        holder.TryAcquire(db);
        Assert.True(holder.IsWriter);
        judge.TryAcquire(db);                               // first look: the heartbeat is fresh
        Assert.False(judge.IsStaleLock);
        clock.Advance(TimeSpan.FromSeconds(silentSeconds));
        judge.TryAcquire(db);
        return judge.IsStaleLock;
    }

    /// <summary>A factor-3 judge sees a factor-1 lock stale only after 180 s, not after 60.</summary>
    [Fact]
    public void AFactorThreeJudge_SeesAFactorOneLockStaleOnlyAfter180Seconds()
    {
        Assert.False(JudgeCallsStale(holderScale: 1, judgeScale: 3, silentSeconds: 61));
        Assert.False(JudgeCallsStale(holderScale: 1, judgeScale: 3, silentSeconds: 179));
        Assert.True(JudgeCallsStale(holderScale: 1, judgeScale: 3, silentSeconds: 181));
    }

    /// <summary>A factor-1 judge sees a factor-3 lock stale after 60 s (it heartbeats every 20 s), not after 59.</summary>
    [Fact]
    public void AFactorOneJudge_SeesAFactorThreeLockStaleAfter60Seconds()
    {
        Assert.False(JudgeCallsStale(holderScale: 3, judgeScale: 1, silentSeconds: 59));
        Assert.True(JudgeCallsStale(holderScale: 3, judgeScale: 1, silentSeconds: 61));
    }

    /// <summary>Two computers of the same factor agree with the unscaled rule scaled: factor 2 means 90 s.</summary>
    [Fact]
    public void SameFactor_JudgesAtThatFactor()
    {
        Assert.False(JudgeCallsStale(holderScale: 2, judgeScale: 2, silentSeconds: 89));
        Assert.True(JudgeCallsStale(holderScale: 2, judgeScale: 2, silentSeconds: 91));
        Assert.False(JudgeCallsStale(holderScale: 1, judgeScale: 1, silentSeconds: 179));
        Assert.True(JudgeCallsStale(holderScale: 1, judgeScale: 1, silentSeconds: 181));
    }

    /// <summary>The takeover confirmation is also the holder's: 170 s for a factor-1 lock even to a factor-3 judge, 170/3 s for a factor-3 lock to a factor-1 judge.</summary>
    [Fact]
    public void TheConfirmationWait_IsTheHoldersToo()
    {
        Assert.Equal(TimeSpan.FromSeconds(170),      ConfirmationRemainingRightAfterTheFirstLook(holderScale: 1, judgeScale: 3));
        Assert.Equal(TimeSpan.FromSeconds(170.0 / 3), ConfirmationRemainingRightAfterTheFirstLook(holderScale: 3, judgeScale: 1));
    }

    /// <summary>The judge looks at a lock once and asks how long a takeover must still wait.</summary>
    /// <param name="holderScale">The holder's factor.</param>
    /// <param name="judgeScale">The judge's factor.</param>
    /// <returns>The remaining wait straight after the first look.</returns>
    private TimeSpan ConfirmationRemainingRightAfterTheFirstLook(int holderScale, int judgeScale)
    {
        var clock = new FakeTimeProvider(Start);
        var db    = Path.Combine(_dir, $"c{holderScale}{judgeScale}.db");
        using var holder = NewService(clock, "ZQX-HOLDER-PC", holderScale);
        using var judge  = NewService(clock, "ZQX-JUDGE-PC",  judgeScale);
        holder.TryAcquire(db);
        Assert.True(holder.IsWriter);
        judge.TryAcquire(db);
        return judge.StaleConfirmationRemaining(judge.CurrentHolder!);
    }

    /// <summary>A lock file that records an absurd factor is judged at the production timings (never kept alive for hours).</summary>
    [Fact]
    public void AnAbsurdRecordedFactor_IsJudgedAsFactorOne()
    {
        var clock = new FakeTimeProvider(Start);
        var db    = Path.Combine(_dir, "absurd.db");
        var now   = clock.GetUtcNow().UtcDateTime;
        File.WriteAllText(Path.ChangeExtension(db, ".lock"), JsonSerializer.Serialize(new LockFileData
        {
            Username = "zqxother", Machine = "ZQX-OTHER-PC", SessionGuid = "g", Acquired = now, Heartbeat = now, TimingScale = 1000
        }));
        using var judge = NewService(clock, "ZQX-JUDGE-PC", 1);
        judge.TryAcquire(db);
        Assert.False(judge.IsStaleLock);
        clock.Advance(TimeSpan.FromSeconds(181));
        judge.TryAcquire(db);
        Assert.True(judge.IsStaleLock);
    }
}
