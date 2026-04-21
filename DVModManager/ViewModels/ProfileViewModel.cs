using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

public partial class ProfileViewModel : ViewModelBase
{
    private readonly IProfileService _profileService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;
    private string _profilesPath = "";

    [ObservableProperty] private ObservableCollection<ModProfile> _profiles = [];
    [ObservableProperty] private ModProfile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = "";

    public bool Applied { get; private set; }
    public string? AppliedProfileName { get; private set; }

    public event EventHandler<string>? ProfileApplyRequested;

    public ProfileViewModel(IProfileService profileService, IDialogService dialogService,
        ISettingsService settings, ILocalizationService loc)
    {
        _profileService = profileService;
        _dialogService = dialogService;
        _settings = settings;
        _loc = loc;
    }

    public async Task LoadProfilesAsync(string profilesPath)
    {
        _profilesPath = profilesPath;
        var list = await _profileService.GetProfilesAsync(profilesPath);
        Profiles.Clear();
        foreach (var p in list) Profiles.Add(p);
    }

    [RelayCommand]
    private void SelectProfile(ModProfile profile) => SelectedProfile = profile;

    [RelayCommand]
    private async Task ApplySelectedAsync(Avalonia.Controls.Window dialog)
    {
        if (SelectedProfile == null) return;
        AppliedProfileName = SelectedProfile.Name;
        Applied = true;
        ProfileApplyRequested?.Invoke(this, SelectedProfile.Name);
        dialog.Close();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedProfile == null) return;
        var confirmed = await _dialogService.ConfirmAsync("Delete Profile",
            $"Delete profile '{SelectedProfile.Name}'?");
        if (!confirmed) return;

        await _profileService.DeleteProfileAsync(SelectedProfile.Name, _profilesPath);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = null;
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        if (SelectedProfile == null) return;

        var exportTitle = _loc.GetString("export.choose_format.title");
        var choice = await _dialogService.ShowExportOptionsAsync(exportTitle);

        if (choice == ExportOption.Cancel) return;

        if (choice == ExportOption.Json)
        {
            var path = await _dialogService.SaveFileAsync(exportTitle,
                "JSON Profile", ["json"], SelectedProfile.Name + ".json");
            if (path == null) return;
            await _profileService.ExportProfileAsync(SelectedProfile, path);
            return;
        }

        // ZIP
        var confirmed = await _dialogService.ConfirmAsync(
            _loc.GetString("export.zip_warning.title"),
            _loc.GetString("export.zip_warning.message"));
        if (!confirmed) return;

        var gamePath = _settings.Settings.GamePath;
        if (string.IsNullOrEmpty(gamePath)) return;

        var zipPath = await _dialogService.SaveFileAsync(exportTitle,
            "ZIP Modpack", ["zip"], SelectedProfile.Name + ".zip");
        if (zipPath == null) return;

        await _profileService.ExportProfileAsZipAsync(SelectedProfile, gamePath, zipPath);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = await _dialogService.OpenFileAsync("Import Profile", "JSON Profile", ["json"]);
        if (path == null) return;

        try
        {
            var profile = await _profileService.ImportProfileAsync(path);
            await _profileService.SaveProfileAsync(profile, _profilesPath);
            await LoadProfilesAsync(_profilesPath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Import Failed", ex.Message);
        }
    }

    [RelayCommand]
    private void Cancel(Avalonia.Controls.Window dialog) => dialog.Close();
}
