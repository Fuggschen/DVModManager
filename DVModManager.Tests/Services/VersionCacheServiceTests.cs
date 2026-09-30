using DVModManager.Models;
using DVModManager.Services;
using Xunit;

namespace DVModManager.Tests.Services;

public class VersionCacheServiceTests
{
    private readonly VersionCacheService _service;

    public VersionCacheServiceTests()
    {
        _service = new VersionCacheService();
    }

    [Fact]
    public async Task ArchiveCurrentVersionAsync_CreatesZipAndUpdatesManifest()
    {
        var tempStorage = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempModDir = Path.Combine(Path.GetTempPath(), "ModDir_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);
        File.WriteAllText(Path.Combine(tempModDir, "Info.json"), "{}");
        File.WriteAllText(Path.Combine(tempModDir, "mod.dll"), "fake binary content");

        var mod = new ModInfo
        {
            Id = "TestMod",
            Version = "1.0.0",
            FolderPath = tempModDir
        };

        try
        {
            await _service.ArchiveCurrentVersionAsync(mod, tempStorage);

            var history = await _service.GetVersionHistoryAsync("TestMod", tempStorage);
            Assert.Single(history);
            Assert.Equal("1.0.0", history[0].Version);

            var archivePath = await _service.GetVersionArchivePathAsync("TestMod", "1.0.0", tempStorage);
            Assert.NotNull(archivePath);
            Assert.True(File.Exists(archivePath));
        }
        finally
        {
            if (Directory.Exists(tempStorage)) Directory.Delete(tempStorage, true);
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }

    [Fact]
    public async Task DeleteVersionAsync_RemovesZipAndManifestEntry()
    {
        var tempStorage = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempModDir = Path.Combine(Path.GetTempPath(), "ModDir_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);
        File.WriteAllText(Path.Combine(tempModDir, "Info.json"), "{}");

        var mod1 = new ModInfo { Id = "TestMod", Version = "1.0.0", FolderPath = tempModDir };
        var mod2 = new ModInfo { Id = "TestMod", Version = "2.0.0", FolderPath = tempModDir };

        try
        {
            await _service.ArchiveCurrentVersionAsync(mod1, tempStorage);
            await _service.ArchiveCurrentVersionAsync(mod2, tempStorage);

            var historyBefore = await _service.GetVersionHistoryAsync("TestMod", tempStorage);
            Assert.Equal(2, historyBefore.Count);

            await _service.DeleteVersionAsync("TestMod", "1.0.0", tempStorage);

            var historyAfter = await _service.GetVersionHistoryAsync("TestMod", tempStorage);
            Assert.Single(historyAfter);
            Assert.Equal("2.0.0", historyAfter[0].Version);

            var deletedPath = await _service.GetVersionArchivePathAsync("TestMod", "1.0.0", tempStorage);
            Assert.Null(deletedPath);
        }
        finally
        {
            if (Directory.Exists(tempStorage)) Directory.Delete(tempStorage, true);
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }

    [Fact]
    public async Task GetCacheSizeAsync_And_PruneCacheAsync_WorkCorrectly()
    {
        var tempStorage = Path.Combine(Path.GetTempPath(), "Storage_" + Guid.NewGuid());
        var tempModDir = Path.Combine(Path.GetTempPath(), "ModDir_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);
        File.WriteAllText(Path.Combine(tempModDir, "data.bin"), new string('A', 10000));

        var mod1 = new ModInfo { Id = "ModA", Version = "1.0.0", FolderPath = tempModDir };
        var mod2 = new ModInfo { Id = "ModA", Version = "2.0.0", FolderPath = tempModDir };

        try
        {
            await _service.ArchiveCurrentVersionAsync(mod1, tempStorage);
            await _service.ArchiveCurrentVersionAsync(mod2, tempStorage);

            var size = await _service.GetCacheSizeAsync(tempStorage);
            Assert.True(size > 0);

            // Prune to 0 bytes max size -> should remove files
            await _service.PruneCacheAsync(tempStorage, 0);

            var sizeAfter = await _service.GetCacheSizeAsync(tempStorage);
            Assert.Equal(0, sizeAfter);
        }
        finally
        {
            if (Directory.Exists(tempStorage)) Directory.Delete(tempStorage, true);
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }
}
