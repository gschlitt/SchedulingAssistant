using Avalonia.Data.Converters;
using System.Globalization;

namespace SchedulingAssistant.Converters;

public class MinutesToTimeConverter : IValueConverter
{
    public static readonly MinutesToTimeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int minutes)
        {
            int h = minutes / 60, m = minutes % 60;
            return $"{h:D2}:{m:D2}";
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
