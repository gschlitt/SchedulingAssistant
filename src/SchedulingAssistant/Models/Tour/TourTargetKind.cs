namespace SchedulingAssistant.Models.Tour;

/// <summary>
/// Determines how a <see cref="TourTarget"/> is resolved to a UI element at runtime.
/// Each kind uses a different resolution strategy in the presentation layer.
/// </summary>
public enum TourTargetKind
{
    /// <summary>
    /// Resolves by matching <c>x:Name</c> in the visual tree.
    /// Primary mechanism — most panels and controls already have well-known names.
    /// </summary>
    NamedControl,

    /// <summary>
    /// Resolves a logical child area within a composite control using dot notation
    /// (e.g. <c>"ScheduleGrid.Canvas"</c>). Used when the outermost container is too
    /// vague and a specific sub-region is needed.
    /// </summary>
    Region,

    /// <summary>
    /// Semantically distinct from <see cref="NamedControl"/> so the presentation layer
    /// can position the tour card differently (e.g. below a menu button rather than
    /// beside a panel).
    /// </summary>
    MenuButton,

    /// <summary>
    /// No UI target — the card is centered in the overlay with no highlight ring
    /// or arrow. Used for welcome/introduction steps.
    /// </summary>
    None
}
