using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
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
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = "";
    [ObservableProperty] private string _selectedProfileName = ManagerStorage.DefaultProfileName;
    [ObservableProperty] private ObservableCollection<string> _profileNames = [];
    [ObservableProperty] private string _windowTitle = "DV Mod Manager";
    [ObservableProperty] private string _panelAvailableHeader = "Available Mods";
    [ObservableProperty] private string _panelActiveHeader = "Active Mods";

    // ── App version / update ───────────────────────────────────────────────────
    private const string ManagerUpdateRepository = "https://raw.githubusercontent.com/Fuggschen/DVModManager/main/DVModManager/repository.json";

    public string AppVersion { get; } = StripMetadata(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    private static string StripMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    [ObservableProperty] private bool _hasManagerUpdate;
    [ObservableProperty] private string _latestManagerVersion = "";
    [ObservableProperty] private string _managerUpdateUrl = "";
    [ObservableProperty] private string _appVersionLabel = "";

    // ── Companion mod state ───────────────────────────────────────────────────
    private const string CompanionModId = "DVModProfiles";
    private const string CompanionModRepository = "https://raw.githubusercontent.com/Fuggschen/DVModManager/main/DVModProfiles/repository.json";

    [ObservableProperty] private bool _isCompanionModInstalled;
    [ObservableProperty] private bool _isCompanionModInactive;
    [ObservableProperty] private string _companionButtonKey = "button.install_companion";

    /// <summary>Localized text for the companion button (computed from CompanionButtonKey).</summary>
    public string CompanionButtonContent => _localization.GetString(CompanionButtonKey);

    partial void OnCompanionButtonKeyChanged(string value)
    {
        OnPropertyChanged(nameof(CompanionButtonContent));
    }

    /// <summary>Checks whether the companion mod exists in either available or active mods.</summary>
    private void UpdateCompanionModState()
    {
        var companion = AvailableMods.Concat(ActiveMods)
            .FirstOrDefault(m => string.Equals(m.Id, CompanionModId, StringComparison.OrdinalIgnoreCase));
        IsCompanionModInstalled = companion != null;
        IsCompanionModInactive = companion != null && !companion.IsActive;
        CompanionButtonKey = companion switch
        {
            null => "button.install_companion",
            { IsActive: true } => "button.activated_companion",
            _ => "button.activate_companion"
        };
        InstallCompanionModCommand.NotifyCanExecuteChanged();
    }

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
        _ = CheckManagerUpdateAsync();
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
            bool belongsToAvailable = group.Panel == null || group.Panel == "available";
            // Show in available panel if: belongs there with items/empty, OR has mods present
            if ((belongsToAvailable && (availItems.Count > 0 || group.ModIds.Count == 0))
                || availItems.Count > 0)
            {
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
            bool belongsToActive = group.Panel == null || group.Panel == "active";
            // Show in active panel if: belongs there with items/empty, OR has mods present
            if ((belongsToActive && (activeItems.Count > 0 || group.ModIds.Count == 0))
                || activeItems.Count > 0)
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
    private async Task AddGroupAsync(string? panel)
    {
        var group = new ModGroup { Id = Guid.NewGuid().ToString(), Name = _localization.GetString("dialog.new_group"), Panel = panel };
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
            SetBusy(_localization.GetString("status.activating_group", checkedTargets.Count));
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
                ? _localization.GetString("status.activated_simple_missing_deps", activated, checkedTargets.Count, string.Join("; ", missingDeps))
                : _localization.GetString("status.activated_simple", activated, checkedTargets.Count);
            return;
        }

        // Group selected — activate all available mods in the group
        if (SelectedGroup != null)
        {
            var group = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == SelectedGroup.GroupId);
            if (group == null) return;
            var targets = AvailableMods.Where(m => group.ModIds.Contains(m.Id)).ToList();
            if (targets.Count == 0) return;
            SetBusy(_localization.GetString("status.activating_group", targets.Count));
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
                ? _localization.GetString("status.activated_simple_missing_deps", activated, targets.Count, string.Join("; ", missingDeps))
                : _localization.GetString("status.activated_count", activated, targets.Count, group.Name);
            return;
        }

        if (SelectedMod == null) return;

        // Capture before any await — ApplyFilters can null SelectedMod
        var mod = SelectedMod;
        var displayName = mod.DisplayName;

        // Try to auto-activate any inactive dependencies first (recursive BFS)
        SetBusy(_localization.GetString("status.resolving_dependencies", displayName));
        var trulyMissing = await AutoActivateDependenciesAsync(mod);
        ClearBusy();
        if (trulyMissing.Count > 0)
        {
            mod.State = ModState.MissingDependency;
            mod.HasMissingDependency = true;
            StatusMessage = _localization.GetString("status.missing_dependencies", displayName, string.Join(", ", trulyMissing));
            return;
        }

        SetBusy(_localization.GetString("status.activating_mod", displayName));
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
            SetBusy(_localization.GetString("status.deactivating_group", checkedTargets.Count));
            int deactivated = 0;
            foreach (var target in checkedTargets)
            {
                var success = await _modInstall.DeactivateModAsync(target.ModInfo, _settings.Settings.GamePath);
                if (success) { target.SyncFromModel(); MoveToInactive(target); deactivated++; }
            }
            ClearBusy();
            StatusMessage = _localization.GetString("status.deactivated_simple", deactivated, checkedTargets.Count);
            return;
        }

        // Group selected — deactivate all active mods in the group
        if (SelectedGroup != null)
        {
            var group = _settings.Settings.ModGroups.FirstOrDefault(g => g.Id == SelectedGroup.GroupId);
            if (group == null) return;
            var targets = ActiveMods.Where(m => group.ModIds.Contains(m.Id)).ToList();
            if (targets.Count == 0) return;
            SetBusy(_localization.GetString("status.deactivating_group", targets.Count));
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

        SetBusy(_localization.GetString("status.deactivating_mod", SelectedMod.DisplayName));
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
        var paths = await _dialogService.OpenFilesAsync(
            _localization.GetString("install.button.tooltip"),
            _localization.GetString("file.filter.mod_archives"), ["zip", "json"]);
        if (paths.Count == 0 || _settings.Settings.GamePath == null) return;

        // If a single JSON file was selected, treat as profile import
        if (paths.Count == 1 && Path.GetExtension(paths[0]).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var profile = await _profileService.ImportProfileAsync(paths[0]);
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

        // Filter to only .zip files
        var zipPaths = paths.Where(p => Path.GetExtension(p).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (zipPaths.Count == 0) return;

        // ── Single ZIP: check for profile.json inside → modpack import ───
        if (zipPaths.Count == 1)
        {
            ModProfile? embeddedProfile = null;
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(zipPaths[0]);
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
                            System.IO.Compression.ZipFile.ExtractToDirectory(zipPaths[0], tempDir, overwriteFiles: true));

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
        }

        // ── Regular mod archive install (one or multiple) ─────────────────
        var installedNames = new List<string>();
        var failedCount = 0;

        foreach (var zipPath in zipPaths)
        {
            SetBusy(_localization.GetString("busy.installing_mod", Path.GetFileName(zipPath)));
            var mod = await _modInstall.InstallFromArchiveAsync(
                zipPath, _settings.Settings.GamePath, _settings.Settings.StoragePath);
            if (mod != null)
                installedNames.Add(mod.EffectiveDisplayName);
            else
                failedCount++;
        }

        await RefreshModsAsync();
        ClearBusy();

        if (zipPaths.Count == 1)
        {
            StatusMessage = installedNames.Count > 0
                ? _localization.GetString("status.install_success", installedNames[0])
                : _localization.GetString("status.install_failed");
        }
        else
        {
            StatusMessage = _localization.GetString("status.install_multi_result",
                installedNames.Count, zipPaths.Count, failedCount);
        }
    }

    /// <summary>
    /// Installs one or more dropped archive files. If <paramref name="activate"/> is true,
    /// each mod is also activated after install (with dependency resolution). If a dependency
    /// is missing, the mod is installed but not activated (stays in Available).
    /// </summary>
    public async Task InstallDroppedArchivesAsync(IReadOnlyList<string> archivePaths, bool activate)
    {
        if (_settings.Settings.GamePath == null) return;

        var installedNames = new List<string>();
        var activatedNames = new List<string>();
        var failedCount = 0;

        // Install all archives first (always install to available/inactive)
        foreach (var path in archivePaths)
        {
            SetBusy(_localization.GetString("busy.installing_mod", Path.GetFileName(path)));
            var mod = await _modInstall.InstallFromArchiveAsync(
                path, _settings.Settings.GamePath, _settings.Settings.StoragePath, activate: false);

            if (mod == null)
            {
                failedCount++;
                continue;
            }

            installedNames.Add(mod.EffectiveDisplayName);
        }

        // Refresh so newly installed mods appear in the lists
        await RefreshModsAsync();

        // If dropping on the active panel, activate each installed mod with dependency resolution
        if (activate && installedNames.Count > 0)
        {
            foreach (var name in installedNames.ToList())
            {
                // Find by matching the name of mods that were just installed
                var installed = AvailableMods
                    .FirstOrDefault(m => installedNames.Contains(m.DisplayName)
                        && !m.IsActive);
                if (installed == null) continue;

                BusyMessage = _localization.GetString("status.resolving_dependencies", installed.DisplayName);
                var missing = await AutoActivateDependenciesAsync(installed);
                if (missing.Count == 0)
                {
                    var success = await _modInstall.ActivateModAsync(installed.ModInfo, _settings.Settings.GamePath);
                    if (success)
                    {
                        installed.SyncFromModel();
                        MoveToActive(installed);
                        activatedNames.Add(installed.DisplayName);
                    }
                }
                // If dependencies are missing, mod stays in Available
            }
        }

        ClearBusy();

        // Build status message
        if (archivePaths.Count == 1)
        {
            if (installedNames.Count > 0)
            {
                if (activate && activatedNames.Count > 0)
                    StatusMessage = _localization.GetString("status.activated", activatedNames[0]);
                else if (activate)
                    StatusMessage = _localization.GetString("status.install_success", installedNames[0]);
                else
                    StatusMessage = _localization.GetString("status.install_success", installedNames[0]);
            }
            else
            {
                StatusMessage = _localization.GetString("status.install_failed");
            }
        }
        else
        {
            var parts = new List<string>();
            if (installedNames.Count > 0)
                parts.Add($"{installedNames.Count} mod(s) installed");
            if (activatedNames.Count > 0)
                parts.Add($"{activatedNames.Count} activated");
            if (failedCount > 0)
                parts.Add($"{failedCount} failed");
            StatusMessage = string.Join(", ", parts);
        }
    }

    private bool CanInstallCompanion() => !IsCompanionModInstalled || IsCompanionModInactive;

    private void RecheckCompanionCanExecute()
    {
        // Called after UpdateCompanionModState to sync command availability
        InstallCompanionModCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Downloads the latest release of the companion mod (DVModProfiles) from GitHub,
    /// installs it into the game, and activates it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallCompanion))]
    private async Task InstallCompanionModAsync()
    {
        if (_settings.Settings.GamePath == null) return;

        // If companion is installed but inactive, just activate it
        if (IsCompanionModInactive)
        {
            var companion = AvailableMods.Concat(ActiveMods)
                .FirstOrDefault(m => string.Equals(m.Id, CompanionModId, StringComparison.OrdinalIgnoreCase));
            if (companion != null)
            {
                SetBusy(_localization.GetString("busy.activating_companion"));
                var ok = await _modInstall.ActivateModAsync(companion.ModInfo, _settings.Settings.GamePath);
                if (ok)
                {
                    companion.SyncFromModel();
                    MoveToActive(companion);
                    StatusMessage = _localization.GetString("status.companion_activated");
                }
                else
                {
                    StatusMessage = _localization.GetString("status.activation_failed", companion.DisplayName);
                }
                UpdateCompanionModState();
                ClearBusy();
            }
            return;
        }

        // Otherwise, download and install
        SetBusy(_localization.GetString("busy.installing_companion"));

        try
        {
            // Resolve the latest release download URL via the update service
            var stub = new ModInfo
            {
                Id = CompanionModId,
                Version = "0.0.0",
                Repository = CompanionModRepository
            };

            using var resolveCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var updateInfo = await _updateService.CheckUpdateAsync(stub, resolveCts.Token);
            if (updateInfo?.DownloadUrl == null)
            {
                ClearBusy();
                StatusMessage = _localization.GetString("status.companion_install_failed");
                return;
            }

            var progress = new Progress<double>(p =>
                BusyMessage = _localization.GetString("busy.downloading_mod", CompanionModId) + $" {p:P0}");

            var result = await _modInstall.DownloadAndInstallFromUrlAsync(
                updateInfo.DownloadUrl, _settings.Settings.GamePath, _settings.Settings.StoragePath,
                progress);

            if (result == null)
            {
                ClearBusy();
                StatusMessage = _localization.GetString("status.companion_install_failed");
                return;
            }

            // Refresh so the mod appears in the lists
            await RefreshModsAsync();

            // Auto-activate the companion mod
            var companion = AvailableMods.Concat(ActiveMods)
                .FirstOrDefault(m => string.Equals(m.Id, CompanionModId, StringComparison.OrdinalIgnoreCase));
            if (companion != null && !companion.IsActive)
            {
                BusyMessage = _localization.GetString("busy.activating_companion");
                var ok = await _modInstall.ActivateModAsync(companion.ModInfo, _settings.Settings.GamePath);
                if (ok)
                {
                    companion.SyncFromModel();
                    MoveToActive(companion);
                }
            }

            ClearBusy();
            StatusMessage = _localization.GetString("status.companion_installed");
        }
        catch
        {
            ClearBusy();
            StatusMessage = _localization.GetString("status.companion_install_failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task UninstallSelectedModAsync()
    {
        if (SelectedMod == null || _settings.Settings.GamePath == null) return;

        var displayName = SelectedMod.DisplayName;
        var confirmed = await _dialogService.ConfirmAsync(
            _localization.GetString("dialog.uninstall_title"),
            _localization.GetString("dialog.uninstall_message", displayName));

        if (!confirmed) return;

        SetBusy(_localization.GetString("busy.uninstalling_mod", displayName));
        var success = await _modInstall.UninstallModAsync(
            SelectedMod.ModInfo, _settings.Settings.GamePath, _settings.Settings.StoragePath, hardDelete: false);
        ClearBusy();

        if (success)
        {
            // Remove from groups before refresh to prevent ghost entries
            var modId = SelectedMod.Id;
            foreach (var g in _settings.Settings.ModGroups)
                g.ModIds.Remove(modId);
            await _settings.SaveAsync();

            await RefreshModsAsync();
            StatusMessage = _localization.GetString("status.uninstalled", displayName);
        }
    }

    // ── Start Game ────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task StartGameAsync()
    {
        Helpers.PlatformHelper.Open("steam://launch/588030/dialog");
        StatusMessage = _localization.GetString("status.game_starting");
        return Task.CompletedTask;
    }

    // ── Updates ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        var allMods = AvailableMods.Concat(ActiveMods).Select(v => v.ModInfo).ToList();
        if (allMods.Count == 0) return;

        SetBusy(_localization.GetString("status.checking_updates"));
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

        SetBusy(_localization.GetString("status.updating", displayName, update.LatestVersion));
        var progress = new Progress<double>(p =>
            BusyMessage = _localization.GetString("busy.update_progress", displayName, p));
        var success = await _modInstall.UpdateModAsync(
            SelectedMod.ModInfo, update, _settings.Settings.GamePath, _settings.Settings.StoragePath, progress);
        ClearBusy();

        if (success)
        {
            // Clear the pending update before refresh so the badge doesn't persist
            SelectedMod.ModInfo.PendingUpdate = null;
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

        var confirmed = await _dialogService.ConfirmAsync(
            _localization.GetString("dialog.update_all_title"),
            _localization.GetString("dialog.update_all_message", modsWithUpdates.Count));
        if (!confirmed) return;

        // Backup first
        if (_settings.Settings.GamePath != null && _settings.Settings.BackupBeforeChanges)
        {
            SetBusy(_localization.GetString("busy.creating_backup"));
            await _modInstall.BackupModsFolderAsync(_settings.Settings.GamePath, _settings.Settings.StoragePath);
        }

        int updated = 0;
        foreach (var mod in modsWithUpdates)
        {
            var modName = mod.DisplayName;
            SetBusy(_localization.GetString("busy.update_multiple", modName, 0.0, updated + 1, modsWithUpdates.Count));
            var progress = new Progress<double>(p =>
                BusyMessage = _localization.GetString("busy.update_multiple", modName, p, updated + 1, modsWithUpdates.Count));
            var success = await _modInstall.UpdateModAsync(
                mod.ModInfo, mod.ModInfo.PendingUpdate!, _settings.Settings.GamePath!, _settings.Settings.StoragePath, progress);
            if (success)
            {
                mod.ModInfo.PendingUpdate = null;
                updated++;
            }
        }

        ClearBusy();
        await RefreshModsAsync();
        StatusMessage = _localization.GetString("status.updated_n_total", updated, modsWithUpdates.Count);
    }

    // ── Rollback ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task RollbackSelectedModAsync(string version)
    {
        if (SelectedMod == null || _settings.Settings.GamePath == null) return;

        var displayName = SelectedMod.DisplayName;
        var modId = SelectedMod.Id;
        SetBusy(_localization.GetString("status.rolling_back", displayName, version));
        var success = await _modInstall.RollbackToVersionAsync(
            modId, version, _settings.Settings.GamePath, _settings.Settings.StoragePath);
        ClearBusy();

        if (success)
        {
            await RefreshModsAsync();
            StatusMessage = _localization.GetString("status.rolled_back", displayName, version);
        }
        else
        {
            StatusMessage = _localization.GetString("status.rollback_failed", displayName);
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

        var confirmed = await _dialogService.ConfirmAsync(
            _localization.GetString("dialog.unload_all_title"),
            _localization.GetString("dialog.unload_all_message", active.Count));
        if (!confirmed) return;

        SetBusy(_localization.GetString("busy.unloading_all"));
        int done = 0;
        foreach (var vm in active)
        {
            BusyMessage = _localization.GetString("busy.downloading_multiple", vm.DisplayName, done + 1, active.Count);
            var ok = await _modInstall.DeactivateModAsync(vm.ModInfo, _settings.Settings.GamePath!);
            if (ok) done++;
        }
        ClearBusy();
        await RefreshModsAsync();
        StatusMessage = _localization.GetString("status.deactivated_simple", done, active.Count);
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
                lines.Add(_localization.GetString("dialog.download_will_auto_github", githubEntries.Count));
                lines.AddRange(githubEntries.Select(e => $"  • {e.ModId}"));
            }
            if (nexusEntries.Count > 0)
            {
                lines.Add(_localization.GetString("dialog.download_will_open_browser", nexusEntries.Count));
                lines.AddRange(nexusEntries.Select(e => $"  • {e.ModId}"));
            }

            var confirmed = await _dialogService.ConfirmAsync(
                _localization.GetString("dialog.download_missing_title"),
                string.Join("\n", lines) + "\n\n" + _localization.GetString("dialog.download_proceed"));

            if (confirmed)
            {
                int downloaded = 0;
                var failedEntries = new List<(string ModId, string? HomePageUrl)>();
                SetBusy(_localization.GetString("busy.downloading_multiple", "...", 0, githubEntries.Count));

                foreach (var entry in githubEntries)
                {
                    BusyMessage = _localization.GetString("busy.resolving_mod", entry.ModId);
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
                            BusyMessage = _localization.GetString("busy.downloading_multiple", entry.ModId, downloaded + 1, githubEntries.Count);
                            var progress = new Progress<double>(p =>
                                BusyMessage = _localization.GetString("busy.downloading_multiple", entry.ModId, downloaded + 1, githubEntries.Count) + $" {p:P0}");
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
                        _localization.GetString("dialog.failed_downloads_title"), failedEntries);
                    if (openNexus)
                    {
                        foreach (var (_, homePageUrl) in failedEntries.Where(f => !string.IsNullOrEmpty(f.HomePageUrl)))
                            Helpers.PlatformHelper.Open(homePageUrl!);
                    }
                    StatusMessage = _localization.GetString("status.downloaded_n_failed", downloaded, failedEntries.Count);
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
            SetBusy(_localization.GetString("busy.creating_backup"));
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
            StatusMessage = _localization.GetString("status.game_path_not_set");
            return;
        }

        var modsDir = Path.Combine(_settings.Settings.GamePath, "Mods");
        if (!Directory.Exists(modsDir))
        {
            StatusMessage = _localization.GetString("status.mods_folder_not_found", modsDir);
            return;
        }

        IReadOnlyList<DVModManager.Models.ModInfo> mods;
        try
        {
            mods = await _modDiscovery.ScanAllModsAsync(_settings.Settings.GamePath);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.GetString("status.scan_error", ex.Message);
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
                if (pendingUpdates.TryGetValue(vm.Id, out var pending)
                    && vm.Version != pending.LatestVersion)
                    vm.ApplyUpdate(pending);
                if (mod.IsActive) ActiveMods.Add(vm);
                else AvailableMods.Add(vm);
            }

            ApplyFilters();
            UpdateCompanionModState();

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
            if (!profiles.Any(p => p.Name == ManagerStorage.DefaultProfileName))
                ProfileNames.Add(ManagerStorage.DefaultProfileName);
            foreach (var p in profiles) ProfileNames.Add(p.Name);

            // Restore only if the name still exists; otherwise fall back to the active profile
            // (or "Default"). This handles the case where the currently-selected profile was deleted.
            if (!string.IsNullOrEmpty(current) && ProfileNames.Contains(current))
                SelectedProfileName = current;
            else
                SelectedProfileName = _settings.Settings.ActiveProfileName is { Length: > 0 } active
                    && ProfileNames.Contains(active) ? active : ManagerStorage.DefaultProfileName;
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
            StatusMessage = running ? _localization.GetString("status.game_running_locked") : _localization.GetString("status.game_stopped_unlocked");
            ActivateSelectedModCommand.NotifyCanExecuteChanged();
            DeactivateSelectedModCommand.NotifyCanExecuteChanged();
            InstallModFromFileCommand.NotifyCanExecuteChanged();
            UninstallSelectedModCommand.NotifyCanExecuteChanged();
            UpdateSelectedModCommand.NotifyCanExecuteChanged();
            UpdateAllModsCommand.NotifyCanExecuteChanged();
            ApplyProfileCommand.NotifyCanExecuteChanged();
            RemoveSelectedModCommand.NotifyCanExecuteChanged();
            InstallCompanionModCommand.NotifyCanExecuteChanged();
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
        UpdateAppVersionLabel();
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
    /// Recursively activate all required dependencies (BFS).
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
        var processed    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BFS queue — seed with the direct requirements
        var queue = new Queue<string>();
        foreach (var req in vm.Requirements)
            queue.Enqueue(req);

        while (queue.Count > 0)
        {
            var depId = queue.Dequeue();
            if (string.IsNullOrEmpty(depId) || !processed.Add(depId)) continue;
            if (activeIds.Contains(depId)) continue;

            if (inactiveMap.TryGetValue(depId, out var depVm))
            {
                BusyMessage = _localization.GetString("busy.activating_dependency", depVm.DisplayName);
                var ok = await _modInstall.ActivateModAsync(depVm.ModInfo, _settings.Settings.GamePath);
                if (ok)
                {
                    depVm.SyncFromModel();
                    activated.Add(depVm);
                    activeIds.Add(depId);
                    // Enqueue this dependency's own requirements for transitive resolution
                    foreach (var subReq in depVm.Requirements)
                        queue.Enqueue(subReq);
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
        var allActive = ActiveMods.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = vm.Requirements
            .Where(r => !allActive.Contains(r))
            .ToList();

        if (missing.Count == 0) return true;

        vm.State = ModState.MissingDependency;
        vm.HasMissingDependency = true;
        StatusMessage = _localization.GetString("status.missing_dependencies", vm.DisplayName, string.Join(", ", missing));
        return false;
    }

    private async Task PromptGamePathAsync()
    {
        var path = await _dialogService.PickFolderAsync(_localization.GetString("dialog.select_game_folder"));
        if (path == null || !_gameDetection.ValidateGamePath(path))
        {
            await _dialogService.ShowMessageAsync(_localization.GetString("dialog.error"), _localization.GetString("dialog.invalid_game_path"));
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

    // ── Manager update check ──────────────────────────────────────────────────

    private async Task CheckManagerUpdateAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DVModManager/" + AppVersion);
            string json;
            json = await http.GetStringAsync(ManagerUpdateRepository);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Version", out var vProp)) return;
            var latestVersion = vProp.GetString();
            if (latestVersion == null) return;

            if (!IsNewerVersion(AppVersion, latestVersion)) return;

            LatestManagerVersion = latestVersion;
            ManagerUpdateUrl = doc.RootElement.TryGetProperty("ReleaseUrl", out var ruProp)
                ? ruProp.GetString() ?? ""
                : "";
            HasManagerUpdate = true;
            UpdateAppVersionLabel();
        }
        catch
        {
            // Silent — don't bother the user if the check fails
        }
    }

    private static bool IsNewerVersion(string current, string latest)
    {
        if (Version.TryParse(Normalize(current), out var c) &&
            Version.TryParse(Normalize(latest), out var l))
            return l > c;
        return false;
    }

    private static string Normalize(string v)
    {
        var s = v.Trim().TrimStart('v', 'V');
        // Ensure at least Major.Minor for System.Version
        if (s.Count(c => c == '.') < 1) s += ".0";
        return s;
    }

    private void UpdateAppVersionLabel()
    {
        if (HasManagerUpdate)
            AppVersionLabel = _localization.GetString("app.version_update", AppVersion, LatestManagerVersion);
        else
            AppVersionLabel = _localization.GetString("app.version", AppVersion);
    }

    [RelayCommand]
    public Task OpenManagerUpdateAsync()
    {
        if (!string.IsNullOrEmpty(ManagerUpdateUrl))
            Helpers.PlatformHelper.Open(ManagerUpdateUrl);
        return Task.CompletedTask;
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
