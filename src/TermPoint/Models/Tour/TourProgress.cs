namespace TermPoint.Models.Tour;

/// <summary>
/// Tracks the user's progress through a running or completed tour.
/// The only mutable entity in the tour model — steps, segments, and tours
/// are all immutable definitions.
/// </summary>
/// <remarks>
/// In-memory only. Only the terminal state (completed/dismissed) is persisted
/// via <c>AppSettings.CompletedTourKeys</c>. No partial-progress persistence —
/// tours are short (under 2 minutes); if dismissed, they start from the beginning
/// on replay.
/// </remarks>
public class TourProgress
{
    /// <summary>Key of the tour being tracked.</summary>
    public string TourKey { get; }

    /// <summary>Current lifecycle state.</summary>
    public TourStatus Status { get; set; }

    /// <summary>
    /// Zero-based index into the tour's segment list.
    /// Meaningful only when <see cref="Status"/> is <see cref="TourStatus.InProgress"/>.
    /// </summary>
    public int CurrentSegmentIndex { get; set; }

    /// <summary>
    /// Zero-based index into the current segment's step list.
    /// Meaningful only when <see cref="Status"/> is <see cref="TourStatus.InProgress"/>.
    /// </summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>
    /// Timestamp when the tour was completed or dismissed. Null while
    /// <see cref="TourStatus.NotStarted"/> or <see cref="TourStatus.InProgress"/>.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    private TourProgress(string tourKey, TourStatus status)
    {
        TourKey = tourKey;
        Status = status;
    }

    /// <summary>
    /// Creates a new in-progress tour progress record at the first step.
    /// </summary>
    /// <param name="tourKey">The tour being started.</param>
    /// <returns>A fresh <see cref="TourProgress"/> with Status = InProgress and indices at 0.</returns>
    public static TourProgress Start(string tourKey)
    {
        return new TourProgress(tourKey, TourStatus.InProgress)
        {
            CurrentSegmentIndex = 0,
            CurrentStepIndex = 0
        };
    }
}
