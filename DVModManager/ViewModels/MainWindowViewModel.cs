using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DVModManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // ── Services ──────────────────────────────────────────────────────────────
    private readonly ISettingsService _settings;
    private readonly IGameDetectionService _gameDetection;
    private readonly IModDiscoveryService _modDiscovery;
    private readonly IModInstallService _modInstall;
    private readonly IVersionCacheService _versionCache;
    private readonly IProfileService _profileService;
    private readonly IUpdateService _updateService;
    private readonly IDialogService _dialogService;

    // ── Observable state ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ModItemViewModel> _availableMods = [];
    [ObservableProperty] private ObservableCollection<ModItemViewModel> _activeMods = [];
    [ObservableProperty] private ModItemViewModel? _selectedMod;
    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _selectedProfileName = "Default";
    [ObservableProperty] private ObservableCollection<string> _profileNames = [];

    // ── Version history for selected mod ──────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<ModVersion> _selectedModVersionHistory = [];
    [ObservableProperty] private ModVersion? _selectedHistoryVersion;

    partial void OnSelectedModChanged(ModItemViewModel? value) =>
        _ = LoadVersionHistoryForSelectedAsync(value);

    private async Task LoadVersionHistoryForSelectedAsync(ModItemViewModel? mod)
    {
        if (mod == null) { SelectedModVersionHistory = []; return; }
        SelectedModVersionHistory = await _versionCache.GetVersionHistoryAsync(
            mod.Id, _settings.Settings.StoragePath);
    }

    // ── Filter state ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _availableFilter = "";
    [ObservableProperty] private string _activeFilter = "";
    [ObservableProperty] private ObservableCollection<ModItemViewModel> _filteredAvailableMods = [];
    [ObservableProperty] private ObservableCollection<ModItemViewModel> _filteredActiveMods = [];

    public MainWindowViewModel(
        ISettingsService settings,
        IGameDetectionService gameDetection,
        IModDiscoveryService modDiscovery,
        IModInstallService modInstall,
        IVersionCacheService versionCache,
        IProfileService profileService,
        IUpdateService updateService,
        IDialogService dialogService)
    {
        _settings = settings;
        _gameDetection = gameDetection;
        _modDiscovery = modDiscovery;
        _modInstall = modInstall;
        _versionCache = versionCache;
        _profileService = profileService;
        _updateService = updateService;
        _dialogService = dialogService;

        _gameDetection.GameRunningChanged += OnGameRunningChanged;
        _modDiscovery.ModsChanged += OnModsChangedExternally;

        IsGameRunning = _gameDetection.IsGameRunning();
    }

    partial void OnAvailableFilterChanged(string value) => ApplyFilters();
    partial void OnActiveFilterChanged(string value) => ApplyFilters();

    partial void OnSelectedProfileNameChanged(string value)
    {
        // Auto-apply when user picks an existing saved profile from the dropdown
        if (IsBusy || IsGameRunning) return;
        if (ProfileNames.Contains(value) && value != _settings.Settings.ActiveProfileName)
            _ = ApplyProfileAsync(value);
    }

    private void ApplyFilters()
    {
        static bool Matches(ModItemViewModel vm, string filter) =>
            string.IsNullOrEmpty(filter) ||
            vm.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            vm.Author.Contains(filter, StringComparison.OrdinalIgnoreCase);

        FilteredAvailableMods = new ObservableCollection<ModItemViewModel>(
            AvailableMods.Where(m => Matches(m, AvailableFilter)));
        FilteredActiveMods = new ObservableCollection<ModItemViewModel>(
            ActiveMods.Where(m => Matches(m, ActiveFilter)));
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try
        {
            await _settings.LoadAsync();
            SelectedProfileName = _settings.Settings.ActiveProfileName;

            if (string.IsNullOrEmpty(_settings.Settings.GamePath))
            {
                _settings.Settings.GamePath = _gameDetection.DetectGamePath();
                if (_settings.Settings.GamePath != null)
                {
                    StatusMessage = $"Detected game at: {_settings.Settings.GamePath}";
                    await _settings.SaveAsync();
                }
                else
                {
                    StatusMessage = "Game not found. Please set the game path in Settings.";
                    await PromptGamePathAsync();
                    return;
                }
            }

            await RefreshModsAsync();
            await RefreshProfileListAsync();

            if (_settings.Settings.AutoCheckUpdatesOnStartup)
                _ = CheckUpdatesAsync();

            _modDiscovery.StartWatching(_settings.Settings.GamePath!);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Startup error: {ex.Message}";
        }
    }

    // ── Mod operations ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task ActivateSelectedModAsync()
    {
        if (SelectedMod == null || _settings.Settings.GamePath == null) return;

        // Capture before any await — ApplyFilters can null SelectedMod
        var target = SelectedMod;
        var displayName = target.DisplayName;

        // Try to auto-activate any inactive dependencies first
        var trulyMissing = await AutoActivateDependenciesAsync(target);
        if (trulyMissing.Count > 0)
        {
            target.State = ModState.MissingDependency;
            target.HasMissingDependency = true;
            StatusMessage = $"Missing dependencies for {displayName}: {string.Join(", ", trulyMissing)}";
            return;
        }

        SetBusy($"Activating {displayName}...");
        var success = await _modInstall.ActivateModAsync(target.ModInfo, _settings.Settings.GamePath);
        ClearBusy();

        if (success)
        {
            target.SyncFromModel();
            MoveToActive(target);
            StatusMessage = $"Activated: {displayName}";
        }
        else
        {
            StatusMessage = $"Failed to activate: {displayName}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeactivateSelectedModAsync()
    {
        if (SelectedMod == null || _settings.Settings.GamePath == null) return;

        SetBusy($"Deactivating {SelectedMod.DisplayName}...");
        var success = await _modInstall.DeactivateModAsync(SelectedMod.ModInfo, _settings.Settings.GamePath);
        ClearBusy();

        var displayName = SelectedMod.DisplayName;
        if (success)
        {
            SelectedMod.SyncFromModel();
            MoveToInactive(SelectedMod);
            StatusMessage = $"Deactivated: {displayName}";
        }
        else
        {
            StatusMessage = $"Failed to deactivate: {displayName}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task InstallModFromFileAsync()
    {
        var path = await _dialogService.OpenFileAsync("Install Mod Archive", "Mod Archives", ["zip"]);
        if (path == null || _settings.Settings.GamePath == null) return;

        SetBusy("Installing mod...");
        var mod = await _modInstall.InstallFromArchiveAsync(
            path, _settings.Settings.GamePath, _settings.Settings.StoragePath);
        ClearBusy();

        if (mod != null)
        {
            await RefreshModsAsync();
            StatusMessage = $"Installed: {mod.EffectiveDisplayName}";
        }
        else
        {
            StatusMessage = "Install failed. Ensure the archive contains Info.json.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task UninstallSelectedModAsync()
    {
        if (SelectedMod == null || _settings.Settings.GamePath == null) return;

        var displayName = SelectedMod.DisplayName;
        var confirmed = await _dialogService.ConfirmAsync(
            "Uninstall Mod",
            $"Remove '{displayName}'? The current version will be archived for rollback, then deleted.");

        if (!confirmed) return;

        SetBusy($"Uninstalling {displayName}...");
        var success = await _modInstall.UninstallModAsync(
            SelectedMod.ModInfo, _settings.Settings.GamePath, _settings.Settings.StoragePath, hardDelete: false);
        ClearBusy();

        if (success)
        {
            await RefreshModsAsync();
            StatusMessage = $"Uninstalled: {displayName}";
        }
    }

    // ── Updates ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        var allMods = AvailableMods.Concat(ActiveMods).Select(v => v.ModInfo).ToList();
        if (allMods.Count == 0) return;

        SetBusy("Checking for updates...");
        var updates = await _updateService.CheckAllUpdatesAsync(allMods);
        ClearBusy();

        foreach (var update in updates)
        {
            var vm = AvailableMods.Concat(ActiveMods).FirstOrDefault(m => m.Id == update.ModId);
            vm?.ApplyUpdate(update);
        }

        StatusMessage = updates.Count > 0
            ? $"{updates.Count} update(s) available."
            : "All mods are up to date.";
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task UpdateSelectedModAsync()
    {
        if (SelectedMod?.ModInfo.PendingUpdate == null || _settings.Settings.GamePath == null) return;

        var displayName = SelectedMod.DisplayName;
        var update = SelectedMod.ModInfo.PendingUpdate;

        // Nexus mods (and GitHub releases without a direct asset URL) require manual download
        bool canAutoDownload = update.Source == "github" && !string.IsNullOrEmpty(update.DownloadUrl);
        if (!canAutoDownload)
        {
            var url = update.ChangelogUrl ?? update.DownloadUrl;
            if (!string.IsNullOrEmpty(url))
                Helpers.PlatformHelper.Open(url);
            StatusMessage = $"Opened mod page for {displayName} v{update.LatestVersion}";
            return;
        }

        SetBusy($"Updating {displayName} to v{update.LatestVersion}...");
        var progress = new Progress<double>(p =>
            BusyMessage = $"Updating {displayName}… {p:P0}");
        var success = await _modInstall.UpdateModAsync(
            SelectedMod.ModInfo, update, _settings.Settings.GamePath, _settings.Settings.StoragePath, progress);
        ClearBusy();

        if (success)
        {
            await RefreshModsAsync();
            StatusMessage = $"Updated {displayName} to v{update.LatestVersion}";
        }
        else
        {
            StatusMessage = $"Update failed for {displayName}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task UpdateAllModsAsync()
    {
        var modsWithUpdates = AvailableMods.Concat(ActiveMods)
            .Where(m => m.HasUpdate
                && m.ModInfo.PendingUpdate?.Source == "github"
                && !string.IsNullOrEmpty(m.ModInfo.PendingUpdate?.DownloadUrl))
            .ToList();

        if (modsWithUpdates.Count == 0)
        {
            StatusMessage = "No downloadable updates available.";
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync("Update All",
            $"Update {modsWithUpdates.Count} mod(s)? Current versions will be archived.");
        if (!confirmed) return;

        // Backup first
        if (_settings.Settings.GamePath != null && _settings.Settings.BackupBeforeChanges)
        {
            SetBusy("Creating backup...");
            await _modInstall.BackupModsFolderAsync(_settings.Settings.GamePath, _settings.Settings.StoragePath);
        }

        int updated = 0;
        foreach (var mod in modsWithUpdates)
        {
            var modName = mod.DisplayName;
            SetBusy($"Updating {modName}... ({updated + 1}/{modsWithUpdates.Count})");
            var progress = new Progress<double>(p =>
                BusyMessage = $"Updating {modName}… {p:P0} ({updated + 1}/{modsWithUpdates.Count})");
            var success = await _modInstall.UpdateModAsync(
                mod.ModInfo, mod.ModInfo.PendingUpdate!, _settings.Settings.GamePath!, _settings.Settings.StoragePath, progress);
            if (success) updated++;
        }

        ClearBusy();
        await RefreshModsAsync();
        StatusMessage = $"Updated {updated}/{modsWithUpdates.Count} mods.";
    }

    // ── Rollback ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task RollbackSelectedModAsync(string version)
    {
        if (SelectedMod == null || _settings.Settings.GamePath == null) return;

        var displayName = SelectedMod.DisplayName;
        var modId = SelectedMod.Id;
        SetBusy($"Rolling back {displayName} to v{version}...");
        var success = await _modInstall.RollbackToVersionAsync(
            modId, version, _settings.Settings.GamePath, _settings.Settings.StoragePath);
        ClearBusy();

        if (success)
        {
            await RefreshModsAsync();
            StatusMessage = $"Rolled back {displayName} to v{version}";
        }
        else
        {
            StatusMessage = $"Rollback failed for {displayName}";
        }
    }

    // ── Profiles ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveCurrentProfileAsync()
    {
        var allMods = AvailableMods.Concat(ActiveMods).Select(v => v.ModInfo).ToList();
        var profile = new ModProfile
        {
            Name = SelectedProfileName,
            Mods = allMods.Select(m => new ProfileModEntry
            {
                ModId = m.Id,
                Version = m.Version,
                IsActive = m.IsActive
            }).ToList()
        };

        await _profileService.SaveProfileAsync(profile, _settings.Settings.ProfilesPath);
        _settings.Settings.ActiveProfileName = SelectedProfileName;
        await _settings.SaveAsync();
        await RefreshProfileListAsync();
        StatusMessage = $"Profile '{SelectedProfileName}' saved.";
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeactivateAllModsAsync()
    {
        if (_settings.Settings.GamePath == null) return;
        var active = ActiveMods.ToList();
        if (active.Count == 0) { StatusMessage = "No active mods."; return; }

        var confirmed = await _dialogService.ConfirmAsync("Unload All Mods",
            $"Deactivate all {active.Count} active mod(s)?");
        if (!confirmed) return;

        SetBusy("Unloading all mods...");
        int done = 0;
        foreach (var vm in active)
        {
            BusyMessage = $"Deactivating {vm.DisplayName}... ({done + 1}/{active.Count})";
            var ok = await _modInstall.DeactivateModAsync(vm.ModInfo, _settings.Settings.GamePath!);
            if (ok) done++;
        }
        ClearBusy();
        await RefreshModsAsync();
        StatusMessage = $"Deactivated {done}/{active.Count} mods.";
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task ApplyProfileAsync(string profileName)
    {
        if (_settings.Settings.GamePath == null) return;

        var profile = await _profileService.GetProfileAsync(profileName, _settings.Settings.ProfilesPath);
        if (profile == null) return;

        var allMods = AvailableMods.Concat(ActiveMods).Select(v => v.ModInfo).ToList();
        var diff = _profileService.ComputeDiff(profile, allMods);

        if (diff.ToActivate.Count == 0 && diff.ToDeactivate.Count == 0 && diff.ToRollback.Count == 0)
        {
            StatusMessage = "Profile is already applied.";
            return;
        }

        // Backup before bulk change
        if (_settings.Settings.BackupBeforeChanges)
        {
            SetBusy("Creating backup...");
            await _modInstall.BackupModsFolderAsync(_settings.Settings.GamePath, _settings.Settings.StoragePath);
        }

        // Apply changes
        foreach (var id in diff.ToDeactivate)
        {
            var mod = allMods.FirstOrDefault(m => m.Id == id);
            if (mod != null && mod.IsActive)
                await _modInstall.DeactivateModAsync(mod, _settings.Settings.GamePath);
        }

        foreach (var (modId, _, toVersion) in diff.ToRollback)
        {
            await _modInstall.RollbackToVersionAsync(
                modId, toVersion, _settings.Settings.GamePath, _settings.Settings.StoragePath);
        }

        foreach (var id in diff.ToActivate)
        {
            var mod = allMods.FirstOrDefault(m => m.Id == id);
            if (mod != null && !mod.IsActive)
                await _modInstall.ActivateModAsync(mod, _settings.Settings.GamePath);
        }

        ClearBusy();
        // Set ActiveProfileName FIRST so OnSelectedProfileNameChanged sees
        // value == ActiveProfileName and does not re-enter ApplyProfileAsync.
        _settings.Settings.ActiveProfileName = profileName;
        SelectedProfileName = profileName;
        await _settings.SaveAsync();
        await RefreshModsAsync();
        StatusMessage = $"Applied profile '{profileName}'.";
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        if (_settings.Settings.GamePath == null)
        {
            StatusMessage = "Game path not set. Open Settings to configure.";
            return;
        }

        var modsDir = Path.Combine(_settings.Settings.GamePath, "Mods");
        if (!Directory.Exists(modsDir))
        {
            StatusMessage = $"Mods folder not found: {modsDir}";
            return;
        }

        IReadOnlyList<DVModManager.Models.ModInfo> mods;
        try
        {
            mods = await _modDiscovery.ScanAllModsAsync(_settings.Settings.GamePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableMods.Clear();
            ActiveMods.Clear();

            foreach (var mod in mods.OrderBy(m => m.EffectiveDisplayName))
            {
                var vm = new ModItemViewModel(mod);
                if (mod.IsActive) ActiveMods.Add(vm);
                else AvailableMods.Add(vm);
            }

            ApplyFilters();

            var active = mods.Count(m => m.IsActive);
            var inactive = mods.Count(m => !m.IsActive);
            StatusMessage = mods.Count == 0
                ? $"No mods found in {modsDir}"
                : $"Found {mods.Count} mod(s) — {active} active, {inactive} inactive.";
        });
    }

    private async Task RefreshProfileListAsync()
    {
        var profiles = await _profileService.GetProfilesAsync(_settings.Settings.ProfilesPath);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ProfileNames.Clear();
            if (!profiles.Any(p => p.Name == "Default"))
                ProfileNames.Add("Default");
            foreach (var p in profiles) ProfileNames.Add(p.Name);
        });
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var vm = App.Services.GetRequiredService<SettingsViewModel>();
        vm.Load(_settings.Settings);
        var dialog = new DVModManager.Views.SettingsDialog { DataContext = vm };
        await dialog.ShowDialog(GetMainWindow());
        if (vm.Saved)
        {
            var previousGamePath = _settings.Settings.GamePath;
            vm.Apply(_settings.Settings);             // copy UI values → AppSettings
            await _settings.SaveAsync();
            await RefreshModsAsync();

            // Restart file watchers if the game path changed
            if (_settings.Settings.GamePath != null &&
                _settings.Settings.GamePath != previousGamePath)
            {
                _modDiscovery.StartWatching(_settings.Settings.GamePath);
            }
        }
    }

    [RelayCommand]
    private async Task OpenProfilesAsync()
    {
        var vm = App.Services.GetRequiredService<ProfileViewModel>();
        await vm.LoadProfilesAsync(_settings.Settings.ProfilesPath);
        var dialog = new DVModManager.Views.ProfileDialog { DataContext = vm };
        await dialog.ShowDialog(GetMainWindow());
        if (vm.Applied && vm.AppliedProfileName != null)
            await ApplyProfileAsync(vm.AppliedProfileName);
    }

    // ── Version history for selected mod ─────────────────────────────────────

    public async Task<IReadOnlyList<ModVersion>> GetVersionHistoryForSelectedAsync()
    {
        if (SelectedMod == null) return [];
        return await _versionCache.GetVersionHistoryAsync(SelectedMod.Id, _settings.Settings.StoragePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool CanModify() => !IsGameRunning && !IsBusy;

    private void OnGameRunningChanged(object? sender, bool running)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsGameRunning = running;
            StatusMessage = running ? "Game is running. Mod changes are locked." : "Game stopped. Mod changes unlocked.";
            ActivateSelectedModCommand.NotifyCanExecuteChanged();
            DeactivateSelectedModCommand.NotifyCanExecuteChanged();
            InstallModFromFileCommand.NotifyCanExecuteChanged();
            UninstallSelectedModCommand.NotifyCanExecuteChanged();
            UpdateSelectedModCommand.NotifyCanExecuteChanged();
            UpdateAllModsCommand.NotifyCanExecuteChanged();
            ApplyProfileCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnModsChangedExternally(object? sender, EventArgs e)
    {
        // Skip if a mod operation is already in progress — it will refresh itself when done
        if (IsBusy) return;
        Dispatcher.UIThread.Post(async () => await RefreshModsAsync());
    }

    private void MoveToActive(ModItemViewModel vm)
    {
        AvailableMods.Remove(vm);
        ActiveMods.Add(vm);
        ApplyFilters();
    }

    private void MoveToInactive(ModItemViewModel vm)
    {
        ActiveMods.Remove(vm);
        AvailableMods.Add(vm);
        ApplyFilters();
    }

    /// <summary>
    /// For each required dependency: if it is already active → skip.
    /// If it is inactive → activate it automatically.
    /// Returns the list of dependency IDs that could not be found at all.
    /// </summary>
    private async Task<List<string>> AutoActivateDependenciesAsync(ModItemViewModel vm)
    {
        if (_settings.Settings.GamePath == null) return [];

        var activeIds = ActiveMods.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Use last-write-wins to handle any duplicates (e.g. mods with empty Id)
        var inactiveMap = new Dictionary<string, ModItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in AvailableMods)
            if (!string.IsNullOrEmpty(m.Id))
                inactiveMap[m.Id] = m;

        var trulyMissing = new List<string>();
        var activated    = new List<ModItemViewModel>();

        foreach (var req in vm.Requirements)
        {
            var depId = req.Split('-')[0]; // strip optional version suffix
            if (string.IsNullOrEmpty(depId) || activeIds.Contains(depId)) continue;

            if (inactiveMap.TryGetValue(depId, out var depVm))
            {
                SetBusy($"Activating dependency {depVm.DisplayName}...");
                var ok = await _modInstall.ActivateModAsync(depVm.ModInfo, _settings.Settings.GamePath);
                ClearBusy();
                if (ok)
                {
                    depVm.SyncFromModel();
                    activated.Add(depVm);
                    activeIds.Add(depId);
                }
                else
                {
                    trulyMissing.Add(depId);
                }
            }
            else
            {
                trulyMissing.Add(depId);
            }
        }

        // Move all activated dependencies in one pass (already on UI thread)
        foreach (var dep in activated)
        {
            AvailableMods.Remove(dep);
            ActiveMods.Add(dep);
        }
        if (activated.Count > 0)
            ApplyFilters();

        return trulyMissing;
    }

    private bool ValidateDependencies(ModItemViewModel vm)
    {
        var allActive = ActiveMods.Select(m => m.Id).ToHashSet();
        var missing = vm.Requirements
            .Where(r => !allActive.Contains(r.Split('-')[0]))
            .ToList();

        if (missing.Count == 0) return true;

        vm.State = ModState.MissingDependency;
        vm.HasMissingDependency = true;
        StatusMessage = $"Missing dependencies for {vm.DisplayName}: {string.Join(", ", missing)}";
        return false;
    }

    private async Task PromptGamePathAsync()
    {
        var path = await _dialogService.PickFolderAsync("Select Derail Valley installation folder");
        if (path == null || !_gameDetection.ValidateGamePath(path))
        {
            await _dialogService.ShowMessageAsync("Error", "Invalid game path — could not find DerailValley_Data folder.");
            return;
        }
        _settings.Settings.GamePath = path;
        await _settings.SaveAsync();
        await RefreshModsAsync();
    }

    private void SetBusy(string message)
    {
        IsBusy = true;
        BusyMessage = message;
        StatusMessage = message;
        ActivateSelectedModCommand.NotifyCanExecuteChanged();
        DeactivateSelectedModCommand.NotifyCanExecuteChanged();
    }

    private void ClearBusy()
    {
        IsBusy = false;
        BusyMessage = "";
        ActivateSelectedModCommand.NotifyCanExecuteChanged();
        DeactivateSelectedModCommand.NotifyCanExecuteChanged();
    }

    private static Avalonia.Controls.Window GetMainWindow()
    {
        if (App.Services.GetService(typeof(DVModManager.Views.MainWindow)) is DVModManager.Views.MainWindow w) return w;
        return (Avalonia.Controls.Window)Avalonia.Application.Current!
            .ApplicationLifetime.Cast<Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>()!
            .MainWindow!;
    }
}

// Extension for null-safe cast
file static class NullExtensions
{
    public static T Cast<T>(this object? o) where T : class =>
        o as T ?? throw new InvalidCastException($"Cannot cast to {typeof(T).Name}");
}
