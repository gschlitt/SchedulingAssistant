using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a boolean (<see cref="SectionListItemViewModel.IsFilterHighlighted"/>) to a
/// uniform 3 pt <see cref="Thickness"/> used as a section-card border when the section
/// matches the active Schedule Grid filter.
/// Returns <c>Thickness(3)</c> when <c>true</c>, <c>Thickness(0)</c> when <c>false</c>.
/// </summary>
public class FilterBorderThicknessConverter : IValueConverter
{
    /// <summary>Shared singleton instance for use as a static XAML resource.</summary>
    public static readonly FilterBorderThicknessConverter Instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool highlighted && highlighted ? new Thickness(3) : new Thickness(0);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
