using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class UpdateService : IUpdateService
{
    private readonly IGitHubModsService _github;
    private readonly INexusModsService _nexus;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(IGitHubModsService github, INexusModsService nexus, ILogger<UpdateService> logger)
    {
        _github = github;
        _nexus = nexus;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ModUpdateInfo>> CheckAllUpdatesAsync(
        IReadOnlyList<ModInfo> mods, CancellationToken ct = default)
    {
        var tasks = mods.Select(m => CheckUpdateAsync(m, ct));
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    public async Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default)
    {
        // Try GitHub first (free, no key required)
        if (!string.IsNullOrEmpty(mod.Repository))
        {
            var ghUpdate = await _github.CheckUpdateAsync(mod, ct);
            if (ghUpdate != null) return ghUpdate;
        }

        // Try Nexus if API key is configured
        if (_nexus.IsConfigured && !string.IsNullOrEmpty(mod.HomePage))
        {
            var nexusUpdate = await _nexus.CheckUpdateAsync(mod, ct);
            if (nexusUpdate != null) return nexusUpdate;
        }

        return null;
    }
}
