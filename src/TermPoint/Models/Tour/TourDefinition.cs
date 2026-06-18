namespace TermPoint.Models.Tour;

/// <summary>
/// A named, ordered sequence of <see cref="TourSegment"/> references forming a
/// complete walkthrough experience. Immutable after construction.
/// </summary>
/// <remarks>
/// Named <c>TourDefinition</c> (not <c>Tour</c>) to avoid ambiguity with the
/// <c>TermPoint.Models.Tour</c> namespace.
/// Post-wizard orientation, WASM demo, and feature spotlights are all just
/// different <c>TourDefinition</c> values referencing overlapping sets of segments.
/// </remarks>
public class TourDefinition
{
    /// <summary>Unique key across all tours (e.g. <c>"post-wizard"</c>).</summary>
    public string Key { get; }

    /// <summary>Display name for the Help menu and progress UI (e.g. "Getting Started Tour").</summary>
    public string Title { get; }

    /// <summary>One-sentence summary of what the tour covers.</summary>
    public string Description { get; }

    /// <summary>
    /// Ordered list of <see cref="TourSegment"/> keys.
    /// List index determines the order segments are presented.
    /// </summary>
    public IReadOnlyList<string> SegmentKeys { get; }

    /// <summary>
    /// Optional auto-trigger rule. Null means on-demand only (launched from Help menu).
    /// </summary>
    public TourTriggerRule? AutoTrigger { get; }

    /// <summary>
    /// Whether this tour appears in Help > Take a Tour for replay. Default true.
    /// </summary>
    public bool IsReplayable { get; }

    /// <summary>
    /// Creates an immutable tour definition.
    /// </summary>
    /// <param name="key">Unique tour key. Must not be empty.</param>
    /// <param name="title">Display name.</param>
    /// <param name="description">One-sentence summary.</param>
    /// <param name="segmentKeys">Ordered segment keys. Must contain at least one entry.</param>
    /// <param name="autoTrigger">Auto-trigger rule, or null for on-demand only.</param>
    /// <param name="isReplayable">Whether the tour appears in the Help menu.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is empty or <paramref name="segmentKeys"/> is empty.
    /// </exception>
    public TourDefinition(
        string key,
        string title,
        string description,
        IEnumerable<string> segmentKeys,
        TourTriggerRule? autoTrigger = null,
        bool isReplayable = true)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Tour key must not be empty.", nameof(key));

        var keys = segmentKeys.ToList().AsReadOnly();
        if (keys.Count == 0)
            throw new ArgumentException("A tour must contain at least one segment key.", nameof(segmentKeys));

        Key = key;
        Title = title;
        Description = description;
        SegmentKeys = keys;
        AutoTrigger = autoTrigger;
        IsReplayable = isReplayable;
    }
}
