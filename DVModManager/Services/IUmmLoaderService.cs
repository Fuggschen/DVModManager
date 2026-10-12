using DVModManager.Models;

namespace DVModManager.Services;

public interface IUmmLoaderService
{
    UmmLoaderStatus GetStatus(string gamePath);
    
    Task<bool> InstallAsync(string gamePath,
        IProgress<double>? progress = null, CancellationToken ct = default);

    Task<bool> InstallFromZipAsync(string zipPath, string gamePath, CancellationToken ct = default);

    Task<bool> UninstallAsync(string gamePath, CancellationToken ct = default);
}
