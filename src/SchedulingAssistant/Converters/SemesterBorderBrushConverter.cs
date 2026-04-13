using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a semester name and optional hex color into the corresponding border
/// <see cref="IBrush"/>. Used via <c>MultiBinding</c> with two inputs:
/// <list type="number">
///   <item><c>SemesterName</c> (string) — e.g. "Fall 2025"</item>
///   <item><c>SemesterColor</c> (string) — e.g. "#C65D1E", or empty</item>
/// </list>
/// Priority: hex color parse first, then name-based resource key lookup from AppColors.
/// </summary>
public class SemesterBorderBrushConverter : IMultiValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly SemesterBorderBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var semesterName = values.Count > 0 ? values[0] as string ?? "" : "";
        var hexColor = values.Count > 1 ? values[1] as string ?? "" : "";

        // Priority 1: explicit hex color
        if (!string.IsNullOrWhiteSpace(hexColor))
        {
            try { return new SolidColorBrush(Color.Parse(hexColor)); }
            catch { /* fall through to name-based lookup */ }
        }

        // Priority 2: semester name → resource key
        var firstWord = semesterName.Split(' ')[0];
        var key = firstWord switch
        {
            "Fall"   => "FallBorder",
            "Winter" => "WinterBorder",
            "Early"  => "EarlySummerBorder",
            "Summer" => "SummerBorder",
            "Late"   => "LateSummerBorder",
            _        => "FallBorder"
        };

        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
            return resource as IBrush;

        return null;
    }
}
