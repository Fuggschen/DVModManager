using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class ModListView : UserControl
{
    // Token written to DataTransfer so the drop handler can verify the source
    private const string DragToken = "dvmm-mod-drag";

    // Static payload: safe because only one drag can be in-flight at a time
    private static ModItemViewModel? s_dragPayload;

    // ── Styled properties ─────────────────────────────────────────────────────

    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Header), "Mods");

    public static readonly StyledProperty<IEnumerable?> ModsProperty =
        AvaloniaProperty.Register<ModListView, IEnumerable?>(nameof(Mods));

    public static readonly StyledProperty<object?> SelectedModProperty =
        AvaloniaProperty.Register<ModListView, object?>(nameof(SelectedMod));

    public static readonly StyledProperty<string> FilterProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Filter), "");

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public IEnumerable? Mods
    {
        get => GetValue(ModsProperty);
        set => SetValue(ModsProperty, value);
    }

    public object? SelectedMod
    {
        get => GetValue(SelectedModProperty);
        set => SetValue(SelectedModProperty, value);
    }

    public string Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    // ── Drag state ────────────────────────────────────────────────────────────

    private Point? _dragOrigin;
    private PointerPressedEventArgs? _pressedArgs; // Avalonia 12: DoDragDropAsync needs the original pressed args

    public ModListView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var lb = this.FindControl<ListBox>("ModListBox");
        if (lb == null) return;

        lb.SelectionChanged += OnListSelectionChanged;
        lb.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        lb.AddHandler(PointerMovedEvent,   OnListPointerMoved,   RoutingStrategies.Tunnel);
        lb.AddHandler(DragDrop.DragOverEvent, OnListDragOver);
        lb.AddHandler(DragDrop.DropEvent,     OnListDrop);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var lb = this.FindControl<ListBox>("ModListBox");
        if (lb == null) return;

        lb.SelectionChanged -= OnListSelectionChanged;
        lb.RemoveHandler(PointerPressedEvent, OnListPointerPressed);
        lb.RemoveHandler(PointerMovedEvent,   OnListPointerMoved);
        lb.RemoveHandler(DragDrop.DragOverEvent, OnListDragOver);
        lb.RemoveHandler(DragDrop.DropEvent,     OnListDrop);
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var item = (sender as ListBox)?.SelectedItem;
        if (item is ModGroupHeaderViewModel gvm)
        {
            vm.SelectedGroup = gvm;
            vm.SelectedMod   = null;
        }
        else
        {
            vm.SelectedGroup = null;
            vm.SelectedMod   = item as ModItemViewModel;
        }
    }

    // ── Drag initiation ───────────────────────────────────────────────────────

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedArgs  = null;
        _dragOrigin   = null;
        s_dragPayload = null;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var lbItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (lbItem?.DataContext is ModItemViewModel mvm)
        {
            _pressedArgs  = e;
            _dragOrigin   = e.GetPosition(this);
            s_dragPayload = mvm;
        }
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (s_dragPayload == null || _dragOrigin == null || _pressedArgs == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            s_dragPayload = null;
            _dragOrigin   = null;
            _pressedArgs  = null;
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin.Value;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;

        // Threshold exceeded — kick off system DnD
        var pressedArgs = _pressedArgs;
        _pressedArgs = null;
        _dragOrigin  = null;
        // s_dragPayload stays set until DoDragDropAsync completes

        var item     = DataTransferItem.CreateText(DragToken);
        var transfer = new DataTransfer();
        transfer.Add(item);

        await DragDrop.DoDragDropAsync(pressedArgs, transfer, DragDropEffects.Move);

        // Clear after drop (also cleared in OnListDrop, but guard here too)
        s_dragPayload = null;
    }

    // ── Drop handling ─────────────────────────────────────────────────────────

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = (e.DataTransfer.TryGetText() == DragToken)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() != DragToken) return;
        var mod = s_dragPayload;
        s_dragPayload = null;
        if (mod == null) return;

        // Walk up from the drop source to find a ListBoxItem
        var lbItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        string? groupId = lbItem?.DataContext is ModGroupHeaderViewModel hvm ? hvm.GroupId : null;

        if (DataContext is MainWindowViewModel vm)
            _ = vm.AssignModToGroupAsync(mod.Id, groupId);

        e.Handled = true;
    }
}
