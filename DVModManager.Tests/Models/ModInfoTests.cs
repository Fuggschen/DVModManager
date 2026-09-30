using DVModManager.Models;
using Xunit;

namespace DVModManager.Tests.Models;

public class ModInfoTests
{
    [Fact]
    public void EffectiveDisplayName_WhenDisplayNameIsSet_ReturnsDisplayName()
    {
        var mod = new ModInfo { Id = "MyMod", DisplayName = "My Mod Display Name" };
        Assert.Equal("My Mod Display Name", mod.EffectiveDisplayName);
    }

    [Theory]
    [InlineData("", "MyMod")]
    [InlineData(null, "MyMod")]
    [InlineData("   ", "MyMod")]
    public void EffectiveDisplayName_WhenDisplayNameIsBlank_ReturnsId(string? displayName, string expected)
    {
        var mod = new ModInfo { Id = "MyMod", DisplayName = displayName! };
        Assert.Equal(expected, mod.EffectiveDisplayName);
    }

    [Fact]
    public void Deserialization_SupportsCommentsAndTrailingCommas()
    {
        var json = """
        {
            // Comment about Id
            "Id": "TestMod",
            "DisplayName": "Test Mod",
            "Author": "TestAuthor",
            "Version": "1.2.3",
            "HomePage": "https://example.com",
            "Repository": "https://github.com/example/repo",
            "Description": "Test description",
            "Requirements": ["ReqMod1", "ReqMod2",],
        }
        """;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var mod = System.Text.Json.JsonSerializer.Deserialize<ModInfo>(json, options);

        Assert.NotNull(mod);
        Assert.Equal("TestMod", mod.Id);
        Assert.Equal("Test Mod", mod.DisplayName);
        Assert.Equal("TestAuthor", mod.Author);
        Assert.Equal("1.2.3", mod.Version);
        Assert.Equal("https://example.com", mod.HomePage);
        Assert.Equal("https://github.com/example/repo", mod.Repository);
        Assert.Equal("Test description", mod.Description);
        Assert.Equal(new[] { "ReqMod1", "ReqMod2" }, mod.Requirements);
    }
}
