namespace TermPoint.Models.Tour;

/// <summary>
/// Lifecycle state of a running or completed tour.
/// Valid transitions: <c>NotStarted → InProgress → Completed | Dismissed</c>.
/// No other transitions are valid. Replaying a tour creates a fresh
/// <see cref="TourProgress"/> rather than reusing the old one.
/// </summary>
public enum TourStatus
{
    NotStarted,
    InProgress,
    Completed,
    Dismissed
}
