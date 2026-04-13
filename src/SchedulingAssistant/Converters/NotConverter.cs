using Avalonia.Data.Converters;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a boolean value to its logical negation.
/// <c>true</c> → <c>false</c>; <c>false</c> → <c>true</c>.
/// </summary>
public class NotConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly NotConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
