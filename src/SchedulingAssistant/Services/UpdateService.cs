using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace SchedulingAssistant.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and stages it for the next launch.
/// Updates are applied silently — no forced restart. The next time the user starts
/// the app, VelopackApp.Build().Run() picks up the staged update automatically.
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/gschlitt/SchedulingAssistant";

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
