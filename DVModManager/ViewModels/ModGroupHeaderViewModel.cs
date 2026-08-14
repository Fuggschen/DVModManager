using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DVModManager.ViewModels;

public partial class ModGroupHeaderViewModel : ViewModelBase
{
    // Callbacks wired by MainWindowViewModel so the header can reach back without coupling
    private readonly Func<string, string, Task> _onRename;        // (groupId, newName)
    private readonly Func<string, Task> _onDelete;                 // (groupId)
    private readonly Func<string, bool, string, Task> _onToggle;  // (groupId, isCollapsed, panel)
    private readonly ILocalizationService? _localization;

    [ObservableProperty] private string _groupId = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _modCount;
    [ObservableProperty] private bool _isCollapsed;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _editName = "";

    /// <summary>Which panel ("available" or "active") this header belongs to.</summary>
    public string Panel { get; }

    public ModGroupHeaderViewModel(
        string groupId,
        string name,
        int modCount,
        bool isCollapsed,
        string panel,
        Func<string, string, Task> onRename,
        Func<string, Task> onDelete,
        Func<string, bool, string, Task> onToggle)
    {
        _groupId     = groupId;
        _name        = name;
        _modCount    = modCount;
        _isCollapsed = isCollapsed;
        _editName    = name;
        Panel        = panel;
        _onRename    = onRename;
        _onDelete    = onDelete;
        _onToggle    = onToggle;

        try
        {
            _localization = App.Services.GetService(typeof(ILocalizationService)) as ILocalizationService;
            if (_localization != null)
                _localization.LanguageChanged += (_, _) => OnPropertyChanged(nameof(ModCountLabel));
        }
        catch { }
    }

    /// <summary>Localized mod count label (e.g. "3 mod(s)").</summary>
    public string ModCountLabel
    {
        get
        {
            if (_localization != null)
                return _localization.GetString("group.mod_count_format", ModCount);
            return $"{ModCount} mod(s)";
        }
    }

    partial void OnModCountChanged(int value) => OnPropertyChanged(nameof(ModCountLabel));

    [RelayCommand]
    private async Task ToggleCollapseAsync()
    {
        IsCollapsed = !IsCollapsed;
        await _onToggle(GroupId, IsCollapsed, Panel);
    }

    [RelayCommand]
    private void BeginRename()
    {
        EditName = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        IsRenaming = false;
        if (!string.IsNullOrWhiteSpace(EditName) && EditName != Name)
            await _onRename(GroupId, EditName.Trim());
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
        EditName = Name;
    }

    [RelayCommand]
    private async Task DeleteGroupAsync() => await _onDelete(GroupId);
}