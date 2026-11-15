namespace TermPoint.Licensing;

/// <summary>
/// Validates a <c>termpoint.lic</c> file using an embedded RSA public key.
/// Standalone service with no UI dependencies.
/// </summary>
public interface ILicenseValidator
{
    /// <summary>
    /// Reads and validates the license file at the given share directory path.
    /// </summary>
    /// <param name="shareDirectoryPath">
    /// Directory containing <c>termpoint.lic</c> (the same directory as the shared database).
    /// </param>
    /// <returns>A <see cref="LicenseResult"/> describing the license state.</returns>
    LicenseResult ValidateLicenseFile(string shareDirectoryPath);

    /// <summary>
    /// Validates licence text that has already been read (a file just read, or a remembered copy):
    /// signature, payload and expiry.
    /// </summary>
    /// <param name="fileContent">The full text of a <c>termpoint.lic</c> file.</param>
    /// <returns>A <see cref="LicenseResult"/> describing the license state.</returns>
    LicenseResult ValidateLicenseText(string fileContent);
}
