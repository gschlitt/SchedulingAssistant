using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TermPoint.Licensing;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests for <see cref="IAppLicenseManager"/> — the composite orchestrator
/// that combines license validation and trial status into a single
/// <see cref="AppAccessResult"/>.
///
/// <para>Tests are organised by the four possible outcomes:
/// <list type="bullet">
///   <item><description>Group 1 — Licensed (valid license → FullAccess)</description></item>
///   <item><description>Group 2 — Trial (no license, trial active → FullAccess)</description></item>
///   <item><description>Group 3 — Expired (license expired → ReadOnly)</description></item>
///   <item><description>Group 4 — Unlicensed (no license, trial expired → ReadOnly)</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class AppLicenseManagerTests : IDisposable
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

    public AppLicenseManagerTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _shareDir = Path.Combine(Path.GetTempPath(), $"lic_mgr_share_{id}");
        _appDataDir = Path.Combine(Path.GetTempPath(), $"lic_mgr_appdata_{id}");
        Directory.CreateDirectory(_shareDir);
        Directory.CreateDirectory(_appDataDir);

        _rsa = RSA.Create(2048);
        _publicKeyPem = _rsa.ExportRSAPublicKeyPem();
    }

    public void Dispose()
    {
        _rsa.Dispose();
        try { Directory.Delete(_shareDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_appDataDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private AppLicenseManager CreateManager(IClock clock)
    {
        var validator = new LicenseValidator(_publicKeyPem, clock);
        var trial = new TrialService(_appDataDir, clock);
        return new AppLicenseManager(validator, trial);
    }

    private static IClock ClockAt(int year, int month, int day)
    {
        return new FixedClock(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
    }

    private void WriteLicenseFile(LicensePayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var signature = _rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var content = new StringBuilder();
        content.AppendLine(Convert.ToBase64String(payloadBytes));
        content.AppendLine();
        content.AppendLine(Convert.ToBase64String(signature));

        File.WriteAllText(Path.Combine(_shareDir, "termpoint.lic"), content.ToString());
    }

    private void SeedTrialFile(DateTime trialStartedUtc)
    {
        var json = JsonSerializer.Serialize(new
        {
            trialStartedUtc = trialStartedUtc.ToString("o"),
            version = 1
        });
        File.WriteAllText(Path.Combine(_appDataDir, "trial.json"), json);
    }

    // ── Group 1: Licensed ────────────────────────────────────────────────────

    [Fact]
    public void Licensed_ValidLicense_FullAccess()
    {
        WriteLicenseFile(new LicensePayload
        {
            Department = "Psychology Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        });

        var manager = CreateManager(ClockAt(2026, 12, 15));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Licensed, result.Reason);
        Assert.Equal("Psychology Dept", result.DepartmentName);
        Assert.NotNull(result.ExpiryDate);
        Assert.Null(result.DaysRemaining);
        Assert.False(result.ShowPurchasePrompt);
    }

    [Fact]
    public void Licensed_PermanentLicense_FullAccess_NoExpiry()
    {
        WriteLicenseFile(new LicensePayload
        {
            Department = "Early Adopter Dept",
            Issued = "2026-07-01",
            Expiry = null,
            LicenseVersion = 1
        });

        var manager = CreateManager(ClockAt(2099, 1, 1));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Licensed, result.Reason);
        Assert.Null(result.ExpiryDate);
        Assert.False(result.ShowPurchasePrompt);
    }

    [Fact]
    public void Licensed_IgnoresTrialState()
    {
        // Even if the trial is expired, a valid license means FullAccess
        SeedTrialFile(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteLicenseFile(new LicensePayload
        {
            Department = "Has License Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        });

        var manager = CreateManager(ClockAt(2026, 12, 15));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Licensed, result.Reason);
    }

    // ── Group 2: Trial ───────────────────────────────────────────────────────

    [Fact]
    public void Trial_NoLicense_FirstLaunch_FullAccess()
    {
        // No license file, no trial file → first launch starts trial
        var manager = CreateManager(ClockAt(2026, 7, 1));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(30, result.DaysRemaining);
        Assert.Null(result.DepartmentName);
        Assert.True(result.ShowPurchasePrompt);
    }

    [Fact]
    public void Trial_NoLicense_MidTrial_FullAccess()
    {
        SeedTrialFile(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        var manager = CreateManager(ClockAt(2026, 7, 16));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.FullAccess, result.AccessLevel);
        Assert.Equal(AccessReason.Trial, result.Reason);
        Assert.Equal(15, result.DaysRemaining);
        Assert.True(result.ShowPurchasePrompt);
    }

    // ── Group 3: Expired license ─────────────────────────────────────────────

    [Fact]
    public void Expired_LicensePastExpiry_ReadOnly()
    {
        WriteLicenseFile(new LicensePayload
        {
            Department = "Expired Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        });

        var manager = CreateManager(ClockAt(2027, 7, 2));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Expired, result.Reason);
        Assert.Equal("Expired Dept", result.DepartmentName);
        Assert.True(result.ShowPurchasePrompt);
    }

    // ── Group 4: Unlicensed ──────────────────────────────────────────────────

    [Fact]
    public void Unlicensed_NoLicense_TrialExpired_ReadOnly()
    {
        SeedTrialFile(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var manager = CreateManager(ClockAt(2026, 12, 31));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Unlicensed, result.Reason);
        Assert.Null(result.DepartmentName);
        Assert.True(result.ShowPurchasePrompt);
    }

    [Fact]
    public void Unlicensed_InvalidLicense_TrialExpired_ReadOnly()
    {
        // Garbage license file + expired trial = ReadOnly
        File.WriteAllText(Path.Combine(_shareDir, "termpoint.lic"), "garbage");
        SeedTrialFile(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var manager = CreateManager(ClockAt(2026, 7, 1));
        var result = manager.EvaluateAccess(_shareDir);

        Assert.Equal(AccessLevel.ReadOnly, result.AccessLevel);
        Assert.Equal(AccessReason.Unlicensed, result.Reason);
        Assert.True(result.ShowPurchasePrompt);
    }

    // ── Test clock ───────────────────────────────────────────────────────────

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    }
}
