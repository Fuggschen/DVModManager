using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DVModManager.Models;

namespace DVModManager.Converters;

public class ModStateToBrushConverter : IValueConverter
{
    public static readonly ModStateToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ModState state
            ? state switch
            {
                ModState.Active => new SolidColorBrush(Color.Parse("#2ecc71")),
                ModState.UpdateAvailable => new SolidColorBrush(Color.Parse("#f39c12")),
                ModState.MissingDependency or ModState.NoMetadata => new SolidColorBrush(Color.Parse("#e74c3c")),
                _ => new SolidColorBrush(Color.Parse("#7f8c8d"))
            }
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
