using System.Text.Json;
using TermPoint.Licensing;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Integration tests for <see cref="ITrialService"/>.
///
/// <para>Each test uses an isolated temp directory as the AppData root
/// and a <see cref="FixedClock"/> for deterministic date math.</para>
///
/// <para>Tests are organised into groups:
/// <list type="bullet">
///   <item><description>Group 1 — First launch (no trial.json exists)</description></item>
///   <item><description>Group 2 — Mid-trial (days remaining)</description></item>
///   <item><description>Group 3 — Trial expiry boundaries</description></item>
///   <item><description>Group 4 — Corrupt / missing trial file</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class TrialServiceTests : IDisposable
{
    private readonly string _appDataDir;

    public TrialServiceTests()
    {
        _appDataDir = Path.Combine(Path.GetTempPath(), $"trial_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_appDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_appDataDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TrialService CreateService(IClock clock)
    {
        return new TrialService(_appDataDir, clock);
    }

    private static IClock ClockAt(int year, int month, int day)
    {
        return new FixedClock(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Writes a trial.json file directly, simulating a prior first-launch.
    /// </summary>
    private void SeedTrialFile(DateTime trialStartedUtc)
    {
        var json = JsonSerializer.Serialize(new
        {
            trialStartedUtc = trialStartedUtc.ToString("o"),
            version = 1
        });
        File.WriteAllText(Path.Combine(_appDataDir, "trial.json"), json);
    }

    // ── Group 1: First launch ────────────────────────────────────────────────

    [Fact]
    public void FirstLaunch_CreatesTrialFile_Returns30Days()
    {
        var clock = ClockAt(2026, 7, 1);
        var service = CreateService(clock);

        var result = service.GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(30, result.DaysRemaining);
        Assert.False(result.TrialExpired);
        Assert.True(File.Exists(Path.Combine(_appDataDir, "trial.json")));
    }

    [Fact]
    public void FirstLaunch_SubsequentCall_SameDay_Still30Days()
    {
        var clock = ClockAt(2026, 7, 1);
        var service = CreateService(clock);

        service.GetTrialStatus();
        var result = service.GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(30, result.DaysRemaining);
    }

    // ── Group 2: Mid-trial ───────────────────────────────────────────────────

    [Fact]
    public void MidTrial_Day1_Returns29Days()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(ClockAt(2026, 7, 2));
        var result = service.GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(29, result.DaysRemaining);
        Assert.False(result.TrialExpired);
    }

    [Fact]
    public void MidTrial_Day15_Returns15Days()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(ClockAt(2026, 7, 16));
        var result = service.GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(15, result.DaysRemaining);
    }

    [Fact]
    public void MidTrial_Day29_Returns1Day()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(ClockAt(2026, 7, 30));
        var result = service.GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(1, result.DaysRemaining);
        Assert.False(result.TrialExpired);
    }

    // ── Group 3: Trial expiry boundaries ─────────────────────────────────────

    [Fact]
    public void ExpiryBoundary_Day30_TrialExpired()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(ClockAt(2026, 7, 31));
        var result = service.GetTrialStatus();

        Assert.False(result.IsInTrial);
        Assert.Equal(0, result.DaysRemaining);
        Assert.True(result.TrialExpired);
    }

    [Fact]
    public void ExpiryBoundary_Day31_TrialExpired()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(ClockAt(2026, 8, 1));
        var result = service.GetTrialStatus();

        Assert.False(result.IsInTrial);
        Assert.Equal(0, result.DaysRemaining);
        Assert.True(result.TrialExpired);
    }

    [Fact]
    public void ExpiryBoundary_WellPast_TrialExpired()
    {
        SeedTrialFile(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var service = CreateService(ClockAt(2026, 12, 31));
        var result = service.GetTrialStatus();

        Assert.False(result.IsInTrial);
        Assert.True(result.TrialExpired);
    }

    // ── Group 4: Corrupt / missing trial file ────────────────────────────────

    [Fact]
    public void CorruptTrialFile_TreatedAsFirstLaunch()
    {
        File.WriteAllText(Path.Combine(_appDataDir, "trial.json"), "not valid json {{{");

        var clock = ClockAt(2026, 7, 1);
        var service = CreateService(clock);
        var result = service.GetTrialStatus();

        // Should start a fresh trial rather than crash
        Assert.True(result.IsInTrial);
        Assert.Equal(30, result.DaysRemaining);
    }

    [Fact]
    public void EmptyTrialFile_TreatedAsFirstLaunch()
    {
        File.WriteAllText(Path.Combine(_appDataDir, "trial.json"), "");

        var clock = ClockAt(2026, 7, 1);
        var service = CreateService(clock);
        var result = service.GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(30, result.DaysRemaining);
    }

    // ── Test clock ───────────────────────────────────────────────────────────

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    }
}
