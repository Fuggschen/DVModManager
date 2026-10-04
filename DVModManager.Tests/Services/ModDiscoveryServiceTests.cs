using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ModDiscoveryServiceTests
{
    private readonly ModDiscoveryService _service;

    public ModDiscoveryServiceTests()
    {
        _service = new ModDiscoveryService(NullLogger<ModDiscoveryService>.Instance);
    }

    [Fact]
    public async Task ScanAllModsAsync_DiscoversActiveAndInactiveMods()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var activeModsDir = Path.Combine(tempGamePath, "Mods");
        var inactiveModsDir = Path.Combine(tempGamePath, "Mods.inactive");

        Directory.CreateDirectory(Path.Combine(activeModsDir, "ModA"));
        File.WriteAllText(Path.Combine(activeModsDir, "ModA", "Info.json"),
            """{"Id": "ModA", "DisplayName": "Mod A", "Version": "1.0.0"}""");

        Directory.CreateDirectory(Path.Combine(inactiveModsDir, "ModB"));
        File.WriteAllText(Path.Combine(inactiveModsDir, "ModB", "Info.json"),
            """{"Id": "ModB", "DisplayName": "Mod B", "Version": "2.0.0"}""");

        // Staging folder that should be ignored
        Directory.CreateDirectory(Path.Combine(activeModsDir, ".staging"));

        try
        {
            var mods = await _service.ScanAllModsAsync(tempGamePath);

            Assert.Equal(2, mods.Count);

            var modA = Assert.Single(mods, m => m.Id == "ModA");
            Assert.True(modA.IsActive);
            Assert.Equal(ModState.Active, modA.State);
            Assert.True(modA.HasMetadata);

            var modB = Assert.Single(mods, m => m.Id == "ModB");
            Assert.False(modB.IsActive);
            Assert.Equal(ModState.Inactive, modB.State);
            Assert.True(modB.HasMetadata);
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
        }
    }

    [Fact]
    public async Task ParseModInfoAsync_WhenInfoJsonMissing_SetsNoMetadata()
    {
        var tempModDir = Path.Combine(Path.GetTempPath(), "OrphanMod_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);

        try
        {
            var modInfo = await _service.ParseModInfoAsync(tempModDir, isActive: true);

            Assert.NotNull(modInfo);
            Assert.False(modInfo.HasMetadata);
            Assert.Equal(ModState.NoMetadata, modInfo.State);
            Assert.Equal(Path.GetFileName(tempModDir), modInfo.Id);
        }
        finally
        {
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }

    [Fact]
    public async Task ParseModInfoAsync_WhenInfoJsonCorrupt_FallsBackGracefully()
    {
        var tempModDir = Path.Combine(Path.GetTempPath(), "CorruptMod_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);
        File.WriteAllText(Path.Combine(tempModDir, "Info.json"), "{ invalid json ");

        try
        {
            var modInfo = await _service.ParseModInfoAsync(tempModDir, isActive: false);

            Assert.NotNull(modInfo);
            Assert.False(modInfo.HasMetadata);
            Assert.Equal(ModState.NoMetadata, modInfo.State);
        }
        finally
        {
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }
}
