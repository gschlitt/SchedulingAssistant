using Avalonia.Data.Converters;
using SchedulingAssistant.Services;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a semester name + optional hex color into the background brush resolved from
/// AppColors' <c>*Background</c> keys (falls back to the hex color when one is supplied).
/// Companion to <see cref="SemesterBorderBrushConverter"/>; delegates to
/// <see cref="SemesterBrushResolver.ResolvePair"/> so brush-resolution logic stays in one place.
/// </summary>
/// <remarks>
/// MultiBinding inputs, in order:
/// <list type="number">
///   <item><c>SemesterName</c> (string) — e.g. "Fall 2025"</item>
///   <item><c>SemesterColor</c> (string) — e.g. "#C65D1E", or empty</item>
/// </list>
/// </remarks>
public class SemesterBackgroundBrushConverter : IMultiValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly SemesterBackgroundBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var semesterName = values.Count > 0 ? values[0] as string ?? "" : "";
        var hexColor     = values.Count > 1 ? values[1] as string ?? "" : "";
        return SemesterBrushResolver.ResolvePair(semesterName, hexColor).bg;
    }
}
