using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Two-way converter between a <c>#RRGGBB</c> hex string (on the ViewModel) and an
/// Avalonia <see cref="Color"/> (on the View). Lets ViewModels avoid exposing
/// <see cref="Color"/> properties to bind color-picker controls like <c>ColorView</c>.
/// Falls back to <see cref="Colors.Gray"/> on unparseable or empty input.
/// </summary>
public class HexToColorConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly HexToColorConverter Instance = new();

    /// <summary>Parses a hex string into a <see cref="Color"/>, or returns grey on failure.</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Color.TryParse(s, out var c)) return c;
        return Colors.Gray;
    }

    /// <summary>Formats a <see cref="Color"/> as an uppercase <c>#RRGGBB</c> hex string.</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c) return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return string.Empty;
    }
}
