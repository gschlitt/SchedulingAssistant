using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a boolean to a <see cref="FontStyle"/>: <c>true</c> → <see cref="FontStyle.Italic"/>;
/// <c>false</c> → <see cref="FontStyle.Normal"/>.
/// </summary>
public class BoolToItalicConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly BoolToItalicConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontStyle.Italic : FontStyle.Normal;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
