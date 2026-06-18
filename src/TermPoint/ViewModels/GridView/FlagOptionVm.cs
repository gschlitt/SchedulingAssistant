using Avalonia.Media;
using TermPoint.Models;
using TermPoint.Services;

namespace TermPoint.ViewModels.GridView;

/// <summary>
/// A single selectable flag choice in the tile context menu's Flag sub-panel.
/// Carries the enum value plus its display label and swatch brush (resolved from
/// <see cref="FlagVisuals"/>); <see cref="HasIcon"/> is false for the "None" option.
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
}
