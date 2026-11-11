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
    NotFound,

    /// <summary>
    /// The license file could not be read for a reason other than "not found" (network error, sharing
    /// violation, access denied). Says nothing about whether a license exists, so callers may fall back
    /// to a remembered copy instead of treating it as unlicensed.
    /// </summary>
    Unreadable
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

    /// <summary>Institution name from the payload, if the signature was valid. Null for older licenses.</summary>
    public string? Institution { get; init; }

    /// <summary>Expiry date from the payload, if the signature was valid. Null for permanent licenses.</summary>
    public DateTime? Expiry { get; init; }

    /// <summary>Reason for failure, suitable for logging (not user display).</summary>
    public string? ErrorReason { get; init; }

    /// <summary>
    /// The license text as read from the file. Set for <see cref="LicenseState.Licensed"/> and
    /// <see cref="LicenseState.Expired"/> results produced from a file, so it can be remembered.
    /// </summary>
    public string? RawContent { get; init; }

    /// <summary>Returns a copy of this result carrying the given file text in <see cref="RawContent"/>.</summary>
    /// <param name="rawContent">The license file text.</param>
    /// <returns>The copy.</returns>
    public LicenseResult WithRawContent(string rawContent) => new()
    {
        State = State,
        Department = Department,
        Institution = Institution,
        Expiry = Expiry,
        ErrorReason = ErrorReason,
        RawContent = rawContent
    };
}
