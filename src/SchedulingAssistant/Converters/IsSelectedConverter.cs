using Avalonia.Data.Converters;
using System.Globalization;

namespace SchedulingAssistant.Converters;

public class IsSelectedConverter : IValueConverter
{
    public static readonly IsSelectedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // value: the selected section ID from the ViewModel
        // parameter: the item ID to compare against
        if (value is string selectedId && parameter is string itemId)
        {
            return selectedId == itemId;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
