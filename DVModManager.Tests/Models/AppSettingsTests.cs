using System.Text.Json;
using DVModManager.Models;
using Xunit;

namespace DVModManager.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var settings = new AppSettings();

        Assert.Null(settings.GamePath);
        Assert.Equal("Dark", settings.ThemeVariant);
        Assert.Equal("en", settings.Language);
        Assert.True(settings.AutoCheckUpdatesOnStartup);
        Assert.True(settings.EnableVersionArchiving);
        Assert.Equal(5L * 1024 * 1024 * 1024, settings.MaxCacheSizeBytes);
        Assert.Empty(settings.ModGroups);
        Assert.Empty(settings.CollapsedGroupIdsAvailable);
        Assert.Empty(settings.CollapsedGroupIdsActive);
        Assert.Null(settings.CollapsedGroupIds);
    }

    [Fact]
    public void DerivedPaths_CombineWithStoragePath()
    {
        var settings = new AppSettings
        {
            StoragePath = Path.Combine(Path.GetTempPath(), "DVMM_Test")
        };

        Assert.Equal(Path.Combine(settings.StoragePath, "versions"), settings.VersionsPath);
        Assert.Equal(Path.Combine(settings.StoragePath, "downloads"), settings.DownloadsPath);
        Assert.Equal(Path.Combine(settings.StoragePath, "logs"), settings.LogsPath);
    }

    [Fact]
    public void JsonSerialization_PreservesAllFields()
    {
        var settings = new AppSettings
        {
            GamePath = "/path/to/game",
            StoragePath = "/path/to/storage",
            ThemeVariant = "Light",
            Language = "de",
            AutoCheckUpdatesOnStartup = false,
            EnableVersionArchiving = false,
            MaxCacheSizeBytes = 2L * 1024 * 1024 * 1024,
            ModGroups =
            [
                new ModGroup
                {
                    Id = "grp1",
                    Name = "Gameplay",
                    Panel = "active",
                    ModIds = ["ModA", "ModB"]
                }
            ],
            CollapsedGroupIdsAvailable = ["grp1"],
            CollapsedGroupIdsActive = ["grp2"]
        };

        var json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(settings.GamePath, deserialized.GamePath);
        Assert.Equal(settings.StoragePath, deserialized.StoragePath);
        Assert.Equal(settings.ThemeVariant, deserialized.ThemeVariant);
        Assert.Equal(settings.Language, deserialized.Language);
        Assert.Equal(settings.AutoCheckUpdatesOnStartup, deserialized.AutoCheckUpdatesOnStartup);
        Assert.Equal(settings.EnableVersionArchiving, deserialized.EnableVersionArchiving);
        Assert.Equal(settings.MaxCacheSizeBytes, deserialized.MaxCacheSizeBytes);
        Assert.Single(deserialized.ModGroups);
        Assert.Equal("Gameplay", deserialized.ModGroups[0].Name);
        Assert.Equal("active", deserialized.ModGroups[0].Panel);
        Assert.Equal(new[] { "ModA", "ModB" }, deserialized.ModGroups[0].ModIds);
        Assert.Contains("grp1", deserialized.CollapsedGroupIdsAvailable);
        Assert.Contains("grp2", deserialized.CollapsedGroupIdsActive);
    }
}
