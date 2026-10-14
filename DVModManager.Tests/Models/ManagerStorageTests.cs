using DVModManager.Models;
using Xunit;

namespace DVModManager.Tests.Models;

public class ManagerStorageTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal("DVModManager", ManagerStorage.DirectoryName);
        Assert.Equal("settings.json", ManagerStorage.SettingsFileName);
        Assert.Equal("profiles", ManagerStorage.ProfilesDirectoryName);
        Assert.Equal("Default", ManagerStorage.DefaultProfileName);
    }

    [Theory]
    [InlineData("MyProfile", "MyProfile.json")]
    [InlineData("Profile with spaces", "Profile with spaces.json")]
    [InlineData("Profile/With/Slashes", "Profile_With_Slashes.json")]
    [InlineData("Profile\\With\\Backslashes", "Profile_With_Backslashes.json")]
    [InlineData("Profile:With*Invalid?Chars\"<In>|Name", "Profile_With_Invalid_Chars__In__Name.json")]
    [InlineData("Profile\tWith\nControl\rChars", "Profile_With_Control_Chars.json")]
    public void ProfileFileName_SanitizesInvalidCharactersAndAppendsExtension(string input, string expected)
    {
        var result = ManagerStorage.ProfileFileName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConfigDirectory_IsPathRooted()
    {
        var configDir = ManagerStorage.ConfigDirectory;
        Assert.True(Path.IsPathRooted(configDir));
        Assert.EndsWith("DVModManager", configDir);
    }

    [Fact]
    public void SettingsFilePath_CombinesConfigDirectoryAndSettingsFileName()
    {
        var settingsPath = ManagerStorage.SettingsFilePath;
        Assert.Equal(Path.Combine(ManagerStorage.ConfigDirectory, ManagerStorage.SettingsFileName), settingsPath);
    }
}
