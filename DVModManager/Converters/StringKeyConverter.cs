using Avalonia.Data.Converters;
using DVModManager.Services;

namespace DVModManager.Converters;

/// <summary>
/// Value converter that translates a localization key (from ViewModel) to a localized string.
/// Binding: Text="{Binding Some ObservableProperty, Converter={StaticResource LocalizeConverter}}"
/// where the property returns a localization key like "status.activated".
/// </summary>
public class StringKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
            return "";

        try
        {
            var localizationService = App.Services.GetService(typeof(ILocalizationService)) as ILocalizationService;
            if (localizationService == null)
                return key; // Fallback

            // If parameter contains format args, apply them
            if (parameter is object[] args)
                return localizationService.GetString(key, args);

            return localizationService.GetString(key);
        }
        catch
        {
            return key;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
