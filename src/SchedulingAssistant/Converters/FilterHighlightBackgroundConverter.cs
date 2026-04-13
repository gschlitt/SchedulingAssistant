using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a boolean (<c>IsFilterHighlighted</c>) to a background <see cref="IBrush"/>
/// for section cards. Returns <c>FilterSelectedSectionBackgroundColor</c> when <c>true</c>,
/// <c>SurfaceBackground</c> when <c>false</c>, falling back to <see cref="Brushes.White"/>.
/// </summary>
public class FilterHighlightBackgroundConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly FilterHighlightBackgroundConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "FilterSelectedSectionBackgroundColor" : "SurfaceBackground";
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true
            && resource is IBrush brush)
            return brush;
        return Brushes.White;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
