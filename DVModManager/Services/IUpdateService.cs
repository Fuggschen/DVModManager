using DVModManager.Models;

namespace DVModManager.Services;

public interface IUpdateService
{
    Task<IReadOnlyList<ModUpdateInfo>> CheckAllUpdatesAsync(IReadOnlyList<ModInfo> mods, CancellationToken ct = default);
    Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default);
}
