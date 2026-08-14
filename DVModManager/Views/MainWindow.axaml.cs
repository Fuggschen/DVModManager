using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class MainWindow : Window
{
    private readonly HashSet<string> _externalDropExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip"
    };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.InitializeAsync();
        };
    }

    private bool IsExternalFileDrag(DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return false;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return false;
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.Name);
            if (_externalDropExtensions.Contains(ext))
                return true;
        }
        return false;
    }

    private ModListView? FindTargetPanel(DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        var hit = this.InputHitTest(pos);
        if (hit is Avalonia.Visual v)
            return v.FindAncestorOfType<ModListView>(includeSelf: true);
        return null;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!IsExternalFileDrag(e)) return;

        var panel = FindTargetPanel(e);
        if (panel != null)
        {
            var hint = panel.FindControl<Panel>("DropHintOverlay");
            if (hint != null) hint.IsVisible = true;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        foreach (var panel in this.GetVisualDescendants().OfType<ModListView>())
        {
            var hint = panel.FindControl<Panel>("DropHintOverlay");
            if (hint != null) hint.IsVisible = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!IsExternalFileDrag(e)) return;

        var activePanel = FindTargetPanel(e);
        foreach (var panel in this.GetVisualDescendants().OfType<ModListView>())
        {
            var hint = panel.FindControl<Panel>("DropHintOverlay");
            if (hint != null)
                hint.IsVisible = panel == activePanel;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        foreach (var panel in this.GetVisualDescendants().OfType<ModListView>())
        {
            var hint = panel.FindControl<Panel>("DropHintOverlay");
            if (hint != null) hint.IsVisible = false;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        var archivePaths = new List<string>();
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.Name);
            if (_externalDropExtensions.Contains(ext))
                archivePaths.Add(file.Path.LocalPath);
        }

        if (archivePaths.Count == 0) return;

        var targetPanel = FindTargetPanel(e);
        bool activate = targetPanel != null &&
            string.Equals(targetPanel.Panel, "active", StringComparison.OrdinalIgnoreCase);

        if (DataContext is MainWindowViewModel vm)
            await vm.InstallDroppedArchivesAsync(archivePaths, activate);

        e.Handled = true;
    }

    private void OnUpdateLabelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            _ = vm.OpenManagerUpdateAsync();
    }
}
