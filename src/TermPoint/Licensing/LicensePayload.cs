namespace TermPoint.Licensing;

/// <summary>
/// The signed payload inside a <c>termpoint.lic</c> key file.
/// Serialized as JSON, then base64-encoded and RSA-signed.
/// </summary>
public class LicensePayload
{
    /// <summary>
    /// Display name of the licensed department (e.g. "UBC Geography").
    /// Shown in the app UI as "Licensed to: ...".
    /// </summary>
    public string Department { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 date when the key was generated (e.g. "2026-07-01").
    /// </summary>
    public string Issued { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 date when the license expires (e.g. "2027-07-01"),
    /// or null for a permanent / never-expires license.
    /// </summary>
    public string? Expiry { get; set; }

    /// <summary>
    /// Schema version of this payload format. Starts at 1.
    /// Named "LicenseVersion" to distinguish from the app version.
    /// </summary>
    public int LicenseVersion { get; set; } = 1;
}
