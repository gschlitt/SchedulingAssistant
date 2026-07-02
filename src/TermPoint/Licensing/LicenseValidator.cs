using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TermPoint.Licensing;

/// <summary>
/// Validates a <c>termpoint.lic</c> file by verifying its RSA-SHA256 signature
/// against an embedded public key, then checking the payload for expiry.
/// No network calls, no UI dependencies — pure local crypto.
/// </summary>
public class LicenseValidator : ILicenseValidator
{
    private const string LicenseFileName = "termpoint.lic";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _publicKeyPem;
    private readonly IClock _clock;

    /// <summary>
    /// Creates a new validator.
    /// </summary>
    /// <param name="publicKeyPem">RSA public key in PEM format, used to verify signatures.</param>
    /// <param name="clock">Clock for expiry checks. Use <see cref="SystemClock"/> in production.</param>
    public LicenseValidator(string publicKeyPem, IClock clock)
    {
        _publicKeyPem = publicKeyPem;
        _clock = clock;
    }

    /// <inheritdoc />
    public LicenseResult ValidateLicenseFile(string shareDirectoryPath)
    {
        var filePath = Path.Combine(shareDirectoryPath, LicenseFileName);

        if (!File.Exists(filePath))
            return new LicenseResult { State = LicenseState.NotFound };

        string fileContent;
        try
        {
            fileContent = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = $"Could not read license file: {ex.Message}"
            };
        }

        return ValidateContent(fileContent);
    }

    /// <summary>
    /// Parses the two-section .lic format, verifies the signature, and checks expiry.
    /// </summary>
    private LicenseResult ValidateContent(string fileContent)
    {
        // Strip comment lines (human-readable header) before parsing.
        // Normalize line endings first — files may have \r\n (Windows) or \n (Unix).
        var normalized = fileContent.Replace("\r\n", "\n");
        var lines = normalized.Split('\n')
            .Where(line => !line.StartsWith('#'));
        var body = string.Join('\n', lines).Trim();
        var parts = body.Split("\n\n", 2, StringSplitOptions.None);
        if (parts.Length < 2)
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = "License file does not contain payload and signature sections"
            };

        var payloadBase64 = parts[0].Trim();
        var signatureBase64 = parts[1].Trim();

        if (string.IsNullOrEmpty(payloadBase64) || string.IsNullOrEmpty(signatureBase64))
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = "License file has empty payload or signature"
            };

        // Decode base64
        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(payloadBase64);
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException ex)
        {
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = $"Invalid base64 in license file: {ex.Message}"
            };
        }

        // Verify RSA-SHA256 signature
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(_publicKeyPem);
        }
        catch (Exception ex)
        {
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = $"Failed to load public key: {ex.Message}"
            };
        }

        bool signatureValid = rsa.VerifyData(
            payloadBytes, signatureBytes,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (!signatureValid)
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = "Signature verification failed"
            };

        // Parse the JSON payload
        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = $"Invalid JSON in license payload: {ex.Message}"
            };
        }

        if (payload == null)
            return new LicenseResult
            {
                State = LicenseState.Invalid,
                ErrorReason = "License payload deserialized to null"
            };

        // Check expiry
        if (payload.Expiry != null)
        {
            if (!DateOnly.TryParse(payload.Expiry, out var expiryDate))
                return new LicenseResult
                {
                    State = LicenseState.Invalid,
                    ErrorReason = $"Invalid expiry date format: {payload.Expiry}"
                };

            var today = DateOnly.FromDateTime(_clock.UtcNow);

            if (expiryDate < today)
                return new LicenseResult
                {
                    State = LicenseState.Expired,
                    Department = payload.Department,
                    Expiry = expiryDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                };

            return new LicenseResult
            {
                State = LicenseState.Licensed,
                Department = payload.Department,
                Expiry = expiryDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            };
        }

        // Null expiry = permanent license
        return new LicenseResult
        {
            State = LicenseState.Licensed,
            Department = payload.Department,
            Expiry = null
        };
    }
}
