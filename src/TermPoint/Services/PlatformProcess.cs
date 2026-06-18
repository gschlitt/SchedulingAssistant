using System.Diagnostics;
using System.Runtime.InteropServices;
#if BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

namespace TermPoint.Services;

/// <summary>
/// Cross-platform helpers for launching URLs, URIs, and executables
/// via the OS default handler.
/// </summary>
public static partial class PlatformProcess
{
#if BROWSER
    /// <summary>
    /// JS interop: calls <c>window.open(url, '_blank')</c> to open a URL in a new browser tab.
    /// </summary>
    [JSImport("globalThis.window.open")]
    private static partial void WindowOpen(string url, string target);
#endif

    /// <summary>
    /// Opens a URL or URI (http, mailto, etc.) in the platform's default handler.
    /// On WASM, opens in a new browser tab via <c>window.open</c>.
    /// </summary>
    /// <param name="uri">The URL or URI to open.</param>
    public static void OpenUri(string uri)
    {
#if BROWSER
        WindowOpen(uri, "_blank");
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", uri);
        else
            Process.Start("xdg-open", uri);
#endif
    }

    /// <summary>
    /// Opens a local file using the OS default application for its type.
    /// On desktop, launches via the OS shell. On WASM, opens a relative URL
    /// in a new browser tab (the file must be deployed as a web asset).
    /// </summary>
    /// <param name="path">
    /// Desktop: absolute file-system path. WASM: relative URL path (e.g. <c>"Help/navigating.html"</c>).
    /// </param>
    public static void OpenLocalFile(string path)
    {
#if BROWSER
        WindowOpen(path, "_blank");
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", path);
        else
            Process.Start("xdg-open", path);
#endif
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
