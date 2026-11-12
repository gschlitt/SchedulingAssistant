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

#if !BROWSER
    /// <summary>
    /// Opens the platform's file manager at the folder holding <paramref name="filePath"/>, with the file selected
    /// where the platform supports it (Explorer's <c>/select</c>, Finder's <c>open -R</c>; elsewhere just the folder).
    /// </summary>
    /// <param name="filePath">Full path of a file that exists.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The file manager could not be started.</exception>
    public static void ShowInFolder(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Explorer parses its own command line and wants the quotes around the path only: /select,"C:\a b\c.zip".
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = false });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var psi = new ProcessStartInfo("open");
            psi.ArgumentList.Add("-R");
            psi.ArgumentList.Add(filePath);
            Process.Start(psi);
        }
        else
        {
            var psi = new ProcessStartInfo("xdg-open");
            psi.ArgumentList.Add(Path.GetDirectoryName(filePath) ?? filePath);
            Process.Start(psi);
        }
    }
#endif

    /// <summary>
    /// Launches an executable by path, as a NEW process. On macOS, uses <c>open -n</c> for .app bundles: without
    /// <c>-n</c>, <c>open</c> only brings an already-running copy forward, and the copy that relaunches is still
    /// running when it does so (spec 1.2.4 item 4.3).
    /// </summary>
    /// <param name="exePath">Full path to the executable.</param>
    /// <param name="args">Arguments for the new process, one per element (quoted by .NET as needed); none by default.</param>
    public static void LaunchExecutable(string exePath, params string[] args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // If the path points inside a .app bundle, open the bundle itself
            var appBundleRoot = FindAppBundle(exePath);
            var psi = appBundleRoot is not null ? new ProcessStartInfo("open") : new ProcessStartInfo(exePath);
            if (appBundleRoot is not null)
            {
                psi.ArgumentList.Add("-n");
                psi.ArgumentList.Add(appBundleRoot);
                if (args.Length > 0) psi.ArgumentList.Add("--args");
            }
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        else
        {
            var psi = new ProcessStartInfo(exePath);
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
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
