using Avalonia;
using Avalonia.Media;
using SchedulingAssistant.Models;

namespace SchedulingAssistant.Services;

/// <summary>
/// Single source of truth for mapping a <see cref="SectionFlag"/> to its visual
/// representation. Both the Section List card and the Schedule grid renderer resolve
/// flag colors through here so the palette stays consistent in one place.
/// The actual color values live in <c>AppColors.axaml</c> (keys FlagRed/FlagBlue/FlagGreen).
/// </summary>
public static class FlagVisuals
{
    /// <summary>
    /// Returns the <c>AppColors.axaml</c> brush resource key for the given flag,
    /// or <c>null</c> for <see cref="SectionFlag.None"/>.
    /// </summary>
    public static string? ResourceKey(SectionFlag flag) => flag switch
    {
        SectionFlag.Red   => "FlagRed",
        SectionFlag.Blue  => "FlagBlue",
        SectionFlag.Green => "FlagGreen",
        _ => null
    };

    /// <summary>
    /// Resolves the brush for the given flag from the application resource dictionary,
    /// or <c>null</c> when the flag is None or the resource is missing.
    /// </summary>
    public static IBrush? ResolveBrush(SectionFlag flag)
    {
        var key = ResourceKey(flag);
        if (key is null || Application.Current is null) return null;
        return Application.Current.Resources.TryGetResource(key, null, out var v) && v is IBrush b
            ? b : null;
    }
}
