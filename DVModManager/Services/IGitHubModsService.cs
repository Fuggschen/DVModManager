using DVModManager.Models;

namespace DVModManager.Services;

public interface IGitHubModsService
{
    Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default);
    Task<string> DownloadReleaseAssetAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
}
