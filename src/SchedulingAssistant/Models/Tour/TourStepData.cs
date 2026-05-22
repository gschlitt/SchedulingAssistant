namespace SchedulingAssistant.Models.Tour;

/// <summary>
/// AXAML-instantiable content class for a tour step. Parameterless constructor and
/// public setters allow declaration in a <c>ResourceDictionary</c> with HotAvalonia
/// hot-reload support. Converted to an immutable <see cref="TourStep"/> by
/// <see cref="Services.TourCatalog"/> at startup.
/// </summary>
public class TourStepData
{
    /// <summary>Short heading displayed in the tour card.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>One to three sentences of explanatory text.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Target resolution strategy as a string (e.g. <c>"NamedControl"</c>, <c>"Region"</c>,
    /// <c>"MenuButton"</c>). Parsed to <see cref="TourTargetKind"/> during catalog initialization.
    /// </summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>
    /// The identifier for the target element, interpreted according to <see cref="TargetKind"/>
    /// (e.g. <c>"SectionViewPanel"</c> for NamedControl).
    /// </summary>
    public string TargetValue { get; set; } = string.Empty;

    /// <summary>
    /// Preferred card position as a string (e.g. <c>"Right"</c>, <c>"Below"</c>, <c>"Auto"</c>).
    /// Parsed to <see cref="TourPlacement"/> during catalog initialization.
    /// Defaults to <c>"Auto"</c> if empty.
    /// </summary>
    public string Placement { get; set; } = string.Empty;

    /// <summary>
    /// Card width in pixels. Defaults to 320. Set a larger value in AXAML for
    /// steps that need more room (e.g. welcome/introduction cards).
    /// </summary>
    public double CardWidth { get; set; } = 320;
}
