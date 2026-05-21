namespace SchedulingAssistant.Models.Tour;

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
    /// Auto-start on every main-window load when the tour key is not
    /// in <c>CompletedTourKeys</c>. On WASM (where settings reset each
    /// session) this effectively fires every visit; on desktop it fires
    /// once and completion suppresses it.
    /// </summary>
    EverySession
}
