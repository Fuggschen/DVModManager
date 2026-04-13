using Avalonia.Controls;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.InitializeAsync();
        };
    }
}
