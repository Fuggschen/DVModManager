using DVModManager.Helpers;
using Xunit;

namespace DVModManager.Tests.Helpers;

public class PathSafetyTests
{
    [Theory]
    [InlineData("ValidMod", true)]
    [InlineData("Valid-Mod_123", true)]
    [InlineData("Mod.Name", true)]
    [InlineData("AnotherMod123", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("  ", false)]
    [InlineData(" LeadingSpace", false)]
    [InlineData("TrailingSpace ", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("invalid/path", false)]
    [InlineData("invalid\\path", false)]
    [InlineData("invalid:path", false)]
    [InlineData("invalid*path", false)]
    [InlineData("invalid?path", false)]
    [InlineData("invalid<path", false)]
    [InlineData("invalid>path", false)]
    [InlineData("invalid|path", false)]
    [InlineData("invalid\"path", false)]
    [InlineData("invalid\0path", false)]
    public void IsValidModId_ShouldReturnExpectedResult(string? id, bool expected)
    {
        Assert.Equal(expected, PathSafety.IsValidModId(id));
    }

    [Theory]
    [InlineData("Root", "Root/Sub", true)]
    [InlineData("Root", "Root/Sub/Deep", true)]
    [InlineData("Root", "Root", false)]
    [InlineData("Root", "Root/../Sibling", false)]
    [InlineData("Root", "RootOther", false)]
    public void IsStrictlyUnder_ShouldReturnExpectedResult(string root, string candidate, bool expected)
    {
        string rootPath = Path.GetFullPath(root);
        string candidatePath = Path.GetFullPath(candidate.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expected, PathSafety.IsStrictlyUnder(rootPath, candidatePath));
    }

    [Fact]
    public void SafeCombine_ValidPath_ReturnsCombinedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "TestRoot");
        string id = "ValidMod";
        string result = PathSafety.SafeCombine(root, id);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "ValidMod"), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("invalid/path")]
    [InlineData("invalid\\path")]
    [InlineData("invalid:path")]
    public void SafeCombine_InvalidId_ThrowsInvalidOperationException(string? invalidId)
    {
        string root = Path.Combine(Path.GetTempPath(), "TestRoot");
        Assert.Throws<InvalidOperationException>(() => PathSafety.SafeCombine(root, invalidId));
    }
}
