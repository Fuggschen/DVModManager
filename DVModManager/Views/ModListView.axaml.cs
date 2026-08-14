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

    // ── Styled properties ─────────────────────────────────────────────────────

    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Header), "Mods");

    public static readonly StyledProperty<IEnumerable?> ModsProperty =
        AvaloniaProperty.Register<ModListView, IEnumerable?>(nameof(Mods));

    public static readonly StyledProperty<object?> SelectedModProperty =
        AvaloniaProperty.Register<ModListView, object?>(nameof(SelectedMod));

    public static readonly StyledProperty<string> FilterProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Filter), "");

    public static readonly StyledProperty<string> PanelProperty =
        AvaloniaProperty.Register<ModListView, string>(nameof(Panel), "");

    public static readonly StyledProperty<bool> ShowCompanionButtonProperty =
        AvaloniaProperty.Register<ModListView, bool>(nameof(ShowCompanionButton), false);

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

    public string Panel
    {
        get => GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    public bool ShowCompanionButton
    {
        get => GetValue(ShowCompanionButtonProperty);
        set => SetValue(ShowCompanionButtonProperty, value);
    }

    // ── Drag state ────────────────────────────────────────────────────────────

    private Point? _dragOrigin;
    private PointerPressedEventArgs? _pressedArgs; // Avalonia 12: DoDragDropAsync needs the original pressed args
    private ModItemViewModel? _draggedMod;         // The mod the pointer actually pressed on
    private ModItemViewModel? _pendingSingleSelect; // Deferred clear-and-select (cancelled if drag starts)

    // Static payload holds all mods being dragged — safe because only one drag can be in-flight at a time
    private static List<ModItemViewModel>? s_dragPayload;

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
        lb.AddHandler(PointerPressedEvent,  OnListPointerPressed,  RoutingStrategies.Tunnel);
        lb.AddHandler(PointerMovedEvent,    OnListPointerMoved,    RoutingStrategies.Tunnel);
        lb.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel);
        DragDrop.AddDragOverHandler(lb, OnListDragOver);
        DragDrop.AddDropHandler(lb, OnListDrop);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var lb = this.FindControl<ListBox>("ModListBox");
        if (lb == null) return;

        lb.SelectionChanged -= OnListSelectionChanged;
        lb.RemoveHandler(PointerPressedEvent,  OnListPointerPressed);
        lb.RemoveHandler(PointerMovedEvent,    OnListPointerMoved);
        lb.RemoveHandler(PointerReleasedEvent, OnListPointerReleased);
        DragDrop.RemoveDragOverHandler(lb, OnListDragOver);
        DragDrop.RemoveDropHandler(lb, OnListDrop);
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
        _pressedArgs        = null;
        _dragOrigin         = null;
        _draggedMod         = null;
        _pendingSingleSelect = null;
        s_dragPayload       = null;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var lbItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);

        // ── Group header clicked ──────────────────────────────────────────────
        if (lbItem?.DataContext is ModGroupHeaderViewModel gvm)
        {
            if (DataContext is MainWindowViewModel gVm)
            {
                var groupMods = gVm.AvailableMods.Concat(gVm.ActiveMods)
                    .Where(m => m.GroupId == gvm.GroupId)
                    .ToList();

                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    // CTRL+click group: append/toggle all mods in the group
                    bool anyUnchecked = groupMods.Any(m => !m.IsChecked);
                    foreach (var m in groupMods)
                        m.IsChecked = anyUnchecked;
                }
                else
                {
                    // Normal click group: clear all, then check all mods in the group
                    foreach (var m in gVm.AvailableMods.Concat(gVm.ActiveMods))
                        m.IsChecked = false;
                    foreach (var m in groupMods)
                        m.IsChecked = true;
                }
            }
            return;
        }

        // ── Mod item clicked ──────────────────────────────────────────────────
        if (lbItem?.DataContext is not ModItemViewModel mvm) return;

        _pressedArgs = e;
        _dragOrigin  = e.GetPosition(this);
        _draggedMod  = mvm;

        // CheckBox click: let binding handle the toggle, just set up drag potential
        if ((e.Source as Visual)?.FindAncestorOfType<CheckBox>(includeSelf: true) != null) return;

        if (DataContext is MainWindowViewModel vm)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // CTRL+click: toggle this mod without clearing others
                mvm.IsChecked = !mvm.IsChecked;
                e.Handled = true;
            }
            else
            {
                // Normal click: defer clear-and-select-one to PointerReleased
                // so an in-progress drag doesn't lose the multi-selection
                _pendingSingleSelect = mvm;
            }
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pending = _pendingSingleSelect;
        _pendingSingleSelect = null;

        // Only apply if no drag was initiated
        if (pending == null || s_dragPayload != null) return;

        if (DataContext is MainWindowViewModel vm)
        {
            foreach (var m in vm.AvailableMods.Concat(vm.ActiveMods))
                m.IsChecked = false;
            pending.IsChecked = true;
        }
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedMod == null || _dragOrigin == null || _pressedArgs == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            s_dragPayload = null;
            _draggedMod   = null;
            _dragOrigin   = null;
            _pressedArgs  = null;
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin.Value;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;

        // Threshold exceeded — resolve payload: all checked mods if the dragged mod is checked
        _pendingSingleSelect = null; // drag started, cancel deferred single-select
        if (DataContext is MainWindowViewModel vm)
        {
            var checkedMods = vm.AvailableMods.Concat(vm.ActiveMods).Where(m => m.IsChecked).ToList();
            s_dragPayload = checkedMods.Contains(_draggedMod) && checkedMods.Count > 0
                ? checkedMods
                : [_draggedMod];
        }
        else
        {
            s_dragPayload = [_draggedMod];
        }

        // Kick off system DnD
        var pressedArgs = _pressedArgs;
        _pressedArgs = null;
        _dragOrigin  = null;
        _draggedMod  = null;
        // s_dragPayload stays set until DoDragDropAsync completes

        var item     = DataTransferItem.CreateText(DragToken);
        var transfer = new DataTransfer();
        transfer.Add(item);

        await DragDrop.DoDragDropAsync(pressedArgs, transfer, DragDropEffects.Move);

        // Clear after drop (also cleared in OnListDrop, but guard here too)
        s_dragPayload = null;
    }

    // ── Internal mod-to-mod DnD ─────────────────────────────────────────────

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() == DragToken)
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnListDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() != DragToken) return;
        var payload = s_dragPayload;
        s_dragPayload = null;
        if (payload == null || payload.Count == 0) return;

        // Walk up from the drop source to find a ListBoxItem
        var lbItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        string? groupId = lbItem?.DataContext is ModGroupHeaderViewModel hvm ? hvm.GroupId : null;

        if (DataContext is MainWindowViewModel vm)
            _ = vm.AssignModsToGroupAsync(payload.Select(m => m.Id), groupId);

        e.Handled = true;
    }
}
