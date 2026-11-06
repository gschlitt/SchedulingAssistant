namespace TermPoint.Services;

/// <summary>
/// Opens Help articles, the HTML files shipped in the application's <c>Help/</c> folder, in the user's browser.
/// One place for the rule that turns an article name into something the platform can open, shared by the Help page
/// (<c>HelpViewModel</c>), the hover tips (<c>HelpTip</c>) and the "?" markers (<c>HelpMarker</c>).
/// </summary>
public static class HelpArticles
{
    /// <summary>The folder the Help articles are copied to next to the app (desktop builds).</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Help");

    /// <summary>
    /// Opens a Help article, or an external page.
    /// </summary>
    /// <param name="target">
    /// A bare file name in the Help folder (e.g. <c>"schedule-grid-filters.html"</c>): opened from the absolute path
    /// on desktop, or as the relative URL <c>Help/&lt;name&gt;</c> on WASM, where the Help files are web assets.
    /// Anything starting with "http" is treated as an external URL and opened as it is.
    /// </param>
    public static void Open(string target)
    {
        if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            PlatformProcess.OpenUri(target);
            return;
        }

#if BROWSER
        PlatformProcess.OpenLocalFile("Help/" + target);
#else
        PlatformProcess.OpenLocalFile(Path.Combine(Directory, target));
#endif
    }
}
