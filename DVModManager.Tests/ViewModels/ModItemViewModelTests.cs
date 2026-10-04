using DVModManager.Models;
using DVModManager.ViewModels;
using Xunit;

namespace DVModManager.Tests.ViewModels;

public class ModItemViewModelTests
{
    [Fact]
    public void Constructor_InitializesPropertiesFromModInfo()
    {
        var modInfo = new ModInfo
        {
            Id = "TestMod",
            DisplayName = "Test Mod Name",
            Author = "AuthorName",
            Version = "1.0.0",
            IsActive = true,
            State = ModState.Active,
            HasMetadata = true,
            HomePage = "https://example.com",
            Repository = "https://github.com/example/repo",
            Description = "A description",
            Requirements = ["Req1", "Req2"]
        };

        using var vm = new ModItemViewModel(modInfo);

        Assert.Equal("TestMod", vm.Id);
        Assert.Equal("Test Mod Name", vm.DisplayName);
        Assert.Equal("AuthorName", vm.Author);
        Assert.Equal("1.0.0", vm.Version);
        Assert.True(vm.IsActive);
        Assert.Equal(ModState.Active, vm.State);
        Assert.True(vm.HasMetadata);
        Assert.Equal("https://example.com", vm.HomePage);
        Assert.Equal("https://github.com/example/repo", vm.Repository);
        Assert.Equal("A description", vm.Description);
        Assert.Equal(new[] { "Req1", "Req2" }, vm.Requirements);
        Assert.Equal("Active", vm.StatusLabel);
        Assert.False(vm.IsMissing);
    }

    [Fact]
    public void CreateMissing_CreatesGhostViewModel()
    {
        using var vm = ModItemViewModel.CreateMissing("MissingMod");

        Assert.Equal("MissingMod", vm.Id);
        Assert.True(vm.IsMissing);
        Assert.Equal(ModState.Missing, vm.State);
    }

    [Fact]
    public void ApplyUpdate_UpdatesStateAndProperties()
    {
        var modInfo = new ModInfo
        {
            Id = "TestMod",
            Version = "1.0.0",
            State = ModState.Active
        };

        using var vm = new ModItemViewModel(modInfo);

        var update = new ModUpdateInfo
        {
            ModId = "TestMod",
            CurrentVersion = "1.0.0",
            LatestVersion = "1.1.0",
            Source = "github"
        };

        vm.ApplyUpdate(update);

        Assert.True(vm.HasUpdate);
        Assert.Equal("1.1.0", vm.UpdateVersion);
        Assert.Equal(ModState.UpdateAvailable, vm.State);
        Assert.Contains("1.1.0", vm.StatusLabel);
    }

    [Fact]
    public void DisplayRequirements_ReflectsMissingDependencyIds()
    {
        var modInfo = new ModInfo
        {
            Id = "TestMod",
            Requirements = ["ReqA", "ReqB"]
        };

        using var vm = new ModItemViewModel(modInfo);
        vm.MissingDependencyIds.Add("ReqB");
        vm.RefreshDisplayRequirements();

        var display = vm.DisplayRequirements;
        Assert.Equal(2, display.Count);

        var reqA = display.First(d => d.Requirement == "ReqA");
        Assert.False(reqA.IsMissing);

        var reqB = display.First(d => d.Requirement == "ReqB");
        Assert.True(reqB.IsMissing);
    }
}
