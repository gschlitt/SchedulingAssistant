namespace SchedulingAssistant.Models.Tour;

/// <summary>
/// Preferred position of the tour card relative to its target element.
/// The presentation layer uses this as a hint and falls back through the
/// priority order (Right → Below → Left → Above) when the preferred side
/// doesn't fit within the viewport.
/// </summary>
public enum TourPlacement
{
    Right,
    Below,
    Left,
    Above,

    /// <summary>
    /// Let the presentation layer choose the best side automatically.
    /// </summary>
    Auto
}
