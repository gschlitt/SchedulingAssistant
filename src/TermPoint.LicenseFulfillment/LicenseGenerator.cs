using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TermPoint.LicenseFulfillment;

/// <summary>
/// Generates signed TermPoint license files. Ported from <c>TermPoint.LicenseGen</c>
/// for use in the Azure Function fulfillment pipeline.
/// </summary>
public static class LicenseGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Generates a signed <c>termpoint.lic</c> file as a string.
    /// </summary>
    /// <param name="department">Department name to embed in the license.</param>
    /// <param name="institution">Institution name to embed in the license, or null if not provided.</param>
    /// <param name="expiryYears">Number of years from today until the license expires.</param>
    /// <param name="privateKeyPem">RSA private key in PEM format.</param>
    /// <returns>The complete license file content (human-readable header + base64 payload + signature).</returns>
    public static string Generate(string department, string? institution, int expiryYears, string privateKeyPem)
    {
        var payload = new LicensePayload
        {
            Department = department,
            Institution = institution,
            Issued = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            Expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(expiryYears)).ToString("yyyy-MM-dd"),
            LicenseVersion = 1
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var content = new StringBuilder();
        content.AppendLine("# TermPoint License");
        content.AppendLine($"# Department: {payload.Department}");
        if (!string.IsNullOrWhiteSpace(payload.Institution))
            content.AppendLine($"# Institution: {payload.Institution}");
        content.AppendLine($"# Issued:     {payload.Issued}");
        content.AppendLine($"# Expires:    {payload.Expiry ?? "Never"}");
        content.AppendLine("#");
        content.AppendLine("# Do not edit below this line.");
        content.AppendLine();
        content.AppendLine(Convert.ToBase64String(payloadBytes));
        content.AppendLine();
        content.AppendLine(Convert.ToBase64String(signature));

        return content.ToString();
    }
}
