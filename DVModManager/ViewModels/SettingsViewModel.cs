using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly IGameDetectionService _gameDetection;
    private readonly ISettingsService _settings;
    private readonly IUmmLoaderService _loaderService;
    private readonly IUmmReleaseFeedService _ummFeed;
    private readonly IStorageMoveService _storageMove;
    private readonly ILocalizationService? _localization;

    public bool Saved { get; private set; }

    // Storage path when the dialog opened; a change means the data has to move
    private string _originalStoragePath = "";

    [ObservableProperty] private string _gamePath = "";
    [ObservableProperty] private string _storagePath = "";
    [ObservableProperty] private bool _autoCheckUpdates = true;
    [ObservableProperty] private bool _enableVersionArchiving = true;
    [ObservableProperty] private int _maxCacheGb = 5;
    [ObservableProperty] private string _themeVariant = "Dark";
    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _loaderMethod = AppSettings.DefaultUmmLoaderMethod;

    // Available language options for the UI
    public List<string> AvailableLanguages { get; } = ["en", "de", "fr", "zh"];

    // UMM loader options (feature #21): injection method
    public List<string> AvailableLoaderMethods { get; } = ["Doorstop", "Assembly"];

    // Display names for languages (for the dropdown)
    public Dictionary<string, string> LanguageDisplayNames => new()
    {
        { "en", "English" },
        { "de", "Deutsch" },
        { "fr", "Français" },
        { "zh", "中文" }
    };

    public SettingsViewModel(IDialogService dialogService, IGameDetectionService gameDetection,
        ISettingsService settings, IUmmLoaderService loaderService, IUmmReleaseFeedService ummFeed,
        IStorageMoveService storageMove)
    {
        _dialogService = dialogService;
        _gameDetection = gameDetection;
        _settings = settings;
        _loaderService = loaderService;
        _ummFeed = ummFeed;
        _storageMove = storageMove;
        
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
        _originalStoragePath = settings.StoragePath;
        AutoCheckUpdates = settings.AutoCheckUpdatesOnStartup;
        EnableVersionArchiving = settings.EnableVersionArchiving;
        MaxCacheGb = (int)(settings.MaxCacheSizeBytes / (1024 * 1024 * 1024));
        ThemeVariant = settings.ThemeVariant;
        Language = settings.Language;
        LoaderMethod = AvailableLoaderMethods.Contains(settings.UmmLoaderMethod)
            ? settings.UmmLoaderMethod
            : AppSettings.DefaultUmmLoaderMethod;
        Saved = false;
        RefreshLoaderState();
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
        settings.UmmLoaderMethod = LoaderMethod;
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

    // ── UMM loader ─────────────────────────────────────────────

    private UmmLoaderStatus? _loaderStatus;

    /// <summary>Plain-text indicator shown above the loader buttons.</summary>
    [ObservableProperty] private string _loaderIndicatorInstalled = "";
    [ObservableProperty] private string _loaderIndicatorInstalledVersion = "";
    [ObservableProperty] private string _loaderIndicatorLatestVersion = "";
    [ObservableProperty] private string _loaderIndicatorMethod = "";
    [ObservableProperty] private string _loaderButtonContent = "";
    [ObservableProperty] private bool _isLoaderInstalled;

    /// <summary>Re-reads loader state from disk and refreshes the indicator.</summary>
    public void RefreshLoaderState()
    {
        var status = string.IsNullOrWhiteSpace(GamePath) ? null : _loaderService.GetStatus(GamePath);
        _loaderStatus = status;

        var yes = _localization?.GetString("dialog.button.yes") ?? "Yes";
        var no = _localization?.GetString("dialog.button.no") ?? "No";
        LoaderIndicatorInstalled = _localization?.GetString("loader.indicator.installed",
            status?.IsInstalled == true ? yes : no) ?? "";
        LoaderIndicatorInstalledVersion = _localization?.GetString("loader.indicator.installed_version",
            status?.InstalledVersion ?? "—") ?? "";
        LoaderIndicatorLatestVersion = _localization?.GetString("loader.indicator.latest_version", "—") ?? "";
        LoaderIndicatorMethod = _localization?.GetString("loader.indicator.method",
            MethodLabel(status)) ?? "";

        IsLoaderInstalled = status?.IsInstalled == true;
        UpdateLoaderButtonContent(updateAvailable: false);
        _ = CheckLoaderUpdateAsync();   // silent feed check → fills Latest Version
    }

    /// <summary>Plain (untranslated) method names, as UMM itself labels them.</summary>
    private static string MethodLabel(UmmLoaderStatus? status) => status?.ActiveMethod switch
    {
        UmmLoaderMethod.Doorstop => "Doorstop",
        UmmLoaderMethod.Assembly => "Assembly",
        _ => "—"
    };

    private void UpdateLoaderButtonContent(bool updateAvailable)
    {
        if (updateAvailable)
            LoaderButtonContent = _localization?.GetString("loader.button.update") ?? "";
        else if (_loaderStatus?.InstalledVersion != null)
            LoaderButtonContent = _localization?.GetString("loader.button.installed", _loaderStatus.InstalledVersion) ?? "";
        else
            LoaderButtonContent = _localization?.GetString("loader.button.install") ?? "";
    }

    /// <summary>
    /// Silently checks the UMM feed and fills the "Latest Version" line (and the
    /// button label) when a newer loader exists. Never pops up anything.
    /// </summary>
    private async Task CheckLoaderUpdateAsync()
    {
        try
        {
            var latest = (await _ummFeed.GetCandidatesAsync()).FirstOrDefault();
            if (latest == null) return;

            LoaderIndicatorLatestVersion = _localization?.GetString("loader.indicator.latest_version",
                latest.Version) ?? "";

            if (_loaderStatus?.InstalledVersion == null) return;
            var installed = UmmReleaseFeedService.ParseVersionOrNull(_loaderStatus.InstalledVersion);
            var newest = UmmReleaseFeedService.ParseVersionOrNull(latest.Version);
            if (installed != null && newest != null && newest > installed)
                UpdateLoaderButtonContent(updateAvailable: true);
        }
        catch { /* offline — indicator stays as-is */ }
    }

    /// <summary>Null-safe localized lookup (the localization service is optional outside the app).</summary>
    private string L(string key, params object?[] args) =>
        _localization == null ? key : _localization.GetString(key, args);

    [RelayCommand]
    private async Task InstallLoaderAsync()
    {
        if (string.IsNullOrWhiteSpace(GamePath)) return;
        if (_gameDetection.IsGameRunning())
        {
            await _dialogService.ShowMessageAsync(
                L("settings.section.loader"), L("status.game_running_locked"));
            return;
        }

        // The dropdown selection must take effect immediately: apply and persist
        // it before installing, so the install and the stored setting can never
        // diverge (Apply() only runs when the dialog is saved).
        _settings.Settings.UmmLoaderMethod = LoaderMethod;
        await _settings.SaveAsync();

        var ok = await _loaderService.InstallAsync(GamePath);
        if (!ok)
        {
            // Manual valve: offer a locally downloaded UMM package.
            var zip = await _dialogService.OpenFileAsync(
                L("dialog.import_umm_title"), L("file.filter.umm_package"), ["zip"]);
            if (zip != null)
                ok = await _loaderService.InstallFromZipAsync(zip, GamePath);
        }

        RefreshLoaderState();
        await _dialogService.ShowMessageAsync(
            L("settings.section.loader"),
            ok
                ? L("status.loader_installed",
                    _loaderStatus?.InstalledVersion ?? "?", MethodLabel(_loaderStatus))
                : L("status.loader_install_failed"));
    }

    [RelayCommand]
    private async Task UninstallLoaderAsync()
    {
        if (string.IsNullOrWhiteSpace(GamePath)) return;
        if (_gameDetection.IsGameRunning())
        {
            await _dialogService.ShowMessageAsync(
                L("settings.section.loader"), L("status.game_running_locked"));
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            L("loader.uninstall_confirm.title"), L("loader.uninstall_confirm.message"));
        if (!confirmed) return;

        var ok = await _loaderService.UninstallAsync(GamePath);
        RefreshLoaderState();
        await _dialogService.ShowMessageAsync(
            L("settings.section.loader"),
            L(ok ? "status.loader_uninstalled" : "status.loader_install_failed"));
    }

    [RelayCommand]
    private async Task SaveAsync(Avalonia.Controls.Window dialog)
    {
        // Check the new storage folder before closing, so the user can still fix it.
        // The move itself runs after the dialog closes (MainWindowViewModel.OpenSettingsAsync).
        var messageKey = _storageMove.Check(_originalStoragePath, StoragePath) switch
        {
            StorageMoveCheck.InvalidPath     => "dialog.storage_invalid_message",
            StorageMoveCheck.NotEmpty        => "dialog.storage_not_empty_message",
            StorageMoveCheck.InsideMovedData => "dialog.storage_inside_data_message",
            _                                => null
        };
        if (messageKey != null)
        {
            await _dialogService.ShowMessageAsync(L("dialog.storage_move_title"), L(messageKey, StoragePath));
            return;
        }

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