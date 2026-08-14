using DVModManager.Models;

namespace DVModManager.Services;

public interface INexusModsService
{
    bool IsConfigured { get; }
    Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default);
    Task<string> DownloadModFileAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
}
