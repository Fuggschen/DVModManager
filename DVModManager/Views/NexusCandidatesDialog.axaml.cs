using Avalonia.Controls;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class NexusCandidatesDialog : Window
{
    public NexusCandidatesDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is NexusCandidatesViewModel vm)
                vm.CloseRequested += (_, _) => Close();
        };
    }
}
