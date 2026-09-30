using Avalonia.Controls;
using DVModManager.Models;
using DVModManager.Services;
using DVModManager.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.ViewModels;

public class SettingsViewModelTests
{
    private class DummyDialogService : IDialogService
    {
        public void SetOwner(Window owner) { }
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> OpenFileAsync(string title, string filterName, string[] extensions) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string filterName, string[] extensions) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string filterName, string[] extensions, string? defaultFileName = null) => Task.FromResult<string?>(null);
        public Task<ExportOption> ShowExportOptionsAsync(string title) => Task.FromResult(ExportOption.Json);
        public Task<bool> ShowModpackImportConfirmAsync(string title, ModProfile profile) => Task.FromResult(true);
        public Task<bool> ShowFailedDownloadsAsync(string title, IReadOnlyList<(string ModId, string? HomePageUrl)> failedMods) => Task.FromResult(false);
    }

    [Fact]
    public void Load_And_Apply_CorrectlyMapsAppSettings()
    {
        var gameDetection = new GameDetectionService(NullLogger<GameDetectionService>.Instance);
        var dialogService = new DummyDialogService();
        var vm = new SettingsViewModel(dialogService, gameDetection);

        var originalSettings = new AppSettings
        {
            GamePath = "/path/to/game",
            StoragePath = "/path/to/storage",
            AutoCheckUpdatesOnStartup = true,
            EnableVersionArchiving = true,
            MaxCacheSizeBytes = 10L * 1024 * 1024 * 1024,
            ThemeVariant = "Light",
            Language = "de"
        };

        vm.Load(originalSettings);

        Assert.Equal("/path/to/game", vm.GamePath);
        Assert.Equal("/path/to/storage", vm.StoragePath);
        Assert.True(vm.AutoCheckUpdates);
        Assert.True(vm.EnableVersionArchiving);
        Assert.Equal(10, vm.MaxCacheGb);
        Assert.Equal("Light", vm.ThemeVariant);
        Assert.Equal("de", vm.Language);
        Assert.False(vm.Saved);

        // Modify VM properties
        vm.GamePath = "  "; // should turn to null in Apply
        vm.StoragePath = "/new/storage";
        vm.AutoCheckUpdates = false;
        vm.EnableVersionArchiving = false;
        vm.MaxCacheGb = 3;
        vm.ThemeVariant = "Dark";
        vm.Language = "en";

        var targetSettings = new AppSettings();
        vm.Apply(targetSettings);

        Assert.Null(targetSettings.GamePath);
        Assert.Equal("/new/storage", targetSettings.StoragePath);
        Assert.False(targetSettings.AutoCheckUpdatesOnStartup);
        Assert.False(targetSettings.EnableVersionArchiving);
        Assert.Equal(3L * 1024 * 1024 * 1024, targetSettings.MaxCacheSizeBytes);
        Assert.Equal("Dark", targetSettings.ThemeVariant);
        Assert.Equal("en", targetSettings.Language);
    }

    [Fact]
    public void AvailableLanguages_ContainsExpectedEntries()
    {
        var gameDetection = new GameDetectionService(NullLogger<GameDetectionService>.Instance);
        var dialogService = new DummyDialogService();
        var vm = new SettingsViewModel(dialogService, gameDetection);

        Assert.Contains("en", vm.AvailableLanguages);
        Assert.Contains("de", vm.AvailableLanguages);
        Assert.Contains("fr", vm.AvailableLanguages);
        Assert.Contains("zh", vm.AvailableLanguages);

        Assert.Equal("English", vm.LanguageDisplayNames["en"]);
        Assert.Equal("Deutsch", vm.LanguageDisplayNames["de"]);
    }
}
