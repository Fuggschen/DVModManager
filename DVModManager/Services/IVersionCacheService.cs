using DVModManager.Models;

namespace DVModManager.Services;

public interface IVersionCacheService
{
    /// <summary>Zip the mod's current folder into the version cache before updating/rolling back.</summary>
    Task ArchiveCurrentVersionAsync(ModInfo mod, string storagePath);

    /// <summary>Returns all archived versions for a given mod, newest first.</summary>
    Task<IReadOnlyList<ModVersion>> GetVersionHistoryAsync(string modId, string storagePath);

    /// <summary>Retrieves the path to a specific archived version zip, or null if not cached.</summary>
    Task<string?> GetVersionArchivePathAsync(string modId, string version, string storagePath);

    Task DeleteVersionAsync(string modId, string version, string storagePath);
    Task<long> GetCacheSizeAsync(string storagePath);
    Task PruneCacheAsync(string storagePath, long maxSizeBytes);
}
