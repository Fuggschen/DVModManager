using DVModManager.Models;

namespace DVModManager.Services;

public interface IModDiscoveryService : IDisposable
{
    Task<IReadOnlyList<ModInfo>> ScanAllModsAsync(string gamePath);
    Task<ModInfo?> ParseModInfoAsync(string folderPath, bool isActive);
    void StartWatching(string gamePath);
    void StopWatching();
    event EventHandler? ModsChanged;
}
