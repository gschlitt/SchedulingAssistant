using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace SchedulingAssistant.Converters;

/// <summary>
/// Converts a boolean (is multi-semester mode) to a BorderThickness.
/// When true, returns 10,0,0,0 (10pt left border for semester indicator).
/// When false, returns 0 (no border).
/// </summary>
public class MultiSemesterLeftBorderThicknessConverter : IValueConverter
{
    public static readonly MultiSemesterLeftBorderThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isMultiSemester && isMultiSemester)
        {
            return new Thickness(10, 0, 0, 0);
        }
        return new Thickness(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
