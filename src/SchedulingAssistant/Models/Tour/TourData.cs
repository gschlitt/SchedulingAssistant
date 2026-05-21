namespace SchedulingAssistant.Models.Tour;

/// <summary>
/// AXAML-instantiable content class for a tour definition. Converted to an immutable
/// <see cref="TourDefinition"/> by <see cref="Services.TourCatalog"/> at startup.
/// </summary>
public class TourData
{
    /// <summary>Display name for the Help menu and progress UI.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>One-sentence summary of what the tour covers.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of <see cref="TourSegment"/> keys
    /// (e.g. <c>"layout-orientation,adding-a-section,using-filters"</c>).
    /// Parsed to an ordered list during catalog initialization.
    /// </summary>
    public string SegmentKeys { get; set; } = string.Empty;

    /// <summary>
    /// Auto-trigger rule as a string (e.g. <c>"PostWizardFirstLaunch"</c>, <c>"EverySession"</c>).
    /// Empty or null means on-demand only. Parsed to <see cref="TourTriggerRule"/>
    /// during catalog initialization.
    /// </summary>
    public string AutoTrigger { get; set; } = string.Empty;

    /// <summary>Whether this tour appears in Help > Take a Tour for replay.</summary>
    public bool IsReplayable { get; set; } = true;
}
