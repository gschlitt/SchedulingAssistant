using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TermPoint.Licensing;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Integration tests for <see cref="ILicenseValidator"/>.
///
/// <para>Each test generates a fresh RSA keypair, signs a payload, writes a
/// <c>termpoint.lic</c> file to an isolated temp directory, and validates it
/// through the real crypto path. No mocks — these exercise file I/O + signature
/// verification end-to-end.</para>
///
/// <para>Tests are organised into groups:
/// <list type="bullet">
///   <item><description>Group 1 — Valid license (expiry in future, null expiry)</description></item>
///   <item><description>Group 2 — Expired license</description></item>
///   <item><description>Group 3 — Missing license file</description></item>
///   <item><description>Group 4 — Corrupt / tampered files</description></item>
///   <item><description>Group 5 — Wrong key (signed with a different private key)</description></item>
///   <item><description>Group 6 — Forward compatibility (unknown version)</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class LicenseValidatorTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _shareDir;
    private readonly RSA _rsa;
    private readonly string _publicKeyPem;

    /// <summary>
    /// Creates an isolated temp directory and a fresh RSA keypair for each test.
    /// </summary>
    public LicenseValidatorTests()
    {
        _shareDir = Path.Combine(Path.GetTempPath(), $"lic_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_shareDir);

        _rsa = RSA.Create(2048);
        _publicKeyPem = _rsa.ExportRSAPublicKeyPem();
    }

    public void Dispose()
    {
        _rsa.Dispose();
        try { Directory.Delete(_shareDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a validator that trusts the test keypair and uses the given clock.
    /// </summary>
    private ILicenseValidator CreateValidator(IClock? clock = null)
    {
        return new LicenseValidator(_publicKeyPem, clock ?? new SystemClock());
    }

    /// <summary>
    /// Creates a clock fixed at the given date, for deterministic expiry testing.
    /// </summary>
    private static IClock ClockAt(int year, int month, int day)
    {
        return new FixedClock(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Signs a <see cref="LicensePayload"/> with the test private key and writes
    /// <c>termpoint.lic</c> to the share directory.
    /// </summary>
    private void WriteLicenseFile(LicensePayload payload)
    {
        WriteLicenseFileWith(payload, _rsa);
    }

    /// <summary>
    /// Signs a payload with an arbitrary RSA key and writes the .lic file.
    /// Used to test wrong-key scenarios.
    /// </summary>
    private void WriteLicenseFileWith(LicensePayload payload, RSA signingKey)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var signature = signingKey.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var content = new StringBuilder();
        content.AppendLine(Convert.ToBase64String(payloadBytes));
        content.AppendLine();
        content.AppendLine(Convert.ToBase64String(signature));

        File.WriteAllText(LicFilePath(), content.ToString());
    }

    /// <summary>Writes arbitrary text to the termpoint.lic file.</summary>
    private void WriteRawLicenseFile(string content)
    {
        File.WriteAllText(LicFilePath(), content);
    }

    private string LicFilePath() => Path.Combine(_shareDir, "termpoint.lic");

    // ── Group 1: Valid license ────────────────────────────────────────────────

    [Fact]
    public void ValidLicense_FutureExpiry_ReturnsLicensed()
    {
        var payload = new LicensePayload
        {
            Department = "Psychology Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        var validator = CreateValidator(ClockAt(2026, 12, 15));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Licensed, result.State);
        Assert.Equal("Psychology Dept", result.Department);
        Assert.NotNull(result.Expiry);
        Assert.Null(result.ErrorReason);
    }

    [Fact]
    public void ValidLicense_NullExpiry_ReturnsLicensed_Permanent()
    {
        var payload = new LicensePayload
        {
            Department = "Early Adopter Dept",
            Issued = "2026-07-01",
            Expiry = null,
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        var validator = CreateValidator(ClockAt(2099, 1, 1));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Licensed, result.State);
        Assert.Equal("Early Adopter Dept", result.Department);
        Assert.Null(result.Expiry);
    }

    [Fact]
    public void ValidLicense_ExpiryIsToday_ReturnsLicensed()
    {
        var payload = new LicensePayload
        {
            Department = "Edge Case Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        // Expiry date itself should still count as licensed (expires at end of day)
        var validator = CreateValidator(ClockAt(2027, 7, 1));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Licensed, result.State);
    }

    // ── Group 2: Expired license ─────────────────────────────────────────────

    [Fact]
    public void ExpiredLicense_PastExpiry_ReturnsExpired()
    {
        var payload = new LicensePayload
        {
            Department = "Expired Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        var validator = CreateValidator(ClockAt(2027, 7, 2));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Expired, result.State);
        Assert.Equal("Expired Dept", result.Department);
        Assert.NotNull(result.Expiry);
    }

    [Fact]
    public void ExpiredLicense_WellPastExpiry_ReturnsExpired()
    {
        var payload = new LicensePayload
        {
            Department = "Ancient Dept",
            Issued = "2020-01-01",
            Expiry = "2021-01-01",
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        var validator = CreateValidator(ClockAt(2026, 7, 1));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Expired, result.State);
    }

    // ── Group 3: Missing license file ────────────────────────────────────────

    [Fact]
    public void MissingFile_ReturnsNotFound()
    {
        // No file written — share dir is empty
        var validator = CreateValidator();
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.NotFound, result.State);
        Assert.Null(result.Department);
        Assert.Null(result.Expiry);
    }

    [Fact]
    public void NonexistentDirectory_ReturnsNotFound_NoCrash()
    {
        var validator = CreateValidator();
        var result = validator.ValidateLicenseFile(@"C:\nonexistent\path\that\does\not\exist");

        Assert.Equal(LicenseState.NotFound, result.State);
    }

    // ── Group 4: Corrupt / tampered files ────────────────────────────────────

    [Fact]
    public void CorruptFile_GarbageContent_ReturnsInvalid_NoCrash()
    {
        WriteRawLicenseFile("this is not a valid license file at all");

        var validator = CreateValidator();
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Invalid, result.State);
        Assert.NotNull(result.ErrorReason);
    }

    [Fact]
    public void CorruptFile_EmptyFile_ReturnsInvalid_NoCrash()
    {
        WriteRawLicenseFile("");

        var validator = CreateValidator();
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Invalid, result.State);
    }

    [Fact]
    public void CorruptFile_ValidBase64ButBadJson_ReturnsInvalid_NoCrash()
    {
        var notJson = Convert.ToBase64String(Encoding.UTF8.GetBytes("not json {{{"));
        var fakeSignature = Convert.ToBase64String(new byte[256]);

        WriteRawLicenseFile($"{notJson}\n\n{fakeSignature}");

        var validator = CreateValidator();
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Invalid, result.State);
    }

    [Fact]
    public void TamperedPayload_ModifiedAfterSigning_ReturnsInvalid()
    {
        // Sign a valid payload
        var payload = new LicensePayload
        {
            Department = "Original Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        // Read the file, replace the payload with a different one, keep the original signature
        var lines = File.ReadAllText(LicFilePath()).Split("\n", StringSplitOptions.None);
        var tamperedPayload = new LicensePayload
        {
            Department = "Tampered Dept",
            Issued = "2026-07-01",
            Expiry = "2099-12-31",
            LicenseVersion = 1
        };
        var tamperedJson = JsonSerializer.Serialize(tamperedPayload, JsonOptions);
        var tamperedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(tamperedJson));

        // Replace first line (payload) but keep the signature line
        lines[0] = tamperedBase64;
        File.WriteAllText(LicFilePath(), string.Join("\n", lines));

        var validator = CreateValidator(ClockAt(2026, 12, 15));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Invalid, result.State);
    }

    // ── Group 5: Wrong key ───────────────────────────────────────────────────

    [Fact]
    public void WrongKey_SignedWithDifferentPrivateKey_ReturnsInvalid()
    {
        using var wrongKey = RSA.Create(2048);

        var payload = new LicensePayload
        {
            Department = "Wrong Key Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        };
        WriteLicenseFileWith(payload, wrongKey);

        var validator = CreateValidator(ClockAt(2026, 12, 15));
        var result = validator.ValidateLicenseFile(_shareDir);

        Assert.Equal(LicenseState.Invalid, result.State);
    }

    // ── Group 6: Forward compatibility ───────────────────────────────────────

    [Fact]
    public void UnknownVersion_HigherThanExpected_StillLicensed()
    {
        var payload = new LicensePayload
        {
            Department = "Future Version Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 99
        };
        WriteLicenseFile(payload);

        var validator = CreateValidator(ClockAt(2026, 12, 15));
        var result = validator.ValidateLicenseFile(_shareDir);

        // Per spec: warn but do not reject
        Assert.Equal(LicenseState.Licensed, result.State);
        Assert.Equal("Future Version Dept", result.Department);
    }

    [Fact]
    public void ReadOnlyLicenseFile_ValidationStillWorks()
    {
        var payload = new LicensePayload
        {
            Department = "Read Only Dept",
            Issued = "2026-07-01",
            Expiry = "2027-07-01",
            LicenseVersion = 1
        };
        WriteLicenseFile(payload);

        // Make the file read-only
        File.SetAttributes(LicFilePath(), FileAttributes.ReadOnly);
        try
        {
            var validator = CreateValidator(ClockAt(2026, 12, 15));
            var result = validator.ValidateLicenseFile(_shareDir);

            Assert.Equal(LicenseState.Licensed, result.State);
        }
        finally
        {
            // Clean up so Dispose() can delete the directory
            File.SetAttributes(LicFilePath(), FileAttributes.Normal);
        }
    }

    // ── Test clock ───────────────────────────────────────────────────────────

    /// <summary>
    /// A clock frozen at a specific point in time, for deterministic testing
    /// of expiry and trial logic.
    /// </summary>
    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; }
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    }
}
