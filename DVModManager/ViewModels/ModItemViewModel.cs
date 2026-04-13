using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DVModManager.Models;

namespace DVModManager.ViewModels;

public partial class ModItemViewModel : ViewModelBase
{
    private readonly ModInfo _modInfo;

    public ModItemViewModel(ModInfo modInfo)
    {
        _modInfo = modInfo;
        SyncFromModel();
    }

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private ModState _state;
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private bool _hasMissingDependency;
    [ObservableProperty] private bool _hasMetadata;
    [ObservableProperty] private string? _homePage;
    [ObservableProperty] private string? _repository;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string[] _requirements = [];

    public ModInfo ModInfo => _modInfo;

    public string StatusLabel => State switch
    {
        ModState.UpdateAvailable => $"Update available → {UpdateVersion}",
        ModState.MissingDependency => "Missing dependency",
        ModState.NoMetadata => "No metadata",
        ModState.Active => "Active",
        _ => "Inactive"
    };

    public IBrush StatusBrush => State switch
    {
        ModState.Active => new SolidColorBrush(Color.Parse("#2ecc71")),
        ModState.UpdateAvailable => new SolidColorBrush(Color.Parse("#f39c12")),
        ModState.MissingDependency or ModState.NoMetadata => new SolidColorBrush(Color.Parse("#e74c3c")),
        _ => new SolidColorBrush(Color.Parse("#7f8c8d"))
    };

    public void ApplyUpdate(ModUpdateInfo update)
    {
        HasUpdate = true;
        UpdateVersion = update.LatestVersion;
        _modInfo.PendingUpdate = update;
        State = ModState.UpdateAvailable;
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusBrush));
    }

    public void SyncFromModel()
    {
        Id = _modInfo.Id;
        DisplayName = _modInfo.EffectiveDisplayName;
        Author = _modInfo.Author;
        Version = _modInfo.Version;
        IsActive = _modInfo.IsActive;
        State = _modInfo.State;
        HasMetadata = _modInfo.HasMetadata;
        HomePage = _modInfo.HomePage;
        Repository = _modInfo.Repository;
        Description = _modInfo.Description;
        Requirements = _modInfo.Requirements;

        if (_modInfo.PendingUpdate != null)
        {
            HasUpdate = true;
            UpdateVersion = _modInfo.PendingUpdate.LatestVersion;
        }

        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusBrush));
    }
}
