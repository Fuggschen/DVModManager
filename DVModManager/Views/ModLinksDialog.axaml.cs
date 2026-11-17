using Avalonia.Controls;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class ModLinksDialog : Window
{
    public ModLinksDialog()
    {
        InitializeComponent();
        // Closing by the title bar must stop the search as well as the buttons do.
        Closed += (_, _) => (DataContext as ModLinksViewModel)?.CancelSearch();
    }
}
