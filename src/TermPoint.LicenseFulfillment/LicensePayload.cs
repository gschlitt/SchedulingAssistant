namespace TermPoint.LicenseFulfillment;

/// <summary>
/// Local copy of the license payload — mirrors TermPoint.Licensing.LicensePayload.
/// Duplicated here so the function compiles without referencing the full TermPoint project.
/// </summary>
public class LicensePayload
{
    public string Department { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string Issued { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public int LicenseVersion { get; set; } = 1;
}
