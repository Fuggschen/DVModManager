using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class GameDetectionServiceTests : IDisposable
{
    private readonly GameDetectionService _service;

    public GameDetectionServiceTests()
    {
        _service = new GameDetectionService(NullLogger<GameDetectionService>.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void ValidateGamePath_WhenValid_ReturnsTrue()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dataDir = Path.Combine(tempDir, "DerailValley_Data");
        Directory.CreateDirectory(dataDir);

        try
        {
            // Act
            bool isValid = _service.ValidateGamePath(tempDir);

            // Assert
            Assert.True(isValid);
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ValidateGamePath_WhenInvalid_ReturnsFalse()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act
            bool isValid = _service.ValidateGamePath(tempDir);

            // Assert
            Assert.False(isValid);
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateGamePath_WhenNullOrEmpty_ReturnsFalse(string? path)
    {
        bool isValid = _service.ValidateGamePath(path!);
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateGamePath_WhenNonExistentPath_ReturnsFalse()
    {
        string nonExistent = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());
        bool isValid = _service.ValidateGamePath(nonExistent);
        Assert.False(isValid);
    }

    [Fact]
    public void IsGameRunning_DoesNotThrow()
    {
        // Should execute cleanly without exceptions
        var running = _service.IsGameRunning();
        Assert.IsType<bool>(running);
    }
}
