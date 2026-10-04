using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ProfileServiceTests
{
    private readonly ProfileService _service;

    public ProfileServiceTests()
    {
        _service = new ProfileService(NullLogger<ProfileService>.Instance);
    }

    [Fact]
    public async Task Save_Get_And_DeleteProfileAsync_WorkCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Profiles_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var profile = new ModProfile
            {
                Name = "MyTestProfile",
                Mods =
                [
                    new ProfileModEntry { ModId = "Mod1", IsActive = true, Version = "1.0.0" },
                    new ProfileModEntry { ModId = "Mod2", IsActive = false, Version = "2.1.0" }
                ]
            };

            await _service.SaveProfileAsync(profile, tempDir);

            var retrieved = await _service.GetProfileAsync("MyTestProfile", tempDir);
            Assert.NotNull(retrieved);
            Assert.Equal("MyTestProfile", retrieved.Name);
            Assert.Equal(2, retrieved.Mods.Count);

            var allProfiles = await _service.GetProfilesAsync(tempDir);
            Assert.Single(allProfiles);

            await _service.DeleteProfileAsync("MyTestProfile", tempDir);
            var afterDelete = await _service.GetProfileAsync("MyTestProfile", tempDir);
            Assert.Null(afterDelete);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetUniqueProfileNameAsync_GeneratesIndexedNamesWhenDuplicatesExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Profiles_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var unique1 = await _service.GetUniqueProfileNameAsync("Default", tempDir);
            Assert.Equal("Default", unique1);

            await _service.SaveProfileAsync(new ModProfile { Name = "Default" }, tempDir);

            var unique2 = await _service.GetUniqueProfileNameAsync("Default", tempDir);
            Assert.Equal("Default (2)", unique2);

            await _service.SaveProfileAsync(new ModProfile { Name = "Default (2)" }, tempDir);

            var unique3 = await _service.GetUniqueProfileNameAsync("Default", tempDir);
            Assert.Equal("Default (3)", unique3);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ComputeDiff_ComputesActivationsDeactivationsRollbacksAndDownloads()
    {
        var profile = new ModProfile
        {
            Name = "TargetProfile",
            Mods =
            [
                new ProfileModEntry { ModId = "ModToActivate", IsActive = true, Version = "1.0.0" },
                new ProfileModEntry { ModId = "ModToDeactivate", IsActive = false, Version = "1.0.0" },
                new ProfileModEntry { ModId = "ModToRollback", IsActive = true, Version = "1.0.0" },
                new ProfileModEntry { ModId = "ModToRedownload", IsActive = true, Version = "1.0.0", RepositoryUrl = "https://example.com/repo" },
                new ProfileModEntry { ModId = "ModToDownload", IsActive = true, Version = "1.0.0", RepositoryUrl = "https://example.com/repo" }
            ]
        };

        var currentMods = new List<ModInfo>
        {
            new() { Id = "ModToActivate", IsActive = false, Version = "1.0.0" },
            new() { Id = "ModToDeactivate", IsActive = true, Version = "1.0.0" },
            new() { Id = "ModNotInProfile", IsActive = true, Version = "1.0.0" },
            new() { Id = "ModToRollback", IsActive = true, Version = "2.0.0" },
            new() { Id = "ModToRedownload", IsActive = true, Version = "2.0.0" }
        };

        var diff = _service.ComputeDiff(profile, currentMods);

        // ToActivate: ModToActivate
        Assert.Contains(diff.ToActivate, m => m == "ModToActivate");

        // ToDeactivate: ModToDeactivate AND ModNotInProfile
        Assert.Contains(diff.ToDeactivate, m => m == "ModToDeactivate");
        Assert.Contains(diff.ToDeactivate, m => m == "ModNotInProfile");

        // ToRollback: ModToRollback
        Assert.Contains(diff.ToRollback, r => r.ModId == "ModToRollback" && r.ToVersion == "1.0.0");

        // ToRedownload: ModToRedownload
        Assert.Contains(diff.ToRedownload, r => r.ModId == "ModToRedownload" && r.Version == "1.0.0");

        // ToDownload: ModToDownload
        Assert.Contains(diff.ToDownload, d => d.ModId == "ModToDownload");
    }
}
