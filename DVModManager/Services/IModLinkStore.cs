namespace DVModManager.Services;

/// <summary>Links the user set or the finder found for one mod.</summary>
public sealed record ModLinks(string? NexusUrl, string? RepositoryUrl);

/// <summary>
/// Keeps mod links outside Info.json, so a mod update cannot overwrite them.
/// Stored in <c>&lt;StoragePath&gt;/links/mod-links.json</c>, keyed by mod id.
/// </summary>
public interface IModLinkStore
{
    /// <summary>Loads the store of <paramref name="storagePath"/>; a missing or corrupt file reads as empty.</summary>
    Task LoadAsync(string storagePath);

    ModLinks? Get(string modId);

    /// <summary>Sets both links of a mod; null or blank values clear them.</summary>
    void Set(string modId, string? nexusUrl, string? repositoryUrl);

    Task SaveAsync();
}
