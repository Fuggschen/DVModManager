using Avalonia.Controls;
using Avalonia.Interactivity;
using DVModManager.Helpers;

namespace DVModManager.Views;

public partial class ModDetailPanel : UserControl
{
    public ModDetailPanel()
    {
        InitializeComponent();
    }

    private void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            PlatformHelper.Open(url);
        }
    }
}
