namespace SchedulingAssistant.Services;

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
    /// True when the platform supports saving to and auto-saving the database file.
    /// False in the browser demo — the demo database is in-memory only.
    /// </summary>
    public static bool SupportsSave => false;
#else
    /// <inheritdoc cref="SupportsFileDialogs"/>
    public static bool SupportsFileDialogs => true;

    /// <inheritdoc cref="SupportsSemesterMutations"/>
    public static bool SupportsSemesterMutations => true;

    /// <inheritdoc cref="SupportsSave"/>
    public static bool SupportsSave => true;
#endif
}
