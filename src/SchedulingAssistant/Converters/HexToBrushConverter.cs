using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a <c>#RRGGBB</c> hex string to a <see cref="SolidColorBrush"/> for use
/// as a Background or Foreground binding value. Falls back to transparent on failure.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly HexToBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Color.TryParse(s, out var c))
            return new SolidColorBrush(c);
        return Brushes.Transparent;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush b)
            return $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
        return string.Empty;
    }
}
