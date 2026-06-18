namespace TermPoint.Models.Tour;

/// <summary>
/// Describes when a tour should auto-start. Attached to a
/// <see cref="TourDefinition"/> via its <c>AutoTrigger</c> property.
/// A null <c>AutoTrigger</c> means on-demand only (no auto-start).
/// </summary>
public enum TourTriggerRule
{
    /// <summary>
    /// Auto-start on the first main-window load after wizard completion.
    /// Detected by: <c>AppSettings.IsInitialSetupComplete == true</c>
    /// AND tour key not in <c>CompletedTourKeys</c>.
    /// </summary>
    PostWizardFirstLaunch,

    /// <summary>
    /// Auto-start on every browser/WASM session. On desktop this trigger
    /// is suppressed (via <see cref="PlatformCapabilities"/>). On WASM,
    /// where settings reset each session, this fires every visit.
    /// </summary>
    EverySession,

    /// <summary>
    /// Auto-start before the startup wizard on first launch.
    /// Detected by: <c>AppSettings.IsInitialSetupComplete == false</c>
    /// AND tour key not in <c>CompletedTourKeys</c>.
    /// </summary>
    PreWizardFirstLaunch
}
