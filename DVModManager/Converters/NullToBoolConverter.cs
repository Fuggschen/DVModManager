using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DVModManager.Converters;

/// <summary>
/// Returns true when value is not null.
/// Pass ConverterParameter="invert" to return true when null instead.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNotNull = value != null;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            return !isNotNull;
        return isNotNull;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
