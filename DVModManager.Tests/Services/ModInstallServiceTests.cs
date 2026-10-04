using System.IO.Compression;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ModInstallServiceTests
{
    private class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private readonly VersionCacheService _versionCache;
    private readonly SettingsService _settingsService;
    private readonly ModInstallService _service;

    public ModInstallServiceTests()
    {
        _versionCache = new VersionCacheService();
        _settingsService = new SettingsService(NullLogger<SettingsService>.Instance);
        _service = new ModInstallService(
            _versionCache,
            _settingsService,
            new DummyHttpClientFactory(),
            NullLogger<ModInstallService>.Instance);
    }

    [Fact]
    public async Task ActivateModAsync_MovesFolderToModsDirectoryAndUpdatesModInfo()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var modsDir = Path.Combine(tempGamePath, "Mods");
        var inactiveModsDir = Path.Combine(tempGamePath, "Mods.inactive");

        Directory.CreateDirectory(Path.Combine(inactiveModsDir, "TestMod"));
        File.WriteAllText(Path.Combine(inactiveModsDir, "TestMod", "Info.json"), "{}");

        var modInfo = new ModInfo
        {
            Id = "TestMod",
            IsActive = false,
            FolderPath = Path.Combine(inactiveModsDir, "TestMod"),
            State = ModState.Inactive
        };

        try
        {
            await _service.ActivateModAsync(modInfo, tempGamePath);

            Assert.True(modInfo.IsActive);
            Assert.Equal(ModState.Active, modInfo.State);
            Assert.Equal(Path.Combine(modsDir, "TestMod"), modInfo.FolderPath);
            Assert.True(Directory.Exists(Path.Combine(modsDir, "TestMod")));
            Assert.False(Directory.Exists(Path.Combine(inactiveModsDir, "TestMod")));
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
        }
    }

    [Fact]
    public async Task DeactivateModAsync_MovesFolderToInactiveDirectoryAndUpdatesModInfo()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var modsDir = Path.Combine(tempGamePath, "Mods");
        var inactiveModsDir = Path.Combine(tempGamePath, "Mods.inactive");

        Directory.CreateDirectory(Path.Combine(modsDir, "TestMod"));
        File.WriteAllText(Path.Combine(modsDir, "TestMod", "Info.json"), "{}");

        var modInfo = new ModInfo
        {
            Id = "TestMod",
            IsActive = true,
            FolderPath = Path.Combine(modsDir, "TestMod"),
            State = ModState.Active
        };

        try
        {
            await _service.DeactivateModAsync(modInfo, tempGamePath);

            Assert.False(modInfo.IsActive);
            Assert.Equal(ModState.Inactive, modInfo.State);
            Assert.Equal(Path.Combine(inactiveModsDir, "TestMod"), modInfo.FolderPath);
            Assert.True(Directory.Exists(Path.Combine(inactiveModsDir, "TestMod")));
            Assert.False(Directory.Exists(Path.Combine(modsDir, "TestMod")));
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
        }
    }

    [Fact]
    public async Task InstallFromArchiveAsync_InstallsZipCorrectly()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var tempStoragePath = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempZip = Path.Combine(Path.GetTempPath(), "TestMod_" + Guid.NewGuid() + ".zip");
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "Source_" + Guid.NewGuid());

        Directory.CreateDirectory(tempSourceDir);
        File.WriteAllText(Path.Combine(tempSourceDir, "Info.json"),
            """{"Id": "InstalledMod", "DisplayName": "Installed Mod", "Version": "1.0.0"}""");
        ZipFile.CreateFromDirectory(tempSourceDir, tempZip);

        try
        {
            var installedMod = await _service.InstallFromArchiveAsync(tempZip, tempGamePath, tempStoragePath);

            Assert.NotNull(installedMod);
            Assert.Equal("InstalledMod", installedMod.Id);
            Assert.True(installedMod.IsActive);
            Assert.True(Directory.Exists(Path.Combine(tempGamePath, "Mods", "InstalledMod")));
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
            if (Directory.Exists(tempStoragePath)) Directory.Delete(tempStoragePath, true);
        }
    }

    [Fact]
    public async Task UninstallModAsync_DeletesModFolder()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var tempStoragePath = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var modDir = Path.Combine(tempGamePath, "Mods", "TestMod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "Info.json"), "{}");

        var modInfo = new ModInfo
        {
            Id = "TestMod",
            IsActive = true,
            FolderPath = modDir
        };

        try
        {
            await _service.UninstallModAsync(modInfo, tempGamePath, tempStoragePath, hardDelete: true);

            Assert.False(Directory.Exists(modDir));
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
            if (Directory.Exists(tempStoragePath)) Directory.Delete(tempStoragePath, true);
        }
    }
}
