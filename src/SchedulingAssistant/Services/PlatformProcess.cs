using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SchedulingAssistant.Services;

/// <summary>
/// Cross-platform helpers for launching URLs, URIs, and executables
/// via the OS default handler.
/// </summary>
public static class PlatformProcess
{
    /// <summary>
    /// Opens a URL or URI (http, mailto, etc.) in the platform's default handler.
    /// </summary>
    /// <param name="uri">The URL or URI to open.</param>
    public static void OpenUri(string uri)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", uri);
        else
            Process.Start("xdg-open", uri);
    }

    /// <summary>
    /// Launches an executable by path. On macOS, uses <c>open</c> for .app bundles.
    /// </summary>
    /// <param name="exePath">Full path to the executable.</param>
    public static void LaunchExecutable(string exePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // If the path points inside a .app bundle, open the bundle itself
            var appBundleRoot = FindAppBundle(exePath);
            if (appBundleRoot is not null)
                Process.Start("open", appBundleRoot);
            else
                Process.Start(exePath);
        }
        else
        {
            Process.Start(exePath);
        }
    }

    /// <summary>
    /// Walks up the path looking for a .app bundle root directory.
    /// Returns null if the executable is not inside a bundle.
    /// </summary>
    private static string? FindAppBundle(string path)
    {
        var dir = Path.GetDirectoryName(path);
        while (dir is not null)
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
