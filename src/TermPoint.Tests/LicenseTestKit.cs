using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TermPoint.Licensing;

namespace TermPoint.Tests;

/// <summary>
/// Shared helpers for the 1.2.4 licence-check tests: a settable clock, a throw-away RSA key that signs
/// <c>termpoint.lic</c> text, and unique temp folders.
/// </summary>
internal sealed class LicenseTestKit : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly List<string> _tempDirs = new();

    /// <summary>The public key matching the signatures this kit produces.</summary>
    public string PublicKeyPem => _rsa.ExportRSAPublicKeyPem();

    /// <summary>
    /// Creates (and remembers for cleanup) a fresh temp directory.
    /// </summary>
    /// <param name="label">A short name included in the folder name.</param>
    /// <returns>The directory path.</returns>
    public string NewDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lickit_{label}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Builds correctly signed <c>.lic</c> file text.
    /// </summary>
    /// <param name="expiry">ISO date the licence expires, or null for a permanent licence.</param>
    /// <param name="department">Department name in the payload.</param>
    /// <returns>The file text.</returns>
    public string SignedLicenseText(string? expiry, string department = "Geography")
    {
        var payload = new LicensePayload { Department = department, Institution = "Test U", Issued = "2026-01-01", Expiry = expiry };
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        var signature = _rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sb = new StringBuilder();
        sb.AppendLine("# TermPoint License");
        sb.AppendLine("#");
        sb.AppendLine();
        sb.AppendLine(Convert.ToBase64String(payloadBytes));
        sb.AppendLine();
        sb.AppendLine(Convert.ToBase64String(signature));
        return sb.ToString();
    }

    /// <summary>Writes signed licence text to <c>termpoint.lic</c> in a folder.</summary>
    /// <param name="folder">The share folder.</param>
    /// <param name="text">The licence text.</param>
    public static void WriteLicense(string folder, string text)
        => File.WriteAllText(Path.Combine(folder, "termpoint.lic"), text);

    /// <summary>Builds the validator + trial + cache + manager stack over the given folders and clock.</summary>
    /// <param name="appData">The AppData folder (trial.json, license-cache.json).</param>
    /// <param name="clock">The clock.</param>
    /// <param name="cache">Receives the cache the manager uses.</param>
    /// <returns>The manager.</returns>
    public AppLicenseManager Manager(string appData, IClock clock, out LicenseCache cache)
    {
        cache = new LicenseCache(appData);
        return new AppLicenseManager(
            new LicenseValidator(PublicKeyPem, clock), new TrialService(appData, clock), cache);
    }

    /// <summary>Deletes the temp folders and releases the key.</summary>
    public void Dispose()
    {
        _rsa.Dispose();
        foreach (var d in _tempDirs)
            try { Directory.Delete(d, recursive: true); } catch { }
    }
}

/// <summary>A clock whose instant can be moved by a test; <see cref="IClock.Today"/> is the date of that instant.</summary>
internal sealed class TestClock : IClock
{
    /// <summary>Creates a clock at a UTC instant.</summary>
    /// <param name="utcNow">The instant.</param>
    public TestClock(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    /// <summary>The current instant (settable).</summary>
    public DateTime UtcNow { get; set; }

    /// <summary>The calendar date of <see cref="UtcNow"/>.</summary>
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}
