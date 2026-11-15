using System.Globalization;
using System.Text.Json;
using TermPoint.Licensing;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// 1.2.4 trial hardening: a start date in the future is reset, days never exceed 30, the start is parsed
/// culture-independently, a failed write never fails the open, and a brand-new user's whole trial lifecycle.
/// </summary>
public sealed class TrialServiceHardeningTests : IDisposable
{
    private readonly LicenseTestKit _kit = new();
    private readonly string _appData;

    public TrialServiceHardeningTests() => _appData = _kit.NewDir("trial");

    public void Dispose() => _kit.Dispose();

    private string TrialFile => Path.Combine(_appData, "trial.json");

    private void SeedRaw(string json) => File.WriteAllText(TrialFile, json);

    private static string OldFormat(DateTime utc)
        => "{\"trialStartedUtc\":\"" + utc.ToString("o", CultureInfo.InvariantCulture) + "\",\"version\":1}";

    [Fact]
    public void FutureStart_IsResetToNow_And30Days_AndFileRewritten()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SeedRaw(OldFormat(now.AddDays(365)));
        var logged = new List<string>();

        var result = new TrialService(_appData, new TestClock(now), logged.Add).GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(30, result.DaysRemaining);
        Assert.Equal(now.AddDays(30), result.TrialEndsUtc);
        Assert.Single(logged);

        // The rewritten file now holds "now", so a later look sees day 1 consumed, not another reset.
        var later = new TrialService(_appData, new TestClock(now.AddDays(1)))
            .GetTrialStatus();
        Assert.Equal(29, later.DaysRemaining);
    }

    [Fact]
    public void StartOneHourInFuture_Gives30_Not31()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SeedRaw(OldFormat(now.AddHours(1)));

        var result = new TrialService(_appData, new TestClock(now)).GetTrialStatus();

        Assert.Equal(30, result.DaysRemaining);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(-29)]
    public void DaysRemaining_NeverAbove30(int startOffsetDays)
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SeedRaw(OldFormat(now.AddDays(startOffsetDays)));

        var result = new TrialService(_appData, new TestClock(now)).GetTrialStatus();

        Assert.InRange(result.DaysRemaining, 1, 30);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void Start_IsParsedInvariant_UnderOtherCultures(string culture)
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SeedRaw(OldFormat(now.AddDays(-10)));

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var result = new TrialService(_appData, new TestClock(now)).GetTrialStatus();
            Assert.Equal(20, result.DaysRemaining);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void WriteIsInvariant_UnderOtherCultures()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            new TrialService(_appData, new TestClock(now)).GetTrialStatus();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var written = JsonDocument.Parse(File.ReadAllText(TrialFile)).RootElement.GetProperty("trialStartedUtc").GetString();
        Assert.StartsWith("2026-11-01T12:00:00", written);
    }

    [Fact]
    public void UnwritableAppData_DoesNotThrow_And_IsInTrial30Days()
    {
        // AppData "directory" is really an existing file, so creating/writing trial.json must fail.
        var blocker = Path.Combine(_kit.NewDir("blocker"), "notadir");
        File.WriteAllText(blocker, "x");
        var logged = new List<string>();
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = new TrialService(blocker, new TestClock(now), logged.Add).GetTrialStatus();

        Assert.True(result.IsInTrial);
        Assert.Equal(30, result.DaysRemaining);
        Assert.Contains(logged, l => l.Contains("trial.json"));
    }

    [Fact]
    public void TrialEndsUtc_IsStartPlus30Days()
    {
        var start = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        SeedRaw(OldFormat(start));

        var result = new TrialService(_appData, new TestClock(start.AddDays(5))).GetTrialStatus();

        Assert.Equal(start.AddDays(30), result.TrialEndsUtc);
    }

    // ── Brand-new user and upgrade lifecycle ─────────────────────────────────

    [Fact]
    public void BrandNewUser_Day0_30_Day29_1_Day30_ReadOnly()
    {
        var share = _kit.NewDir("share");
        var t0 = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(t0);
        var manager = _kit.Manager(_appData, clock, out _);

        var first = manager.EvaluateAccess(share);
        Assert.Equal(AccessReason.Trial, first.Reason);
        Assert.Equal(AccessLevel.FullAccess, first.AccessLevel);
        Assert.Equal(30, first.DaysRemaining);
        Assert.Equal(t0.AddDays(30), first.TrialEndsUtc);

        clock.UtcNow = t0.AddDays(29);
        Assert.Equal(1, manager.EvaluateAccess(share).DaysRemaining);

        clock.UtcNow = t0.AddDays(30);
        var ended = manager.EvaluateAccess(share);
        Assert.Equal(AccessReason.Unlicensed, ended.Reason);
        Assert.Equal(AccessLevel.ReadOnly, ended.AccessLevel);
    }

    [Fact]
    public void UpgradeFromOldTrialJson_KeepsExactlyItsRemainingDays()
    {
        var share = _kit.NewDir("share");
        var start = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        SeedRaw(OldFormat(start));
        var clock = new TestClock(start.AddDays(11).AddHours(3));

        var result = _kit.Manager(_appData, clock, out _).EvaluateAccess(share);

        Assert.Equal(19, result.DaysRemaining);
        // And the old file was not rewritten.
        Assert.Equal(OldFormat(start), File.ReadAllText(TrialFile));
    }
}
