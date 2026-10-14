using System.IO.Compression;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ModInstallServiceTests : IDisposable
{
    private class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StubHttpMessageHandler(byte[] payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static byte[] CreateZipFromDirectory(string sourceDir)
    {
        using var stream = new MemoryStream();
        ZipFile.CreateFromDirectory(sourceDir, stream);
        return stream.ToArray();
    }

    private readonly VersionCacheService _versionCache;
    private readonly SettingsService _settingsService;
    private readonly ModInstallService _service;

    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), "Settings_" + Guid.NewGuid().ToString("N"));

    public ModInstallServiceTests()
    {
        _versionCache = new VersionCacheService();
        _settingsService = TestData.CreateSettingsService(_settingsDir);
        _service = new ModInstallService(
            _versionCache,
            _settingsService,
            new DummyHttpClientFactory(),
            NullLogger<ModInstallService>.Instance);
    }

    public void Dispose() => TestData.DeleteDirectory(_settingsDir);

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

    [Fact]
    public async Task UpdateModAsync_PreservesSettingsAndModGeneratedFiles()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var tempStoragePath = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "Source_" + Guid.NewGuid());
        var liveDir = Path.Combine(tempGamePath, "Mods", "TestMod");

        // Existing install with UMM-style runtime-generated files
        Directory.CreateDirectory(Path.Combine(liveDir, "Cache"));
        File.WriteAllText(Path.Combine(liveDir, "Info.json"),
            """{"Id": "TestMod", "DisplayName": "Test Mod", "Version": "1.0.0"}""");
        File.WriteAllText(Path.Combine(liveDir, "TestMod.dll"), "old-dll");
        File.WriteAllText(Path.Combine(liveDir, "Settings.xml"), "<Settings>user</Settings>");
        File.WriteAllText(Path.Combine(liveDir, "Cache", "generated.bin"), "runtime-data");

        // The new version ships only its own files
        Directory.CreateDirectory(tempSourceDir);
        File.WriteAllText(Path.Combine(tempSourceDir, "Info.json"),
            """{"Id": "TestMod", "DisplayName": "Test Mod", "Version": "2.0.0"}""");
        File.WriteAllText(Path.Combine(tempSourceDir, "TestMod.dll"), "new-dll");
        var zipBytes = CreateZipFromDirectory(tempSourceDir);

        var modInfo = new ModInfo
        {
            Id = "TestMod",
            Version = "1.0.0",
            IsActive = true,
            FolderPath = liveDir,
            State = ModState.Active
        };
        var update = new ModUpdateInfo
        {
            ModId = "TestMod",
            CurrentVersion = "1.0.0",
            LatestVersion = "2.0.0",
            DownloadUrl = "https://example.invalid/TestMod.zip"
        };

        var service = new ModInstallService(
            _versionCache,
            _settingsService,
            new StubHttpClientFactory(new StubHttpMessageHandler(zipBytes)),
            NullLogger<ModInstallService>.Instance);

        try
        {
            var ok = await service.UpdateModAsync(modInfo, update, tempGamePath, tempStoragePath);

            Assert.True(ok);
            Assert.Equal("<Settings>user</Settings>", File.ReadAllText(Path.Combine(liveDir, "Settings.xml")));
            Assert.Equal("runtime-data", File.ReadAllText(Path.Combine(liveDir, "Cache", "generated.bin")));
            Assert.Contains("2.0.0", File.ReadAllText(Path.Combine(liveDir, "Info.json")));
            Assert.False(Directory.Exists(liveDir + ".update_staging"));
            Assert.False(Directory.Exists(liveDir + ".update_backup"));
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
            if (Directory.Exists(tempStoragePath)) Directory.Delete(tempStoragePath, true);
            if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
        }
    }

    [Fact]
    public async Task UpdateModAsync_FilesFromNewVersionReplaceExistingFiles()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var tempStoragePath = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "Source_" + Guid.NewGuid());
        var liveDir = Path.Combine(tempGamePath, "Mods", "TestMod");

        Directory.CreateDirectory(liveDir);
        File.WriteAllText(Path.Combine(liveDir, "Info.json"),
            """{"Id": "TestMod", "DisplayName": "Test Mod", "Version": "1.0.0"}""");
        File.WriteAllText(Path.Combine(liveDir, "TestMod.dll"), "old-dll");
        File.WriteAllText(Path.Combine(liveDir, "shared.cfg"), "old-config");

        Directory.CreateDirectory(tempSourceDir);
        File.WriteAllText(Path.Combine(tempSourceDir, "Info.json"),
            """{"Id": "TestMod", "DisplayName": "Test Mod", "Version": "2.0.0"}""");
        File.WriteAllText(Path.Combine(tempSourceDir, "TestMod.dll"), "new-dll");
        File.WriteAllText(Path.Combine(tempSourceDir, "shared.cfg"), "new-config");
        var zipBytes = CreateZipFromDirectory(tempSourceDir);

        var modInfo = new ModInfo
        {
            Id = "TestMod",
            Version = "1.0.0",
            IsActive = true,
            FolderPath = liveDir,
            State = ModState.Active
        };
        var update = new ModUpdateInfo
        {
            ModId = "TestMod",
            CurrentVersion = "1.0.0",
            LatestVersion = "2.0.0",
            DownloadUrl = "https://example.invalid/TestMod.zip"
        };

        var service = new ModInstallService(
            _versionCache,
            _settingsService,
            new StubHttpClientFactory(new StubHttpMessageHandler(zipBytes)),
            NullLogger<ModInstallService>.Instance);

        try
        {
            var ok = await service.UpdateModAsync(modInfo, update, tempGamePath, tempStoragePath);

            Assert.True(ok);
            Assert.Equal("new-dll", File.ReadAllText(Path.Combine(liveDir, "TestMod.dll")));
            Assert.Equal("new-config", File.ReadAllText(Path.Combine(liveDir, "shared.cfg")));
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
            if (Directory.Exists(tempStoragePath)) Directory.Delete(tempStoragePath, true);
            if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
        }
    }

    [Fact]
    public async Task InstallFromArchiveAsync_PreservesModGeneratedFilesOnReinstall()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var tempStoragePath = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempZip = Path.Combine(Path.GetTempPath(), "TestMod_" + Guid.NewGuid() + ".zip");
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "Source_" + Guid.NewGuid());
        var liveDir = Path.Combine(tempGamePath, "Mods", "InstalledMod");

        // Existing install with a runtime-generated settings file
        Directory.CreateDirectory(liveDir);
        File.WriteAllText(Path.Combine(liveDir, "Info.json"),
            """{"Id": "InstalledMod", "DisplayName": "Installed Mod", "Version": "1.0.0"}""");
        File.WriteAllText(Path.Combine(liveDir, "Settings.xml"), "<Settings>user</Settings>");

        Directory.CreateDirectory(tempSourceDir);
        File.WriteAllText(Path.Combine(tempSourceDir, "Info.json"),
            """{"Id": "InstalledMod", "DisplayName": "Installed Mod", "Version": "2.0.0"}""");
        File.WriteAllText(Path.Combine(tempSourceDir, "InstalledMod.dll"), "new-dll");
        ZipFile.CreateFromDirectory(tempSourceDir, tempZip);

        try
        {
            var installedMod = await _service.InstallFromArchiveAsync(tempZip, tempGamePath, tempStoragePath);

            Assert.NotNull(installedMod);
            Assert.Equal("<Settings>user</Settings>", File.ReadAllText(Path.Combine(liveDir, "Settings.xml")));
            Assert.Equal("new-dll", File.ReadAllText(Path.Combine(liveDir, "InstalledMod.dll")));
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
    public async Task RollbackToVersionAsync_PreservesModGeneratedFiles()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var tempStoragePath = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var liveDir = Path.Combine(tempGamePath, "Mods", "TestMod");

        Directory.CreateDirectory(liveDir);
        File.WriteAllText(Path.Combine(liveDir, "Info.json"),
            """{"Id": "TestMod", "DisplayName": "Test Mod", "Version": "1.0.0"}""");
        File.WriteAllText(Path.Combine(liveDir, "TestMod.dll"), "v1-dll");
        var v1 = new ModInfo { Id = "TestMod", Version = "1.0.0", FolderPath = liveDir };
        await _versionCache.ArchiveCurrentVersionAsync(v1, tempStoragePath);

        File.WriteAllText(Path.Combine(liveDir, "Info.json"),
            """{"Id": "TestMod", "DisplayName": "Test Mod", "Version": "2.0.0"}""");
        File.WriteAllText(Path.Combine(liveDir, "TestMod.dll"), "v2-dll");
        File.WriteAllText(Path.Combine(liveDir, "Settings.xml"), "<Settings>user</Settings>");

        try
        {
            var ok = await _service.RollbackToVersionAsync("TestMod", "1.0.0", tempGamePath, tempStoragePath);

            Assert.True(ok);
            Assert.Equal("v1-dll", File.ReadAllText(Path.Combine(liveDir, "TestMod.dll")));
            Assert.Equal("<Settings>user</Settings>", File.ReadAllText(Path.Combine(liveDir, "Settings.xml")));
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
            if (Directory.Exists(tempStoragePath)) Directory.Delete(tempStoragePath, true);
        }
    }
}
