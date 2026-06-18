namespace TermPoint.Models.Tour;

/// <summary>
/// A named, ordered group of <see cref="TourStep"/> keys that belong together thematically.
/// The unit of reuse across tours: different tours cherry-pick different segments.
/// Immutable after construction.
/// </summary>
/// <remarks>
/// Examples: "Layout Orientation" (section panel, schedule grid, workload panel),
/// "Using Filters" (filter bar, a dropdown, AND/OR toggle, clear button).
/// Segments are the natural grain for tour descriptions.
/// </remarks>
public class TourSegment
{
    /// <summary>
    /// Unique key across the segment catalog (e.g. <c>"layout-orientation"</c>).
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Human-readable name for progress indicators and the tour chooser UI
    /// (e.g. "Layout Orientation").
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Ordered list of <see cref="TourStep"/> keys that form this segment's content.
    /// List index determines display order.
    /// </summary>
    public IReadOnlyList<string> StepKeys { get; }

    /// <summary>
    /// Creates an immutable tour segment.
    /// </summary>
    /// <param name="key">Unique segment key. Must not be empty.</param>
    /// <param name="title">Display name for progress indicators.</param>
    /// <param name="stepKeys">Ordered step keys. Must contain at least one entry.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is empty or <paramref name="stepKeys"/> is empty.
    /// </exception>
    public TourSegment(string key, string title, IEnumerable<string> stepKeys)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Segment key must not be empty.", nameof(key));

        var keys = stepKeys.ToList().AsReadOnly();
        if (keys.Count == 0)
            throw new ArgumentException("A segment must contain at least one step key.", nameof(stepKeys));

        Key = key;
        Title = title;
        StepKeys = keys;
    }
}
