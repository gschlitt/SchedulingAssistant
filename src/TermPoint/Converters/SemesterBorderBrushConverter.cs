using Avalonia.Data.Converters;
using TermPoint.Services;
using System.Globalization;

namespace TermPoint.Converters;

/// <summary>
/// Converts a semester name + optional hex color into the border brush resolved from
/// AppColors' <c>*Border</c> keys (falls back to the hex color when one is supplied).
/// Delegates to <see cref="SemesterBrushResolver.Resolve"/> so there is a single source
/// of truth for semester brush resolution.
/// </summary>
/// <remarks>
/// MultiBinding inputs, in order:
/// <list type="number">
///   <item><c>SemesterName</c> (string) — e.g. "Fall 2025"</item>
///   <item><c>SemesterColor</c> (string) — e.g. "#C65D1E", or empty</item>
/// </list>
/// </remarks>
public class SemesterBorderBrushConverter : IMultiValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly SemesterBorderBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var semesterName = values.Count > 0 ? values[0] as string ?? "" : "";
        var hexColor     = values.Count > 1 ? values[1] as string ?? "" : "";
        return SemesterBrushResolver.Resolve(semesterName, hexColor);
    }
}
