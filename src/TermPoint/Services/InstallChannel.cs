namespace TermPoint.Services;

/// <summary>
/// How this copy of TermPoint reached the user's machine. The channel decides two things:
/// <list type="bullet">
///   <item>what the "this database needs a newer TermPoint" banner tells the user to do
///         (see <see cref="MinimumVersionToWrite.BuildMessage"/>), and</item>
///   <item>whether the build is allowed to RAISE a database's <c>MinimumVersionToWrite</c>
///         stamp — only <see cref="Store"/> and <see cref="DirectDownload"/> builds may, so a
///         developer's or a sideloader's copy can never lock a department out of its database.</item>
/// </list>
/// Resolved once per process by <see cref="PlatformCapabilities.InstallChannel"/>.
/// </summary>
public enum InstallChannel
{
    /// <summary>
    /// An MSIX package installed from the Microsoft Store (package family name equals
    /// <see cref="PlatformCapabilities.StorePackageFamilyName"/>). Updates arrive through the Store.
    /// </summary>
    Store,

    /// <summary>
    /// Any other MSIX package — installed outside the Store (e.g. an .msix/.appinstaller file
    /// handed to a department). Updates come from wherever the user got the package.
    /// </summary>
    Sideload,

    /// <summary>
    /// Installed by the Velopack installer (the downloadable Windows setup, and the macOS builds).
    /// Updates are fetched automatically from GitHub Releases.
    /// </summary>
    DirectDownload,

    /// <summary>
    /// Anything else: run from Visual Studio, a portable/unpacked copy, a unit-test host, or the
    /// browser demo. Never stamps databases.
    /// </summary>
    Development,
}
