namespace TermPoint.Services;

/// <summary>
/// Compile-time feature flags for platform-specific capabilities.
/// All properties are compile-time constants — <c>false</c> in the WASM browser demo
/// (where there is no file system, no native process spawning, and no desktop window),
/// <c>true</c> on all desktop targets.
///
/// ViewModels check these before invoking a feature and surface an inline message when
/// the capability is unavailable, instead of silently failing or throwing.
/// The <c>#if BROWSER</c> switch is confined to this single file.
/// </summary>
public static class PlatformCapabilities
{
#if BROWSER
    /// <summary>
    /// True when the platform supports native file save/open dialogs (StorageProvider).
    /// False in the browser demo — there is no file system to write to.
    /// </summary>
    public static bool SupportsFileDialogs => false;

    /// <summary>
    /// True when the platform supports semester mutations (add, rename, delete, copy, empty).
    /// False in the browser demo — the demo database is read-only.
    /// </summary>
    public static bool SupportsSemesterMutations => false;

    /// <summary>
    /// True when the user may switch the active semester via the picker.
    /// False in the browser demo: the demo loads a single busy semester at startup, and
    /// re-selecting it would re-run the (WASM-slow) full section-list realization. Disabling
    /// the picker keeps that one-time cost hidden behind the splash and prevents an in-session
    /// rebuild lag that does not occur on desktop.
    /// </summary>
    public static bool SupportsSemesterSwitching => false;

    /// <summary>
    /// True when the platform supports saving to and auto-saving the database file.
    /// False in the browser demo — the demo database is in-memory only.
    /// </summary>
    public static bool SupportsSave => false;

    /// <summary>
    /// Extra pixels added per entry when measuring co-scheduled tile height.
    /// WASM renders on-tree TextBlocks taller than off-tree Measure() predicts.
    /// </summary>
    public static int TileHeightMarginPerEntry => 2;

    /// <summary>
    /// True when the platform supports spawning detached top-level windows
    /// (e.g. floating "sticky note" copies of workflow cards).
    /// False in the browser demo — WASM is single-window.
    /// </summary>
    public static bool SupportsDetachedWindows => false;

    /// <summary>
    /// True when the platform can open external URLs.
    /// On WASM, opens in a new browser tab via <c>window.open</c>.
    /// </summary>
    public static bool SupportsLinks => true;

    /// <summary>
    /// True on desktop targets, false in the browser demo.
    /// Use for UI features that are desktop-only but don't map to a specific capability
    /// (e.g. hiding a color-picker column that would be impractical in WASM).
    /// </summary>
    public static bool IsDesktop => false;
#else
    /// <inheritdoc cref="SupportsFileDialogs"/>
    public static bool SupportsFileDialogs => true;

    /// <inheritdoc cref="SupportsSemesterMutations"/>
    public static bool SupportsSemesterMutations => true;

    /// <inheritdoc cref="SupportsSemesterSwitching"/>
    public static bool SupportsSemesterSwitching => true;

    /// <inheritdoc cref="SupportsSave"/>
    public static bool SupportsSave => true;

    /// <inheritdoc cref="TileHeightMarginPerEntry"/>
    public static int TileHeightMarginPerEntry => 0;

    /// <inheritdoc cref="SupportsDetachedWindows"/>
    public static bool SupportsDetachedWindows => true;

    /// <inheritdoc cref="SupportsLinks"/>
    public static bool SupportsLinks => true;

    /// <inheritdoc cref="IsDesktop"/>
    public static bool IsDesktop => true;
#endif

    /// <summary>
    /// True when running as an installed MSIX package (e.g. from the Microsoft Store).
    /// Velopack update checks are skipped in this case — the Store handles updates.
    /// </summary>
    public static bool IsMsixPackage { get; } = DetectMsixPackage();

    private static bool DetectMsixPackage()
    {
#if BROWSER
        return false;
#else
        try
        {
            int length = 0;
            // ERROR_INSUFFICIENT_BUFFER (122) means we're in a package context.
            // APPMODEL_ERROR_NO_PACKAGE (15700) means we're not.
            return GetCurrentPackageFullName(ref length, null) != 15700;
        }
        catch
        {
            return false;
        }
#endif
    }

#if !BROWSER
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
#endif
}
