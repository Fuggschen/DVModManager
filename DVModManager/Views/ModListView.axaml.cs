using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using DVModManager.ViewModels;

namespace DVModManager.Views;

public partial class ModListView : UserControl
{
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
    private ModItemViewModel? _draggedMod;         // The mod the pointer actually pressed on
    private ModGroupHeaderViewModel? _draggedGroup; // The group header the pointer pressed on
    private ModItemViewModel? _pendingSingleSelect; // Deferred clear-and-select (cancelled if drag starts)

    // Static payload holds all mods being dragged — safe because only one drag can be in-flight at a time
    private static List<ModItemViewModel>? s_dragPayload;
    private static bool s_dragIsGroup;

    // ── Drag auto-scroll state
    private DispatcherTimer? _dragScrollTimer;
    private int _dragScrollDirection;
    private double _dragScrollSpeed = 8.0;

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
        if (s_activeDrag?.Source == this)
            EndDrag();
        StopDragAutoScroll();
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
        _dragOrigin         = null;
        _draggedMod         = null;
        _draggedGroup       = null;
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

            _dragOrigin   = e.GetPosition(this);
            _draggedGroup = gvm;
            return;
        }

        // ── Mod item clicked ──────────────────────────────────────────────────
        if (lbItem?.DataContext is not ModItemViewModel mvm) return;

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
        if (s_activeDrag?.Source == this)
        {
            _pendingSingleSelect = null;
            CompleteDrag(e);
            e.Handled = true;
            return;
        }

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

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (s_activeDrag != null)
        {
            if (s_activeDrag.Source == this)
            {
                UpdateDrag(e);
                e.Handled = true;
            }
            return;
        }

        if ((_draggedMod == null && _draggedGroup == null) || _dragOrigin == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            s_dragPayload = null;
            _draggedMod   = null;
            _draggedGroup = null;
            _dragOrigin   = null;
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin.Value;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;

        _pendingSingleSelect = null;
        s_dragIsGroup = _draggedGroup != null;
        if (_draggedGroup != null)
        {
            // Group header: drag every mod of that group shown in this panel
            var groupId = _draggedGroup.GroupId;
            s_dragPayload = DataContext is MainWindowViewModel gvm
                ? (Panel == "active" ? gvm.ActiveMods : gvm.AvailableMods)
                    .Where(m => m.GroupId == groupId)
                    .ToList()
                : [];
        }
        else if (_draggedMod is { } draggedMod)
        {
            var checkedMods = DataContext is MainWindowViewModel vm
                ? vm.AvailableMods.Concat(vm.ActiveMods).Where(m => m.IsChecked).ToList()
                : [];
            s_dragPayload = checkedMods.Contains(draggedMod)
                ? checkedMods
                : [draggedMod];
        }

        _dragOrigin   = null;
        _draggedMod   = null;
        _draggedGroup = null;

        if (s_dragPayload is not { Count: > 0 })
        {
            s_dragPayload = null;
            return;
        }

        BeginDrag(e);
    }

    // ── In-app drag ───────────────────────────────────────────────────────────
    // Mod moves never leave the window, so this deliberately avoids
    // DragDrop.DoDragDropAsync: the OS drag loop (OLE DoDragDrop on Windows) is
    // modal and swallows mouse wheel messages. Capturing the pointer keeps all
    // input inside Avalonia, so the wheel can scroll whichever list is hovered.

    private sealed class DragSession(ModListView source, ListBox sourceList, IPointer pointer, TopLevel topLevel)
    {
        public ModListView Source     { get; } = source;
        public ListBox     SourceList { get; } = sourceList;
        public IPointer    Pointer    { get; } = pointer;
        public TopLevel    TopLevel   { get; } = topLevel;
        public ModListView? Hovered   { get; set; }
    }

    private static DragSession? s_activeDrag;

    private static readonly Cursor s_dragMoveCursor = new(StandardCursorType.DragMove);
    private static readonly Cursor s_noDropCursor   = new(StandardCursorType.No);

    private void BeginDrag(PointerEventArgs e)
    {
        var lb       = this.FindControl<ListBox>("ModListBox");
        var topLevel = TopLevel.GetTopLevel(this);
        if (lb == null || topLevel == null)
        {
            s_dragPayload = null;
            return;
        }

        s_activeDrag = new DragSession(this, lb, e.Pointer, topLevel);

        lb.AddHandler(PointerWheelChangedEvent, OnDragPointerWheel, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        lb.PointerCaptureLost += OnDragCaptureLost;
        topLevel.AddHandler(KeyDownEvent, OnDragKeyDown, RoutingStrategies.Tunnel);

        e.Pointer.Capture(lb);
        UpdateDrag(e);
    }

    private void UpdateDrag(PointerEventArgs e)
    {
        var drag = s_activeDrag;
        if (drag == null) return;

        var target = FindDragTarget(drag, e, out _);

        if (drag.Hovered != target)
        {
            drag.Hovered?.StopDragAutoScroll();
            drag.Hovered = target;
        }

        target?.UpdateDragAutoScroll(e);
        drag.SourceList.Cursor = target != null ? s_dragMoveCursor : s_noDropCursor;
    }

    private void OnDragPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        var drag = s_activeDrag;
        if (drag == null) return;

        FindDragTarget(drag, e, out _)?.ScrollListBy(-e.Delta.Y * WheelScrollStep);
        UpdateDrag(e);
        e.Handled = true;
    }

    private void OnDragKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || s_activeDrag == null) return;
        EndDrag();
        e.Handled = true;
    }

    private void OnDragCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Capture was taken away (window lost focus, etc.): cancel without dropping
        EndDrag();
    }

    private void CompleteDrag(PointerEventArgs e)
    {
        var drag = s_activeDrag;
        if (drag == null) return;

        var target  = FindDragTarget(drag, e, out var lbItem);
        var payload = s_dragPayload;
        var isGroup = s_dragIsGroup;
        EndDrag();

        if (target != null && payload is { Count: > 0 })
            target.HandleDrop(payload, lbItem, isGroup);
    }

    private void EndDrag()
    {
        var drag = s_activeDrag;
        if (drag == null) return;
        s_activeDrag = null;

        drag.SourceList.RemoveHandler(PointerWheelChangedEvent, OnDragPointerWheel);
        drag.SourceList.PointerCaptureLost -= OnDragCaptureLost;
        drag.TopLevel.RemoveHandler(KeyDownEvent, OnDragKeyDown);
        drag.SourceList.Cursor = null;
        drag.Hovered?.StopDragAutoScroll();

        if (drag.Pointer.Captured == drag.SourceList)
            drag.Pointer.Capture(null);

        s_dragPayload = null;
        s_dragIsGroup = false;
    }

    private static ModListView? FindDragTarget(DragSession drag, PointerEventArgs e, out ListBoxItem? lbItem)
    {
        lbItem = null;
        var hit = drag.TopLevel.InputHitTest(e.GetPosition(drag.TopLevel)) as Visual;
        var target = hit?.FindAncestorOfType<ModListView>(includeSelf: true);
        if (target == null) return null;

        // Only the list area accepts drops, not the header or search box
        var targetList = target.FindControl<ListBox>("ModListBox");
        if (targetList == null || !hit!.GetSelfAndVisualAncestors().Contains(targetList)) return null;

        lbItem = hit.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        return target;
    }

    private const double WheelScrollStep = 50.0;

    private void UpdateDragAutoScroll(PointerEventArgs e)
    {
        var lb = this.FindControl<ListBox>("ModListBox");

        if (lb == null)
        {
            StopDragAutoScroll();
            return;
        }

        var position = e.GetPosition(lb);

        const double edgeSize = 60.0;

        // Cursor is near the top edge
        if (position.Y <= edgeSize)
        {
            _dragScrollDirection = -1;

            var distance = Math.Clamp(
                position.Y,
                0,
                edgeSize);

            var progress = 1.0 - (distance / edgeSize);

            _dragScrollSpeed = CalculateDragScrollSpeed(progress);

            StartDragAutoScroll();
        }

        // Cursor is near the bottom edge
        else if (position.Y >= lb.Bounds.Height - edgeSize)
        {
            _dragScrollDirection = 1;

            var distance = Math.Clamp(
                lb.Bounds.Height - position.Y,
                0,
                edgeSize);

            var progress = 1.0 - (distance / edgeSize);

            _dragScrollSpeed = CalculateDragScrollSpeed(progress);

            StartDragAutoScroll();
        }

        // Cursor is not near either edge
        else
        {
            StopDragAutoScroll();
        }
    }


    private static double CalculateDragScrollSpeed(double progress)
    {
        const double minSpeed = 1.5;
        const double maxSpeed = 30.0;

        var easedProgress = progress * progress * progress;

        return minSpeed +
            ((maxSpeed - minSpeed) * easedProgress);
    }


    private void StartDragAutoScroll()
    {
        if (_dragScrollTimer != null)
            return;

        _dragScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _dragScrollTimer.Tick += OnDragScrollTimerTick;
        _dragScrollTimer.Start();
    }


    private void StopDragAutoScroll()
    {
        if (_dragScrollTimer == null)
            return;

        _dragScrollTimer.Stop();
        _dragScrollTimer.Tick -= OnDragScrollTimerTick;

        _dragScrollTimer = null;
        _dragScrollDirection = 0;
        _dragScrollSpeed = 0;
    }


    private void OnDragScrollTimerTick(object? sender, EventArgs e)
    {
        if (_dragScrollDirection == 0)
            return;

        if (this.FindControl<ListBox>("ModListBox") == null)
        {
            StopDragAutoScroll();
            return;
        }

        ScrollListBy(_dragScrollDirection * _dragScrollSpeed);
    }


    private void ScrollListBy(double deltaY)
    {
        var scrollViewer = this.FindControl<ListBox>("ModListBox")?
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (scrollViewer == null)
            return;

        var offset = scrollViewer.Offset;

        var maxOffsetY = Math.Max(
            0,
            scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

        var newOffsetY = Math.Clamp(
            offset.Y + deltaY,
            0,
            maxOffsetY);

        // Already at the top or bottom
        if (Math.Abs(newOffsetY - offset.Y) < 0.01)
            return;

        scrollViewer.Offset = new Vector(
            offset.X,
            newOffsetY);
    }


    private void HandleDrop(List<ModItemViewModel> payload, ListBoxItem? lbItem, bool isGroup)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Determine which panel the mods were dragged from and which they were dropped on
        bool allInactive = payload.All(m => !m.IsActive);
        bool allActive   = payload.All(m => m.IsActive);

        // Cross-panel: dropping inactive mods on the active panel → activate
        if (allInactive && Panel == "active")
        {
            _ = ActivateDroppedModsAsync(vm, payload);
            return;
        }

        // Cross-panel: dropping active mods on the inactive panel → deactivate
        if (allActive && Panel == "available")
        {
            _ = DeactivateDroppedModsAsync(vm, payload);
            return;
        }

        // Same-panel group drop: groups have no ordering, and merging into
        // another group on a stray drop would be destructive
        if (isGroup) return;

        // Same-panel: assign to group
        string? groupId = lbItem?.DataContext is ModGroupHeaderViewModel hvm ? hvm.GroupId : null;
        _ = vm.AssignModsToGroupAsync(payload.Select(m => m.Id), groupId);
    }

    private static Task ActivateDroppedModsAsync(MainWindowViewModel vm, List<ModItemViewModel> mods)
    {
        // Dedicated batch method — ActivateSelectedModCommand would re-run the
        // whole checked set on every invocation (H11).
        return vm.ActivateModsAsync(mods);
    }

    private static Task DeactivateDroppedModsAsync(MainWindowViewModel vm, List<ModItemViewModel> mods)
    {
        return vm.DeactivateModsAsync(mods);
    }
}
