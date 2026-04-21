using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using DVModManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DVModManager.Converters;

/// <summary>
/// Markup extension for localizing text.
/// Usage in XAML: Text="{conv:Localize toolbar.save_profile}"
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocalizeExtension() { }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        var source = App.Services.GetRequiredService<ILocalizationService>();

        // Wrap each key in a dedicated observable so PropertyChanged("Value") fires
        // on language change — Avalonia's ReflectionBindingExtension reliably handles
        // named-property change notifications but NOT "Item[]" (WPF/Silverlight convention).
        var localizedValue = new LocalizedValue(source, Key);

        var binding = new ReflectionBindingExtension(nameof(LocalizedValue.Value))
        {
            Source = localizedValue,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        };

        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Single-key observable wrapper over <see cref="ILocalizationService"/>.
/// Raises PropertyChanged("Value") whenever the active language changes,
/// ensuring bound UI elements update immediately without relying on "Item[]".
/// </summary>
internal sealed class LocalizedValue : INotifyPropertyChanged
{
    private readonly ILocalizationService _service;
    private readonly string _key;

    public string Value => _service[_key];

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizedValue(ILocalizationService service, string key)
    {
        _service = service;
        _key = key;
        service.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
