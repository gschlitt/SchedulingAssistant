namespace TermPoint.Models.Tour;

/// <summary>
/// AXAML-instantiable content class for a tour segment. Converted to an immutable
/// <see cref="TourSegment"/> by <see cref="Services.TourCatalog"/> at startup.
/// </summary>
public class TourSegmentData
{
    /// <summary>Human-readable name for progress indicators (e.g. "Layout Orientation").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of <see cref="TourStep"/> keys that form this segment's content
    /// (e.g. <c>"layout.section-panel,layout.schedule-grid,layout.workload-panel"</c>).
    /// Parsed to an ordered list during catalog initialization.
    /// </summary>
    public string StepKeys { get; set; } = string.Empty;
}
