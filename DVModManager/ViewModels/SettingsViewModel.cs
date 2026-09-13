using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly IGameDetectionService _gameDetection;
    private readonly ILocalizationService? _localization;

    public bool Saved { get; private set; }

    [ObservableProperty] private string _gamePath = "";
    [ObservableProperty] private string _storagePath = "";
    [ObservableProperty] private bool _autoCheckUpdates = true;
    [ObservableProperty] private bool _enableVersionArchiving = true;
    [ObservableProperty] private int _maxCacheGb = 5;
    [ObservableProperty] private string _themeVariant = "Dark";
    [ObservableProperty] private string _language = "en";

    // Available language options for the UI
    public List<string> AvailableLanguages { get; } = ["en", "de", "fr", "zh"];

    // Display names for languages (for the dropdown)
    public Dictionary<string, string> LanguageDisplayNames => new()
    {
        { "en", "English" },
        { "de", "Deutsch" },
        { "fr", "Français" },
        { "zh", "中文" }
    };

    public SettingsViewModel(IDialogService dialogService, IGameDetectionService gameDetection)
    {
        _dialogService = dialogService;
        _gameDetection = gameDetection;
        
        // Try to get localization service if available
        try
        {
            _localization = App.Services.GetService(typeof(ILocalizationService)) as ILocalizationService;
        }
        catch { }
    }

    public void Load(AppSettings settings)
    {
        GamePath = settings.GamePath ?? "";
        StoragePath = settings.StoragePath;
        AutoCheckUpdates = settings.AutoCheckUpdatesOnStartup;
        EnableVersionArchiving = settings.EnableVersionArchiving;
        MaxCacheGb = (int)(settings.MaxCacheSizeBytes / (1024 * 1024 * 1024));
        ThemeVariant = settings.ThemeVariant;
        Language = settings.Language;
        Saved = false;
    }

    public void Apply(AppSettings settings)
    {
        settings.GamePath = string.IsNullOrWhiteSpace(GamePath) ? null : GamePath;
        settings.StoragePath = StoragePath;
        settings.AutoCheckUpdatesOnStartup = AutoCheckUpdates;
        settings.EnableVersionArchiving = EnableVersionArchiving;
        settings.MaxCacheSizeBytes = (long)MaxCacheGb * 1024 * 1024 * 1024;
        settings.ThemeVariant = ThemeVariant;
        settings.Language = Language;
    }

    [RelayCommand]
    private async Task BrowseGamePathAsync()
    {
        var path = await _dialogService.PickFolderAsync(_localization!.GetString("dialog.select_game_folder_short"));
        if (path == null) return;

        if (_gameDetection.ValidateGamePath(path))
            GamePath = path;
        else
            await _dialogService.ShowMessageAsync(
                _localization.GetString("dialog.invalid_path_title"),
                _localization.GetString("dialog.invalid_path_message"));
    }

    [RelayCommand]
    private async Task DetectGamePathAsync()
    {
        var path = _gameDetection.DetectGamePath();
        if (path != null)
            GamePath = path;
        else
            await _dialogService.ShowMessageAsync(
                _localization!.GetString("dialog.game_not_found_title"),
                _localization.GetString("dialog.game_not_found_message"));
    }

    [RelayCommand]
    private async Task BrowseStoragePathAsync()
    {
        var path = await _dialogService.PickFolderAsync(_localization!.GetString("dialog.select_storage_folder"));
        if (path != null) StoragePath = path;
    }

    [RelayCommand]
    private void Save(Avalonia.Controls.Window dialog)
    {
        Saved = true;
        dialog.Close();
    }

    [RelayCommand]
    private void Cancel(Avalonia.Controls.Window dialog)
    {
        Saved = false;
        dialog.Close();
    }
}