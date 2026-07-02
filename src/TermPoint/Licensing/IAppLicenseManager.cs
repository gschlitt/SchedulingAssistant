namespace TermPoint.Licensing;

/// <summary>
/// Orchestrates license validation and trial evaluation into a single
/// <see cref="AppAccessResult"/> that the UI layer consumes.
/// </summary>
public interface IAppLicenseManager
{
    /// <summary>
    /// Evaluates the combined license + trial state for the given share directory.
    /// </summary>
    /// <param name="shareDirectoryPath">Directory containing the shared database and <c>termpoint.lic</c>.</param>
    /// <returns>The composite access result.</returns>
    AppAccessResult EvaluateAccess(string shareDirectoryPath);
}
