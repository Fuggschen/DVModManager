using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DVModManager.ViewModels;

public partial class ModItemViewModel : ViewModelBase
{
    private readonly ModInfo _modInfo;
    private readonly ILocalizationService? _localization;

    /// <summary>Normal constructor from a scanned ModInfo.</summary>
    public ModItemViewModel(ModInfo modInfo)
    {
        _modInfo = modInfo;

        try
        {
            _localization = App.Services.GetService(typeof(ILocalizationService)) as ILocalizationService;
            if (_localization != null)
                _localization.LanguageChanged += (_, _) => OnPropertyChanged(nameof(StatusLabel));
        }
        catch
        {
            _localization = null;
        }

        SyncFromModel();
    }

    /// <summary>Ghost constructor for a mod that is in a group but missing from disk.</summary>
    public static ModItemViewModel CreateMissing(string modId) =>
        new(new ModInfo { Id = modId, State = ModState.Missing }) { IsMissing = true };

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private ModState _state;
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private bool _hasMissingDependency;
    [ObservableProperty] private bool _hasMetadata;
    [ObservableProperty] private string? _homePage;
    [ObservableProperty] private string? _repository;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string[] _requirements = [];

    /// <summary>Set by ApplyGroupedFilters to indicate which group this item belongs to.</summary>
    public string? GroupId { get; set; }

    /// <summary>True when the mod is inside a group (used for indent).</summary>
    public bool IsGrouped => GroupId != null;

    /// <summary>True when the mod entry is a ghost (in a group but not found on disk).</summary>
    public bool IsMissing { get; private set; }

    public ModInfo ModInfo => _modInfo;

    private string LocalizeStatus(string key, string fallback, params object?[] args)
    {
        if (_localization == null)
            return args.Length == 0 ? fallback : string.Format(fallback, args);

        var localized = _localization.GetString(key, args);
        if (localized == key)
            return args.Length == 0 ? fallback : string.Format(fallback, args);

        return localized;
    }

    public string StatusLabel => State switch
    {
        ModState.UpdateAvailable   => LocalizeStatus("modstate.update_available", "Update available → {0}", UpdateVersion),
        ModState.MissingDependency => LocalizeStatus("modstate.missing_dependency", "Missing dependency"),
        ModState.NoMetadata        => LocalizeStatus("modstate.no_metadata", "No metadata"),
        ModState.Missing           => LocalizeStatus("modstate.missing", "Missing — not found on disk"),
        ModState.Active            => LocalizeStatus("modstate.active", "Active"),
        _                          => LocalizeStatus("modstate.inactive", "Inactive")
    };

    public IBrush StatusBrush => State switch
    {
        ModState.Active          => new SolidColorBrush(Color.Parse("#2ecc71")),
        ModState.UpdateAvailable => new SolidColorBrush(Color.Parse("#f39c12")),
        ModState.MissingDependency or ModState.NoMetadata or ModState.Missing
                                 => new SolidColorBrush(Color.Parse("#e74c3c")),
        _                        => new SolidColorBrush(Color.Parse("#7f8c8d"))
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
