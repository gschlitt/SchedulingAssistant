namespace TermPoint.Licensing;

/// <summary>
/// Outcome of license file validation.
/// </summary>
public enum LicenseState
{
    /// <summary>Valid, non-expired license.</summary>
    Licensed,

    /// <summary>Valid license but the expiry date has passed.</summary>
    Expired,

    /// <summary>File exists but the signature is invalid or the payload is malformed.</summary>
    Invalid,

    /// <summary>No license file found at the expected location.</summary>
    NotFound
}

/// <summary>
/// Result of validating a <c>termpoint.lic</c> file.
/// Returned by <see cref="ILicenseValidator.ValidateLicenseFile"/>.
/// </summary>
public class LicenseResult
{
    /// <summary>The outcome of validation.</summary>
    public LicenseState State { get; init; }

    /// <summary>Department name from the payload, if the signature was valid.</summary>
    public string? Department { get; init; }

    /// <summary>Expiry date from the payload, if the signature was valid. Null for permanent licenses.</summary>
    public DateTime? Expiry { get; init; }

    /// <summary>Reason for failure, suitable for logging (not user display).</summary>
    public string? ErrorReason { get; init; }
}
