using DVModManager.Models;

namespace DVModManager.Services;

public interface IMonoRuntimeService
{
    /// <summary>
    /// Reads the installed runtime. When the DLL matches the downloads of
    /// several channels, <paramref name="channel"/> decides which one is reported.
    /// </summary>
    MonoRuntimeStatus GetStatus(string gamePath, MonoRuntimeChannel channel);

    /// <summary>Newest feed release of <paramref name="channel"/>, or null when there is none.</summary>
    Task<MonoRuntimeRelease?> GetLatestReleaseAsync(MonoRuntimeChannel channel, CancellationToken ct = default);

    Task<bool> InstallPatchAsync(string gamePath, MonoRuntimeChannel channel,
        IProgress<double>? progress = null, CancellationToken ct = default);

    Task<bool> RestoreOriginalAsync(string gamePath, CancellationToken ct = default);
}
