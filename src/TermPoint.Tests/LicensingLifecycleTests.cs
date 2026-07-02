using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TermPoint.Licensing;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// End-to-end lifecycle tests for the licensing system. Each test simulates a
/// real-world scenario by manipulating <c>termpoint.lic</c> and <c>trial.json</c>
/// on disk, then evaluating <see cref="AppAccessResult"/> through the full
/// validation + trial + composite stack.
///
/// <para>Scenarios mirror the manual test plan (Tests 1–12):</para>
/// <list type="bullet">
///   <item><description>1  — Fresh trial (first launch, no license)</description></item>
///   <item><description>2  — Mid-trial (day 15)</description></item>
///   <item><description>3  — Trial last day (day 29)</description></item>
///   <item><description>4  — Trial expired (day 31)</description></item>
///   <item><description>5  — Valid permanent license</description></item>
///   <item><description>6  — Valid expiring license (still valid)</description></item>
///   <item><description>7  — Expired license, trial also expired</description></item>
///   <item><description>8  — Expired license, fresh user (trial seeded by license path)</description></item>
///   <item><description>9  — Brand new user, no files at all</description></item>
///   <item><description>10 — Corrupt license file, trial active</description></item>
///   <item><description>11 — Staff transition (license present, no trial file)</description></item>
///   <item><description>12 — License renewal (replace expired with valid)</description></item>
/// </list>
/// </summary>
public sealed class LicensingLifecycleTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _shareDir;
    private readonly string _appDataDir;
    private readonly RSA _rsa;
    private readonly string _publicKeyPem;

    public LicensingLifecycleTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir = Path.Combine(Path.GetTempPath(), $"lic_lifecycle_share_{id}");
        _appDataDir = Path.Combine(Path.GetTempPath(), $"lic_lifecycle_appdata_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_appDataDir);

        _rsa = RSA.Create(2048);
        _publicKeyPem = _rsa.ExportRSAPublicKeyPem();
    }

    public void Dispose()
    {
        _rsa.Dispose();
        try { Directory.Delete(_shareDir, recursive: true); } catch { }
        try { Directory.Delete(_appDataDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AppLicenseManager CreateManager(IClock clock)
    {
        var validator = new LicenseValidator(_publicKeyPem, clock);
        var trial = new TrialService(_appDataDir, clock);
        return new AppLicenseManager(validator, trial);
    }

    private static IClock ClockAt(int year, int month, int day)
        => new FixedClock(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));

    private void WriteLicenseFile(LicensePayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var signature = _rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var content = new StringBuilder();
        content.AppendLine("# TermPoint License");
        content.AppendLine($"# Department: {payload.Department}");
        content.AppendLine($"# Expires:    {payload.Expiry ?? "Never"}");
        content.AppendLine("#");
        content.AppendLine();
        content.AppendLine(Convert.ToBase64String(payloadBytes));
        content.AppendLine();
        content.AppendLine(Convert.ToBase64String(signature));

        File.WriteAllText(LicFilePath, content.ToString());
    }

    private void DeleteLicenseFile()
    {
        if (File.Exists(LicFilePath))
            File.Delete(LicFilePath);
    }

    private void SeedTrialFile(DateTime trialStartedUtc)
    {
        var json = JsonSerializer.Serialize(new
        {
            trialStartedUtc = trialStartedUtc.ToString("o"),
            version = 1
        });
        File.WriteAllText(TrialFilePath, json);
    }

    private void DeleteTrialFile()
    {
        if (File.Exists(TrialFilePath))
            File.Delete(TrialFilePath);
    }

    private string LicFilePath => Path.Combine(_shareDir, "termpoint.lic");
    private string TrialFilePath => Path.Combine(_appDataDir, "trial.json");

    // ── Test 1: Fresh trial (first launch) ──────────────────────────────────

    [Fact]
    public void Test01_FreshTrial_FirstLaunch_FullAccess30Days()
    {
        // No license file, no trial.json — simulates very first launch.
        var result = CreateManager(ClockAt(2026, 9, 1)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(30, result.DaysRemaining);
        Assert.Null(result.DepartmentName);
        Assert.True(result.ShowPurchasePrompt);
        Assert.True(File.Exists(TrialFilePath), "trial.json should be created on first launch");
    }

    // ── Test 2: Mid-trial (day 15) ──────────────────────────────────────────

    [Fact]
    public void Test02_MidTrial_Day15_FullAccess15Days()
    {
        SeedTrialFile(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = CreateManager(ClockAt(2026, 9, 16)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(15, result.DaysRemaining);
    }

    // ── Test 3: Trial last day (day 29) ─────────────────────────────────────

    [Fact]
    public void Test03_TrialLastDay_Day29_FullAccess1Day()
    {
        SeedTrialFile(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = CreateManager(ClockAt(2026, 9, 30)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(1, result.DaysRemaining);
    }

    // ── Test 4: Trial expired (day 31) ──────────────────────────────────────

    [Fact]
    public void Test04_TrialExpired_Day31_ReadOnly()
    {
        SeedTrialFile(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = CreateManager(ClockAt(2026, 10, 2)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Unlicensed, result.Reason);
        Assert.True(result.ShowPurchasePrompt);
    }

    // ── Test 5: Valid permanent license ──────────────────────────────────────

    [Fact]
    public void Test05_ValidPermanentLicense_FullAccess()
    {
        WriteLicenseFile(new LicensePayload
        {
            Department = "Test University",
            Issued = "2026-09-01",
            Expiry = null,
            LicenseVersion = 1
        });

        var result = CreateManager(ClockAt(2026, 9, 15)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Licensed, result.Reason);
        Assert.Equal("Test University", result.DepartmentName);
        Assert.Null(result.ExpiryDate);
        Assert.False(result.ShowPurchasePrompt);
        Assert.True(File.Exists(TrialFilePath), "trial.json should be seeded even when licensed");
    }

    // ── Test 6: Valid expiring license (still valid) ─────────────────────────

    [Fact]
    public void Test06_ValidExpiringLicense_FullAccess()
    {
        WriteLicenseFile(new LicensePayload
        {
            Department = "Test U",
            Issued = "2026-09-01",
            Expiry = "2027-09-01",
            LicenseVersion = 1
        });

        var result = CreateManager(ClockAt(2026, 12, 15)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Licensed, result.Reason);
        Assert.Equal("Test U", result.DepartmentName);
        Assert.NotNull(result.ExpiryDate);
        Assert.False(result.ShowPurchasePrompt);
    }

    // ── Test 7: Expired license, trial also expired ─────────────────────────

    [Fact]
    public void Test07_ExpiredLicense_TrialAlsoExpired_ReadOnly()
    {
        SeedTrialFile(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteLicenseFile(new LicensePayload
        {
            Department = "Test U",
            Issued = "2026-09-01",
            Expiry = "2027-09-01",
            LicenseVersion = 1
        });

        var result = CreateManager(ClockAt(2027, 10, 1)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Expired, result.Reason);
        Assert.Equal("Test U", result.DepartmentName);
        Assert.True(result.ShowPurchasePrompt);
    }

    // ── Test 8: Expired license, fresh user ──────────────────────────────────
    // A new user whose first launch has a license that's already expired.
    // The trial is seeded by EvaluateAccess, so they fall through to trial.

    [Fact]
    public void Test08_ExpiredLicense_FreshUser_StillGetsExpired()
    {
        // No trial.json — fresh install. But the license is already expired.
        WriteLicenseFile(new LicensePayload
        {
            Department = "Test U",
            Issued = "2026-09-01",
            Expiry = "2027-09-01",
            LicenseVersion = 1
        });

        // Clock is past expiry — license is expired.
        // Trial was just seeded (fresh), so it would be active,
        // but the expired license takes priority.
        var result = CreateManager(ClockAt(2027, 10, 1)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Expired, result.Reason);
    }

    // ── Test 9: Brand new user, no files ─────────────────────────────────────

    [Fact]
    public void Test09_BrandNewUser_NoFiles_FreshTrial()
    {
        // Identical to Test 1 — confirms no leftover state affects result.
        var result = CreateManager(ClockAt(2026, 9, 1)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(30, result.DaysRemaining);
    }

    // ── Test 10: Corrupt license file, trial active ─────────────────────────

    [Fact]
    public void Test10_CorruptLicenseFile_TrialActive_FullAccess()
    {
        File.WriteAllText(LicFilePath, "this is not a valid license file");
        SeedTrialFile(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = CreateManager(ClockAt(2026, 9, 10)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(21, result.DaysRemaining);
    }

    // ── Test 11: Staff transition (second user, existing license) ────────────

    [Fact]
    public void Test11_StaffTransition_LicensePresent_NoTrialFile_Immediate()
    {
        // Jason installs fresh, points at Nancy's share which already has a license.
        WriteLicenseFile(new LicensePayload
        {
            Department = "Geography Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        });

        var result = CreateManager(ClockAt(2026, 10, 15)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Licensed, result.Reason);
        Assert.Equal("Geography Dept", result.DepartmentName);
    }

    // ── Test 12: License renewal ─────────────────────────────────────────────

    [Fact]
    public void Test12_LicenseRenewal_RestoresFullAccess()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        // First evaluation: expired license → ReadOnly.
        WriteLicenseFile(new LicensePayload
        {
            Department = "History Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        });
        var expired = CreateManager(ClockAt(2027, 8, 1)).EvaluateAccess(_shareDir);
        Assert.Equal(AccessLevel.ReadOnly, expired.AccessLevel);
        Assert.Equal(AccessReason.Expired, expired.Reason);

        // Replace with a renewed license.
        WriteLicenseFile(new LicensePayload
        {
            Department = "History Dept Renewed",
            Issued = "2027-08-01",
            Expiry = "2028-08-01",
            LicenseVersion = 1
        });
        var renewed = CreateManager(ClockAt(2027, 8, 1)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, renewed.AccessLevel);
        Assert.Equal(AccessReason.Licensed, renewed.Reason);
        Assert.Equal("History Dept Renewed", renewed.DepartmentName);
    }

    // ── Additional: License deleted after long use → no fresh trial ─────────

    [Fact]
    public void Test_LicenseDeletedAfterLongUse_TrialAlreadyExpired_ReadOnly()
    {
        // User had a license from day 1. Trial was seeded on first launch.
        SeedTrialFile(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteLicenseFile(new LicensePayload
        {
            Department = "Sociology Dept",
            Issued = "2026-09-01",
            Expiry = "2027-09-01",
            LicenseVersion = 1
        });

        // Six months later, someone deletes the license file.
        DeleteLicenseFile();

        // Trial started 6 months ago — long expired. No fresh trial.
        var result = CreateManager(ClockAt(2027, 3, 1)).EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Unlicensed, result.Reason);
    }

    // ── Test clock ───────────────────────────────────────────────────────────

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    }
}
