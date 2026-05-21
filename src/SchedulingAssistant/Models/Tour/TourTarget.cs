namespace SchedulingAssistant.Models.Tour;

/// <summary>
/// Identifies a UI element that a tour step points at.
/// <see cref="Kind"/> selects the resolution strategy; <see cref="Value"/>
/// is the identifier interpreted according to that strategy.
/// </summary>
public readonly record struct TourTarget
{
    /// <summary>Resolution strategy (NamedControl, Region, or MenuButton).</summary>
    public TourTargetKind Kind { get; }

    /// <summary>
    /// The identifier for the target element (e.g. <c>"SectionViewPanel"</c> for NamedControl,
    /// <c>"ScheduleGrid.Canvas"</c> for Region). Must not be empty.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new tour target.
    /// </summary>
    /// <param name="kind">Resolution strategy.</param>
    /// <param name="value">Target identifier. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null or whitespace.</exception>
    public TourTarget(TourTargetKind kind, string value)
    {
        if (kind != TourTargetKind.None && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("TourTarget value must not be empty.", nameof(value));

        Kind = kind;
        Value = value ?? string.Empty;
    }

    /// <summary>
    /// Convenience factory for an untargeted step (centered card, no highlight).
    /// </summary>
    public static TourTarget None => new(TourTargetKind.None, string.Empty);

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{Value}";
}
