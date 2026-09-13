using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DVModManager.Converters;

/// <summary>
/// Converts a bool (IsMissing) to a brush: orange (#fab387) for missing, muted (#a6adc8) for satisfied.
/// Used as a static resource via x:Static in XAML.
/// </summary>
public class DependencyColorConverter : IValueConverter
{
    public static readonly DependencyColorConverter Instance = new();

    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#fab387"));
    private static readonly IBrush SatisfiedBrush = new SolidColorBrush(Color.Parse("#a6adc8"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? MissingBrush : SatisfiedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}