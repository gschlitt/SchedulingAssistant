using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace TermPoint.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and stages it for the next launch.
/// Updates are applied silently — no forced restart. The next time the user starts
/// the app, VelopackApp.Build().Run() picks up the staged update automatically.
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/gschlitt/TermPoint";

    /// <summary>
    /// True when this process was installed by Velopack (the downloadable Windows setup or a macOS
    /// build) rather than run from Visual Studio, a portable copy, or an MSIX package. Used by
    /// <see cref="PlatformCapabilities.InstallChannel"/> to recognise the
    /// <see cref="InstallChannel.DirectDownload"/> channel.
    ///
    /// <para><c>NoInlining</c> keeps the JIT from pulling this method — and with it
    /// <c>Velopack.dll</c> — into the caller. The assembly loads only if this method is actually
    /// called, which callers do only for non-MSIX installs, after <c>VelopackApp</c> startup
    /// has run in <c>Program.Main</c>.</para>
    /// </summary>
    /// <returns>True when Velopack reports an installed instance; false otherwise or on any failure.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsVelopackInstalled()
    {
        try
        {
            return new UpdateManager(new GithubSource(RepoUrl, null, false)).IsInstalled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks for a newer release on GitHub and downloads it in the background if found.
    /// Safe to call at startup — silently no-ops when not running from an installed instance.
    /// Exceptions are caught and logged; update failures are non-fatal.
    /// </summary>
    public async Task CheckForUpdatesAsync(IAppLogger? logger = null)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));

            if (!mgr.IsInstalled)
                return; // running from source or dev environment

            var update = await mgr.CheckForUpdatesAsync();
            if (update == null)
                return; // already up to date

            logger?.LogInfo($"Update available: {update.TargetFullRelease.Version}. Downloading in background.");
            await mgr.DownloadUpdatesAsync(update);
            logger?.LogInfo("Update staged. Will be applied on next launch.");
        }
        catch (Exception ex)
        {
            // Update failures must never crash the app.
            logger?.LogError(ex, "Background update check failed.");
        }
    }
}
