using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly ILocalizationService _localization;

    // ── Observable state ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ModItemViewModel> _availableMods = [];
    [ObservableProperty] private ObservableCollection<ModItemViewModel> _activeMods = [];
    [ObservableProperty] private ModItemViewModel? _selectedMod;
    [ObservableProperty] private ModGroupHeaderViewModel? _selectedGroup;
    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _selectedProfileName = "Default";
    [ObservableProperty] private ObservableCollection<string> _profileNames = [];
    [ObservableProperty] private string _windowTitle = "DV Mod Manager";
    [ObservableProperty] private string _panelAvailableHeader = "Available Mods";
    [ObservableProperty] private string _panelActiveHeader = "Active Mods";

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
    // Mixed list: ModGroupHeaderViewModel | ModItemViewModel
    [ObservableProperty] private ObservableCollection<object> _filteredAvailableMods = [];
    [ObservableProperty] private ObservableCollection<object> _filteredActiveMods = [];

    public MainWindowViewModel(
        ISettingsService settings,
        IGameDetectionService gameDetection,
        IModDiscoveryService modDiscovery,
        IModInstallService modInstall,
        IVersionCacheService versionCache,
        IProfileService profileService,
        IUpdateService updateService,
        IDialogService dialogService,
        ILocalizationService localization)
    {
        _settings = settings;
        _gameDetection = gameDetection;
        _modDiscovery = modDiscovery;
        _modInstall = modInstall;
        _versionCache = versionCache;
        _profileService = profileService;
        _updateService = updateService;
        _dialogService = dialogService;
        _localization = localization;

        _gameDetection.GameRunningChanged += OnGameRunningChanged;
        _modDiscovery.ModsChanged += OnModsChangedExternally;
        _localization.LanguageChanged += (_, _) => UpdateLocalizedStrings();

        IsGameRunning = _gameDetection.IsGameRunning();
        UpdateLocalizedStrings();
    }

    partial void OnAvailableFilterChanged(string value) => ApplyGroupedFilters();
    partial void OnActiveFilterChanged(string value) => ApplyGroupedFilters();

    partial void OnSelectedProfileNameChanged(string value)
    {
        // Auto-apply when user picks an existing saved profile from the dropdown
        if (IsBusy || IsGameRunning) return;
        if (ProfileNames.Contains(value) && value != _settings.Settings.ActiveProfileName)
            _ = ApplyProfileAsync(value);
    }

    private void ApplyFilters() => ApplyGroupedFilters(); // kept for call-sites not yet updated

    private void ApplyGroupedFilters()
    {
        static bool Matches(ModItemViewModel vm, string filter) =>
            string.IsNullOrEmpty(filter) ||
            vm.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            vm.Author.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var groups   = _settings.Settings.ModGroups;
        var collapsedAvailable = _settings.Settings.CollapsedGroupIdsAvailable;
        var collapsedActive    = _settings.Settings.CollapsedGroupIdsActive;

        // Index mods by ID for quick lookup
        var availableById = new Dictionary<string, ModItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in AvailableMods) if (!string.IsNullOrEmpty(m.Id)) availableById[m.Id] = m;
        var activeById = new Dictionary<string, ModItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in ActiveMods) if (!string.IsNullOrEmpty(m.Id)) activeById[m.Id] = m;

        var newAvailable = new ObservableCollection<object>();
        var newActive    = new ObservableCollection<object>();

        // Track which mods have been placed into a group
        var groupedAvailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupedActive    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            bool isCollapsedAvail = collapsedAvailable.Contains(group.Id);

            // ---- Available panel ----
            var availItems = new List<ModItemViewModel>();
            foreach (var modId in group.ModIds)
            {
                if (availableById.TryGetValue(modId, out var vm))
                {
                    if (Matches(vm, AvailableFilter))
                    {
                        vm.GroupId = group.Id;
                        availItems.Add(vm);
                        groupedAvailable.Add(modId);
                    }
                }
                else if (!activeById.ContainsKey(modId))
                {
                    // On disk in neither panel — ghost it in Available
                    var ghost = ModItemViewModel.CreateMissing(modId);
                    ghost.GroupId = group.Id;
                    if (Matches(ghost, AvailableFilter))
                    {
                        availItems.Add(ghost);
                        groupedAvailable.Add(modId);
                    }
                }
            }
            if (availItems.Count > 0 || group.ModIds.Count == 0)
            {
                // Always show empty groups in the Available panel so new groups are visible
                newAvailable.Add(BuildHeader(group, availItems.Count, isCollapsedAvail, "available"));
                if (!isCollapsedAvail)
                    foreach (var item in availItems) newAvailable.Add(item);
            }

            // ---- Active panel ----
            bool isCollapsedActive = collapsedActive.Contains(group.Id);
            var activeItems = new List<ModItemViewModel>();
            foreach (var modId in group.ModIds)
            {
                if (activeById.TryGetValue(modId, out var vm))
                {
                    if (Matches(vm, ActiveFilter))
                    {
                        vm.GroupId = group.Id;
                        activeItems.Add(vm);
                        groupedActive.Add(modId);
                    }
                }
            }
            if (activeItems.Count > 0)
            {
                newActive.Add(BuildHeader(group, activeItems.Count, isCollapsedActive, "active"));
                if (!isCollapsedActive)
                    foreach (var item in activeItems) newActive.Add(item);
            }
        }

        // Ungrouped mods at the bottom
        foreach (var vm in AvailableMods)
        {
            if (!groupedAvailable.Contains(vm.Id) && Matches(vm, AvailableFilter))
            {
                vm.GroupId = null;
                newAvailable.Add(vm);
            }
        }
        foreach (var vm in ActiveMods)
        {
            if (!groupedActive.Contains(vm.Id) && Matches(vm, ActiveFilter))
            {
                vm.GroupId = null;
                newActive.Add(vm);
            }
        }

        FilteredAvailableMods = newAvailable;
        FilteredActiveMods    = newActive;
    }

    private ModGroupHeaderViewModel BuildHeader(ModGroup group, int count, bool isCollapsed, string panel) =>
        new(
            group.Id, group.Name, count, isCollapsed, panel,
            onRename: RenameGroupAsync,
            onDelete: DeleteGroupAsync,
            onToggle: ToggleGroupCollapseAsync);

    // ── Group operations ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        var group = new ModGroup { Id = Guid.NewGuid().ToString(), Name = "New Group" };
        _settings.Settings.ModGroups.Add(group);
        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    private async Task RenameGroupAsync(string groupId, string newName)
    {
        var group = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;
        group.Name = newName;
        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    private async Task DeleteGroupAsync(string groupId)
    {
        var group = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;
        // Clear GroupId on all mods that were in this group
        foreach (var vm in AvailableMods.Concat(ActiveMods))
            if (vm.GroupId == groupId) vm.GroupId = null;
        _settings.Settings.ModGroups.Remove(group);
        _settings.Settings.CollapsedGroupIdsAvailable.Remove(groupId);
        _settings.Settings.CollapsedGroupIdsActive.Remove(groupId);
        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    private async Task ToggleGroupCollapseAsync(string groupId, bool isCollapsed, string panel)
    {
        var set = panel == "active"
            ? _settings.Settings.CollapsedGroupIdsActive
            : _settings.Settings.CollapsedGroupIdsAvailable;

        if (isCollapsed) set.Add(groupId);
        else             set.Remove(groupId);

        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    public async Task AssignModToGroupAsync(string modId, string? groupId)
    {
        // Remove from any existing group
        foreach (var g in _settings.Settings.ModGroups)
            g.ModIds.Remove(modId);

        // Add to new group if specified
        if (groupId != null)
        {
            var target = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == groupId);
            if (target != null && !target.ModIds.Contains(modId))
                target.ModIds.Add(modId);
        }

        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    public async Task AssignModsToGroupAsync(IEnumerable<string> modIds, string? groupId)
    {
        foreach (var modId in modIds)
        {
            foreach (var g in _settings.Settings.ModGroups)
                g.ModIds.Remove(modId);

            if (groupId != null)
            {
                var target = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == groupId);
                if (target != null && !target.ModIds.Contains(modId))
                    target.ModIds.Add(modId);
            }
        }

        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    public async Task RemoveMissingModFromGroupAsync(string modId)
    {
        foreach (var g in _settings.Settings.ModGroups)
            g.ModIds.Remove(modId);
        await _settings.SaveAsync();
        ApplyGroupedFilters();
    }

    // ── Startup ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try
        {
            await _settings.LoadAsync();
            
            // Apply language from persisted settings
            if (!string.IsNullOrEmpty(_settings.Settings.Language))
                _localization.SetLanguage(_settings.Settings.Language);
            
            SelectedProfileName = _settings.Settings.ActiveProfileName;
            UpdateLocalizedStrings();

            if (string.IsNullOrEmpty(_settings.Settings.GamePath))
            {
                _settings.Settings.GamePath = _gameDetection.DetectGamePath();
                if (_settings.Settings.GamePath != null)
                {
                    StatusMessage = _localization.GetString("status.detected_game", _settings.Settings.GamePath);
                    await _settings.SaveAsync();
                }
                else
                {
                    StatusMessage = _localization.GetString("status.game_path_not_set");
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
            StatusMessage = _localization.GetString("status.startup_error", ex.Message);
        }
    }

    // ── Mod operations ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task ActivateSelectedModAsync()
    {
        if (_settings.Settings.GamePath == null) return;

        // Multiple mods checked — activate all checked available mods
        var checkedTargets = AvailableMods.Where(m => m.IsChecked).ToList();
        if (checkedTargets.Count >= 2)
        {
            SetBusy($"Activating {checkedTargets.Count} mod(s)...");
            int activated = 0;
            var missingDeps = new List<string>();
            foreach (var target in checkedTargets)
            {
                var missing = await AutoActivateDependenciesAsync(target);
                if (missing.Count > 0)
                {
                    target.State = ModState.MissingDependency;
                    target.HasMissingDependency = true;
                    missingDeps.Add($"{target.DisplayName} (missing: {string.Join(", ", missing)})");
                    continue;
                }
                var success = await _modInstall.ActivateModAsync(target.ModInfo, _settings.Settings.GamePath);
                if (success) { target.SyncFromModel(); MoveToActive(target); activated++; }
            }
            ClearBusy();
            StatusMessage = missingDeps.Count > 0
                ? $"Activated {activated}/{checkedTargets.Count} — missing deps: {string.Join("; ", missingDeps)}"
                : $"Activated {activated}/{checkedTargets.Count} mod(s).";
            return;
        }

        // Group selected — activate all available mods in the group
        if (SelectedGroup != null)
        {
            var group = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == SelectedGroup.GroupId);
            if (group == null) return;
            var targets = AvailableMods.Where(m => group.ModIds.Contains(m.Id)).ToList();
            if (targets.Count == 0) return;
            SetBusy($"Activating {targets.Count} mod(s)...");
            int activated = 0;
            var missingDeps = new List<string>();
            foreach (var target in targets)
            {
                var missing = await AutoActivateDependenciesAsync(target);
                if (missing.Count > 0)
                {
                    target.State = ModState.MissingDependency;
                    target.HasMissingDependency = true;
                    missingDeps.Add($"{target.DisplayName} (missing: {string.Join(", ", missing)})");
                    continue;
                }
                var success = await _modInstall.ActivateModAsync(target.ModInfo, _settings.Settings.GamePath);
                if (success) { target.SyncFromModel(); MoveToActive(target); activated++; }
            }
            ClearBusy();
            StatusMessage = missingDeps.Count > 0
                ? $"Activated {activated}/{targets.Count} — missing deps: {string.Join("; ", missingDeps)}"
                : $"Activated {activated}/{targets.Count} mod(s) in group '{group.Name}'.";
            return;
        }

        if (SelectedMod == null) return;

        // Capture before any await — ApplyFilters can null SelectedMod
        var mod = SelectedMod;
        var displayName = mod.DisplayName;

        // Try to auto-activate any inactive dependencies first
        var trulyMissing = await AutoActivateDependenciesAsync(mod);
        if (trulyMissing.Count > 0)
        {
            mod.State = ModState.MissingDependency;
            mod.HasMissingDependency = true;
            StatusMessage = $"Missing dependencies for {displayName}: {string.Join(", ", trulyMissing)}";
            return;
        }

        SetBusy($"Activating {displayName}...");
        var ok = await _modInstall.ActivateModAsync(mod.ModInfo, _settings.Settings.GamePath);
        ClearBusy();

        if (ok)
        {
            mod.SyncFromModel();
            MoveToActive(mod);
            StatusMessage = _localization.GetString("status.activated", displayName);
        }
        else
        {
            StatusMessage = _localization.GetString("status.activation_failed", displayName);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeactivateSelectedModAsync()
    {
        if (_settings.Settings.GamePath == null) return;

        // Multiple mods checked — deactivate all checked active mods
        var checkedTargets = ActiveMods.Where(m => m.IsChecked).ToList();
        if (checkedTargets.Count >= 2)
        {
            SetBusy($"Deactivating {checkedTargets.Count} mod(s)...");
            int deactivated = 0;
            foreach (var target in checkedTargets)
            {
                var success = await _modInstall.DeactivateModAsync(target.ModInfo, _settings.Settings.GamePath);
                if (success) { target.SyncFromModel(); MoveToInactive(target); deactivated++; }
            }
            ClearBusy();
            StatusMessage = $"Deactivated {deactivated}/{checkedTargets.Count} mod(s).";
            return;
        }

        // Group selected — deactivate all active mods in the group
        if (SelectedGroup != null)
        {
            var group = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == SelectedGroup.GroupId);
            if (group == null) return;
            var targets = ActiveMods.Where(m => group.ModIds.Contains(m.Id)).ToList();
            if (targets.Count == 0) return;
            SetBusy($"Deactivating {targets.Count} mod(s)...");
            int deactivated = 0;
            foreach (var target in targets)
            {
                var success = await _modInstall.DeactivateModAsync(target.ModInfo, _settings.Settings.GamePath);
                if (success) { target.SyncFromModel(); MoveToInactive(target); deactivated++; }
            }
            ClearBusy();
            StatusMessage = _localization.GetString("status.deactivated_count", deactivated, targets.Count, group.Name);
            return;
        }

        if (SelectedMod == null) return;

        SetBusy($"Deactivating {SelectedMod.DisplayName}...");
        var success2 = await _modInstall.DeactivateModAsync(SelectedMod.ModInfo, _settings.Settings.GamePath);
        ClearBusy();

        var dn = SelectedMod.DisplayName;
        if (success2)
        {
            SelectedMod.SyncFromModel();
            MoveToInactive(SelectedMod);
            StatusMessage = _localization.GetString("status.deactivated", dn);
        }
        else
        {
            StatusMessage = _localization.GetString("status.deactivation_failed", dn);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task InstallModFromFileAsync()
    {
        var path = await _dialogService.OpenFileAsync(
            _localization.GetString("install.button.tooltip"),
            "Mod Archives & Profiles", ["zip", "json"]);
        if (path == null || _settings.Settings.GamePath == null) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();

        // ── JSON profile import ────────────────────────────────────────────
        if (ext == ".json")
        {
            try
            {
                var profile = await _profileService.ImportProfileAsync(path);
                var uniqueName = await _profileService.GetUniqueProfileNameAsync(
                    profile.Name, _settings.Settings.ProfilesPath);
                profile.Name = uniqueName;
                await _profileService.SaveProfileAsync(profile, _settings.Settings.ProfilesPath);
                await RefreshProfileListAsync();
                StatusMessage = _localization.GetString("status.modpack_imported", uniqueName);
            }
            catch
            {
                StatusMessage = _localization.GetString("status.modpack_import_failed");
            }
            return;
        }

        // ── ZIP: check for profile.json inside → modpack import ───────────
        ModProfile? embeddedProfile = null;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var profileEntry = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "profile.json", StringComparison.OrdinalIgnoreCase));
            if (profileEntry != null)
            {
                using var sr = new System.IO.StreamReader(profileEntry.Open());
                var json = await sr.ReadToEndAsync();
                embeddedProfile = System.Text.Json.JsonSerializer.Deserialize<ModProfile>(json);
            }
        }
        catch { /* not a valid zip or no profile — fall through to normal install */ }

        if (embeddedProfile != null)
        {
            var confirmed = await _dialogService.ShowModpackImportConfirmAsync(
                _localization.GetString("import.modpack.title"), embeddedProfile);
            if (!confirmed) return;

            SetBusy(_localization.GetString("import.modpack.title") + "…");
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "dvmm_modpack_" + Guid.NewGuid());
                try
                {
                    await Task.Run(() =>
                        System.IO.Compression.ZipFile.ExtractToDirectory(path, tempDir, overwriteFiles: true));

                    var modsDir = Path.Combine(tempDir, "mods");
                    if (Directory.Exists(modsDir))
                    {
                        foreach (var modFolder in Directory.GetDirectories(modsDir))
                        {
                            await _modInstall.InstallFromFolderAsync(
                                modFolder,
                                _settings.Settings.GamePath,
                                _settings.Settings.StoragePath,
                                activate: false);
                        }
                    }

                    var uniqueName = await _profileService.GetUniqueProfileNameAsync(
                        embeddedProfile.Name, _settings.Settings.ProfilesPath);
                    embeddedProfile.Name = uniqueName;
                    await _profileService.SaveProfileAsync(embeddedProfile, _settings.Settings.ProfilesPath);
                    await RefreshModsAsync();
                    await RefreshProfileListAsync();
                    StatusMessage = _localization.GetString("status.modpack_imported", uniqueName);
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch
            {
                ClearBusy();
                StatusMessage = _localization.GetString("status.modpack_import_failed");
            }
            ClearBusy();
            return;
        }

        // ── Regular mod archive install ────────────────────────────────────
        SetBusy("Installing mod...");
        var mod = await _modInstall.InstallFromArchiveAsync(
            path, _settings.Settings.GamePath, _settings.Settings.StoragePath);
        ClearBusy();

        if (mod != null)
        {
            await RefreshModsAsync();
            StatusMessage = _localization.GetString("status.install_success", mod.EffectiveDisplayName);
        }
        else
        {
            StatusMessage = _localization.GetString("status.install_failed");
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
            StatusMessage = _localization.GetString("status.uninstalled", displayName);
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
            ? _localization.GetString("status.updates_available", updates.Count)
            : _localization.GetString("status.all_up_to_date");
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
            StatusMessage = _localization.GetString("status.update_opened", displayName, update.LatestVersion);
            return;
        }

        SetBusy($"Updating {displayName} to v{update.LatestVersion}...");
        var progress = new Progress<double>(p =>
            BusyMessage = _localization.GetString("busy.update_progress", displayName, p));
        var success = await _modInstall.UpdateModAsync(
            SelectedMod.ModInfo, update, _settings.Settings.GamePath, _settings.Settings.StoragePath, progress);
        ClearBusy();

        if (success)
        {
            await RefreshModsAsync();
            StatusMessage = _localization.GetString("status.updated", displayName, update.LatestVersion);
        }
        else
        {
            StatusMessage = _localization.GetString("status.update_failed", displayName);
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
            StatusMessage = _localization.GetString("status.no_downloadable_updates");
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
        var activeMods = ActiveMods.Select(v => v.ModInfo).ToList();
        var profile = new ModProfile
        {
            Name = SelectedProfileName,
            Mods = activeMods.Select(m => new ProfileModEntry
            {
                ModId         = m.Id,
                Version       = m.Version,
                IsActive      = true,
                RepositoryUrl = string.IsNullOrEmpty(m.Repository) ? null : m.Repository,
                HomePageUrl   = string.IsNullOrEmpty(m.HomePage)   ? null : m.HomePage
            }).ToList()
        };

        await _profileService.SaveProfileAsync(profile, _settings.Settings.ProfilesPath);
        _settings.Settings.ActiveProfileName = SelectedProfileName;
        await _settings.SaveAsync();
        await RefreshProfileListAsync();
        StatusMessage = _localization.GetString("status.profile_saved", SelectedProfileName);
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeactivateAllModsAsync()
    {
        if (_settings.Settings.GamePath == null) return;
        var active = ActiveMods.ToList();
        if (active.Count == 0) { StatusMessage = _localization.GetString("status.no_active_mods"); return; }

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

        bool hasWork = diff.ToActivate.Count > 0 || diff.ToDeactivate.Count > 0
                    || diff.ToRollback.Count > 0 || diff.ToDownload.Count > 0
                    || diff.ToRedownload.Count > 0;

        if (!hasWork)
        {
            StatusMessage = _localization.GetString("status.profile_already_applied");
            return;
        }

        // ── Handle missing mods that need downloading ─────────────────────────
        if (diff.ToDownload.Count > 0)
        {
            var githubEntries = diff.ToDownload.Where(e => !string.IsNullOrEmpty(e.RepositoryUrl)).ToList();
            var nexusEntries  = diff.ToDownload.Where(e => string.IsNullOrEmpty(e.RepositoryUrl) && !string.IsNullOrEmpty(e.HomePageUrl)).ToList();

            var lines = new List<string>();
            if (githubEntries.Count > 0)
            {
                lines.Add($"Will auto-download from GitHub ({githubEntries.Count}):");
                lines.AddRange(githubEntries.Select(e => $"  • {e.ModId}"));
            }
            if (nexusEntries.Count > 0)
            {
                lines.Add($"Will open in browser for manual download ({nexusEntries.Count}):");
                lines.AddRange(nexusEntries.Select(e => $"  • {e.ModId}"));
            }

            var confirmed = await _dialogService.ConfirmAsync(
                "Download Missing Mods",
                string.Join("\n", lines) + "\n\nProceed with downloads?");

            if (confirmed)
            {
                int downloaded = 0;
                var failedEntries = new List<(string ModId, string? HomePageUrl)>();
                SetBusy($"Downloading missing mods (0/{githubEntries.Count})...");

                foreach (var entry in githubEntries)
                {
                    BusyMessage = $"Resolving {entry.ModId}...";
                    // Build a stub ModInfo so we can call CheckUpdateAsync which
                    // calls the GitHub releases API to get the asset download URL.
                    var stub = new ModInfo
                    {
                        Id = entry.ModId,
                        Version = "0.0.0",
                        Repository = entry.RepositoryUrl!
                    };
                    try
                    {
                        using var resolveCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        var updateInfo = await _updateService.CheckUpdateAsync(stub, resolveCts.Token);
                        if (updateInfo?.DownloadUrl != null)
                        {
                            BusyMessage = $"Downloading {entry.ModId} ({downloaded + 1}/{githubEntries.Count})...";
                            var progress = new Progress<double>(p =>
                                BusyMessage = $"Downloading {entry.ModId} {p:P0} ({downloaded + 1}/{githubEntries.Count})...");
                            using var downloadCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                            var result = await _modInstall.DownloadAndInstallFromUrlAsync(
                                updateInfo.DownloadUrl, _settings.Settings.GamePath, _settings.Settings.StoragePath,
                                progress, downloadCts.Token);
                            if (result != null) downloaded++;
                            else failedEntries.Add((entry.ModId, entry.HomePageUrl));
                        }
                        else
                        {
                            failedEntries.Add((entry.ModId, entry.HomePageUrl));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        failedEntries.Add((entry.ModId, entry.HomePageUrl));
                    }
                }

                // Collect nexus-only entries into the failed list so they appear in the dialog
                foreach (var entry in nexusEntries)
                    failedEntries.Add((entry.ModId, entry.HomePageUrl));

                // Refresh so newly installed mods appear in the diff for activation
                await RefreshModsAsync();
                allMods = AvailableMods.Concat(ActiveMods).Select(v => v.ModInfo).ToList();
                diff = _profileService.ComputeDiff(profile, allMods);

                ClearBusy();
                if (failedEntries.Count > 0)
                {
                    var openNexus = await _dialogService.ShowFailedDownloadsAsync(
                        "Download Failed", failedEntries);
                    if (openNexus)
                    {
                        foreach (var (_, homePageUrl) in failedEntries.Where(f => !string.IsNullOrEmpty(f.HomePageUrl)))
                            Helpers.PlatformHelper.Open(homePageUrl!);
                    }
                    StatusMessage = $"Downloaded {downloaded} mod(s). {failedEntries.Count} could not be auto-downloaded.";
                }
            }
        }

        // ── Handle mods that need re-downloading to the correct version ──────
        if (diff.ToRedownload.Count > 0)
        {
            var reDownloadEntries = diff.ToRedownload
                .Where(e => !string.IsNullOrEmpty(e.RepositoryUrl)).ToList();

            // Separate entries: those with the target version already in the cache
            // vs those that need downloading from the repository.
            var cachedEntries = new List<ProfileModEntry>();
            var downloadEntries = new List<ProfileModEntry>();
            foreach (var entry in reDownloadEntries)
            {
                var cachedPath = await _versionCache.GetVersionArchivePathAsync(
                    entry.ModId, entry.Version, _settings.Settings.StoragePath);
                if (cachedPath != null)
                    cachedEntries.Add(entry);
                else
                    downloadEntries.Add(entry);
            }

            var reLines = new List<string>();
            if (cachedEntries.Count > 0)
            {
                reLines.Add(_localization.GetString("dialog.redownload_will_rollback", cachedEntries.Count));
                reLines.AddRange(cachedEntries.Select(e => _localization.GetString("dialog.redownload_entry", e.ModId, e.Version)));
            }
            if (downloadEntries.Count > 0)
            {
                reLines.Add(_localization.GetString("dialog.redownload_will_download", downloadEntries.Count));
                reLines.AddRange(downloadEntries.Select(e => _localization.GetString("dialog.redownload_entry", e.ModId, e.Version)));
            }

            var reConfirmed = await _dialogService.ConfirmAsync(
                _localization.GetString("dialog.redownload_title"),
                string.Join("\n", reLines) + "\n\n" + _localization.GetString("dialog.redownload_proceed"));

            if (reConfirmed)
            {
                int reDone = 0;
                var reFailedEntries = new List<(string ModId, string? HomePageUrl)>();
                int totalCount = cachedEntries.Count + downloadEntries.Count;

                // Phase 1: Rollback cached versions
                if (cachedEntries.Count > 0)
                {
                    SetBusy(_localization.GetString("busy.rolling_back_cache", 0, cachedEntries.Count));
                    foreach (var entry in cachedEntries)
                    {
                        BusyMessage = _localization.GetString("status.rolling_back", entry.ModId, entry.Version);
                        var ok = await _modInstall.RollbackToVersionAsync(
                            entry.ModId, entry.Version,
                            _settings.Settings.GamePath, _settings.Settings.StoragePath);
                        if (ok) reDone++;
                        else reFailedEntries.Add((entry.ModId, entry.HomePageUrl));
                    }
                }

                // Phase 2: Download and apply versions not in cache
                if (downloadEntries.Count > 0)
                {
                    SetBusy(_localization.GetString("busy.downloading_correct", 0, downloadEntries.Count));
                    foreach (var entry in downloadEntries)
                    {
                        BusyMessage = _localization.GetString("busy.resolving_mod", entry.ModId) + $" ({reDone + 1}/{totalCount})";
                        // Build a stub ModInfo so we can resolve the download URL via the update service
                        var stub = new ModInfo
                        {
                            Id = entry.ModId,
                            Version = "0.0.0",
                            Repository = entry.RepositoryUrl!
                        };
                        try
                        {
                            // Find the current mod to preserve its active/inactive state
                            var currentMod = allMods.FirstOrDefault(m =>
                                string.Equals(m.Id, entry.ModId, StringComparison.OrdinalIgnoreCase));

                            using var resolveCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            var updateInfo = await _updateService.CheckUpdateAsync(stub, resolveCts.Token);
                            if (updateInfo?.DownloadUrl != null && currentMod != null)
                            {
                                BusyMessage = _localization.GetString("busy.downloading_mod", entry.ModId) + $" ({reDone + 1}/{totalCount})";
                                var progress = new Progress<double>(p =>
                                    BusyMessage = _localization.GetString("busy.download_progress", entry.ModId, p) + $" ({reDone + 1}/{totalCount})");

                                // Use UpdateModAsync which properly: archives current version,
                                // deletes old folder, downloads & installs new version preserving
                                // the active/inactive state.
                                updateInfo.LatestVersion = entry.Version;
                                var ok = await _modInstall.UpdateModAsync(
                                    currentMod, updateInfo,
                                    _settings.Settings.GamePath, _settings.Settings.StoragePath,
                                    progress);
                                if (ok) reDone++;
                                else reFailedEntries.Add((entry.ModId, entry.HomePageUrl));
                            }
                            else
                            {
                                reFailedEntries.Add((entry.ModId, entry.HomePageUrl));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            reFailedEntries.Add((entry.ModId, entry.HomePageUrl));
                        }
                    }
                }

                // Refresh so the applied correct versions appear in the diff
                await RefreshModsAsync();
                allMods = AvailableMods.Concat(ActiveMods).Select(v => v.ModInfo).ToList();
                diff = _profileService.ComputeDiff(profile, allMods);

                ClearBusy();
                if (reFailedEntries.Count > 0)
                {
                    var openNexus = await _dialogService.ShowFailedDownloadsAsync(
                        _localization.GetString("dialog.redownload_failed_title"), reFailedEntries);
                    if (openNexus)
                    {
                        foreach (var (_, homePageUrl) in reFailedEntries.Where(f => !string.IsNullOrEmpty(f.HomePageUrl)))
                            Helpers.PlatformHelper.Open(homePageUrl!);
                    }
                    StatusMessage = _localization.GetString("status.applied_correct_version_failed", reDone, reFailedEntries.Count);
                }
                else if (reDone > 0)
                {
                    StatusMessage = _localization.GetString("status.applied_correct_version", reDone);
                }
            }
        }

        // ── No remaining work? ────────────────────────────────────────────────
        if (diff.ToActivate.Count == 0 && diff.ToDeactivate.Count == 0 && diff.ToRollback.Count == 0)
        {
            _settings.Settings.ActiveProfileName = profileName;
            SelectedProfileName = profileName;
            await _settings.SaveAsync();
            StatusMessage = _localization.GetString("status.profile_applied", profileName);
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
        StatusMessage = _localization.GetString("status.profile_applied", profileName);
    }

    // ── Remove (for externally deleted mods) ─────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task RemoveSelectedModAsync()
    {
        if (SelectedMod == null) return;

        var displayName = SelectedMod.DisplayName;
        var confirmed = await _dialogService.ConfirmAsync(
            _localization.GetString("dialog.remove_title"),
            _localization.GetString("dialog.remove_message", displayName));

        if (!confirmed) return;

        var modId = SelectedMod.Id;

        // Try to delete the folder from disk if it still exists
        if (_settings.Settings.GamePath != null)
        {
            var folderPath = SelectedMod.ModInfo.FolderPath;
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                try { Directory.Delete(folderPath, true); }
                catch { /* best-effort */ }
            }
        }

        // Remove from the UI lists
        AvailableMods.Remove(SelectedMod);
        ActiveMods.Remove(SelectedMod);

        // Clean up any group references
        foreach (var g in _settings.Settings.ModGroups)
            g.ModIds.Remove(modId);
        await _settings.SaveAsync();

        ApplyFilters();
        StatusMessage = _localization.GetString("status.removed", displayName);
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
            // Preserve any pending update state before clearing — RefreshModsAsync
            // creates new ModInfo/VM instances so update badges would be lost otherwise
            // A mod can appear twice (same Id in both Mods and Mods.inactive) or as a
            // duplicate, so guard against duplicate keys with a case-insensitive compare.
            var pendingUpdates = new Dictionary<string, ModUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in AvailableMods.Concat(ActiveMods))
                if (m.ModInfo.PendingUpdate != null)
                    pendingUpdates.TryAdd(m.Id, m.ModInfo.PendingUpdate);

            AvailableMods.Clear();
            ActiveMods.Clear();

            foreach (var mod in mods.OrderBy(m => m.EffectiveDisplayName))
            {
                var vm = new ModItemViewModel(mod);
                // Re-apply any update badge that was set before the refresh
                if (pendingUpdates.TryGetValue(vm.Id, out var pending))
                    vm.ApplyUpdate(pending);
                if (mod.IsActive) ActiveMods.Add(vm);
                else AvailableMods.Add(vm);
            }

            ApplyFilters();

            var active = mods.Count(m => m.IsActive);
            var inactive = mods.Count(m => !m.IsActive);
            StatusMessage = mods.Count == 0
                ? _localization.GetString("status.no_mods_found", modsDir)
                : _localization.GetString("status.mods_found", mods.Count, active, inactive);
        });
    }

    private async Task RefreshProfileListAsync()
    {
        var profiles = await _profileService.GetProfilesAsync(_settings.Settings.ProfilesPath);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Snapshot before Clear() — Avalonia's editable ComboBox blanks its Text
            // binding when ItemsSource is cleared, wiping SelectedProfileName.
            var current = SelectedProfileName;

            ProfileNames.Clear();
            if (!profiles.Any(p => p.Name == "Default"))
                ProfileNames.Add("Default");
            foreach (var p in profiles) ProfileNames.Add(p.Name);

            // Restore only if the name still exists; otherwise fall back to the active profile
            // (or "Default"). This handles the case where the currently-selected profile was deleted.
            if (!string.IsNullOrEmpty(current) && ProfileNames.Contains(current))
                SelectedProfileName = current;
            else
                SelectedProfileName = _settings.Settings.ActiveProfileName is { Length: > 0 } active
                    && ProfileNames.Contains(active) ? active : "Default";
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
            var previousLanguage = _settings.Settings.Language;
            vm.Apply(_settings.Settings);             // copy UI values → AppSettings
            await _settings.SaveAsync();
            
            // Apply language change if it changed
            if (_settings.Settings.Language != previousLanguage)
            {
                _localization.SetLanguage(_settings.Settings.Language);
            }
            
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

        // Always refresh so deletions/imports are reflected in the dropdown
        await RefreshProfileListAsync();

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
            RemoveSelectedModCommand.NotifyCanExecuteChanged();
        });
    }

    private volatile bool _pendingExternalRefresh;

    private void OnModsChangedExternally(object? sender, EventArgs e)
    {
        if (IsBusy)
        {
            // Queue the refresh so it fires after the current operation completes
            _pendingExternalRefresh = true;
            return;
        }
        Dispatcher.UIThread.Post(async () => await RefreshModsAsync());
    }

    private void UpdateLocalizedStrings()
    {
        WindowTitle = _localization.GetString("window.title");
        PanelAvailableHeader = _localization.GetString("panel.available");
        PanelActiveHeader = _localization.GetString("panel.active");
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
        RemoveSelectedModCommand.NotifyCanExecuteChanged();
    }

    private void ClearBusy()
    {
        IsBusy = false;
        BusyMessage = "";
        ActivateSelectedModCommand.NotifyCanExecuteChanged();
        DeactivateSelectedModCommand.NotifyCanExecuteChanged();
        RemoveSelectedModCommand.NotifyCanExecuteChanged();

        // Process any queued external refresh that was suppressed while busy
        if (_pendingExternalRefresh)
        {
            _pendingExternalRefresh = false;
            _ = RefreshModsAsync();
        }
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
