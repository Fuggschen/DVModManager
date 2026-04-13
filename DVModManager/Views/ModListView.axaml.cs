using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class ModListView : UserControl
{
    // ── Styled properties ─────────────────────────────────────────────────────

    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Header), "Mods");

    public static readonly StyledProperty<ObservableCollection<ModItemViewModel>?> ModsProperty =
        AvaloniaProperty.Register<ModListView, ObservableCollection<ModItemViewModel>?>(nameof(Mods));

    public static readonly StyledProperty<ModItemViewModel?> SelectedModProperty =
        AvaloniaProperty.Register<ModListView, ModItemViewModel?>(nameof(SelectedMod));

    public static readonly StyledProperty<string> FilterProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Filter), "");

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public ObservableCollection<ModItemViewModel>? Mods
    {
        get => GetValue(ModsProperty);
        set => SetValue(ModsProperty, value);
    }

    public ModItemViewModel? SelectedMod
    {
        get => GetValue(SelectedModProperty);
        set => SetValue(SelectedModProperty, value);
    }

    public string Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    public ModListView()
    {
        InitializeComponent();
    }
}
