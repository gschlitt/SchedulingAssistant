using Avalonia.Media;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// A single selectable flag choice, shown in the tile context menu's Flag sub-panel and in
/// the section editor's Flag dropdown.
/// Carries the enum value plus its display label and swatch brush (resolved from
/// <see cref="FlagVisuals"/>); <see cref="HasIcon"/> is false for the "None" option.
/// Use <see cref="CreateOptions"/> to obtain the standard list so every picker offers the
/// same choices in the same order.
/// </summary>
public class FlagOptionVm
{
    public SectionFlag Value { get; }
    public string Label { get; }

    /// <summary>Swatch color for the flag icon, or null for the None option.</summary>
    public IBrush? Brush { get; }

    /// <summary>True when this option draws a colored flag icon (i.e. not None).</summary>
    public bool HasIcon => Value != SectionFlag.None;

    public FlagOptionVm(SectionFlag value, string label)
    {
        Value = value;
        Label = label;
        Brush = FlagVisuals.ResolveBrush(value);
    }

    /// <summary>
    /// Builds the standard list of flag choices, in display order: None "(None)", Red, Blue, Green.
    /// A new list of new instances is returned on every call (rather than a cached static) because
    /// each option's <see cref="Brush"/> is resolved from the application resources at construction.
    /// </summary>
    /// <returns>A fresh list containing one <see cref="FlagOptionVm"/> per <see cref="SectionFlag"/> value.</returns>
    public static IReadOnlyList<FlagOptionVm> CreateOptions() =>
    [
        new(SectionFlag.None, "(None)"),
        new(SectionFlag.Red, "Red"),
        new(SectionFlag.Blue, "Blue"),
        new(SectionFlag.Green, "Green"),
    ];
}
