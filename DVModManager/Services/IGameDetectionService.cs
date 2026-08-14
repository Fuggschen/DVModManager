using DVModManager.Models;

namespace DVModManager.Services;

public interface IGameDetectionService : IDisposable
{
    string? DetectGamePath();
    bool ValidateGamePath(string path);
    bool IsGameRunning();
    event EventHandler<bool> GameRunningChanged;
}
