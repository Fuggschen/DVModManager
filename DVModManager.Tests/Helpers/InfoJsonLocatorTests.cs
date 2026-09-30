using DVModManager.Helpers;
using Xunit;

namespace DVModManager.Tests.Helpers;

public class InfoJsonLocatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Locate_WhenFolderIsNullOrEmpty_ReturnsNull(string? folder)
    {
        var found = InfoJsonLocator.Locate(folder!);
        Assert.Null(found);
    }

    [Fact]
    public void Locate_WhenFolderDoesNotExist_ReturnsNull()
    {
        string nonExistent = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid());
        var found = InfoJsonLocator.Locate(nonExistent);
        Assert.Null(found);
    }

    [Fact]
    public void Locate_WhenInfoJsonExistsExactCase_ReturnsPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string infoJson = Path.Combine(tempDir, "Info.json");
        File.WriteAllText(infoJson, "{}");

        try
        {
            string? found = InfoJsonLocator.Locate(tempDir);
            Assert.NotNull(found);
            Assert.Equal(infoJson, found);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("info.json")]
    [InlineData("INFO.JSON")]
    [InlineData("Info.JSON")]
    public void Locate_WhenInfoJsonDifferentCasing_ReturnsPath(string fileName)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string infoJson = Path.Combine(tempDir, fileName);
        File.WriteAllText(infoJson, "{}");

        try
        {
            string? found = InfoJsonLocator.Locate(tempDir);
            Assert.NotNull(found);
            // On case-insensitive filesystems (Windows) the exact-case "Info.json" probe
            // matches first, so the returned casing can differ from what was written.
            Assert.Equal(infoJson, found, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Locate_WhenNoInfoJsonExists_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "other.json"), "{}");

        try
        {
            string? found = InfoJsonLocator.Locate(tempDir);
            Assert.Null(found);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
