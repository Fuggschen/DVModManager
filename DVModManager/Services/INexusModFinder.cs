using DVModManager.Helpers;
using DVModManager.Models;

namespace DVModManager.Services;

/// <summary>What the finder knows about a local mod.</summary>
public sealed record ModSearchKey(
    string ModId,
    string? Version,
    string? Author,
    string? RepositoryUrl,
    string? HomePageUrl,
    IReadOnlyList<FingerprintFile> Fingerprint)
{
    /// <summary>
    /// Builds the key of a scanned mod. Reads the mod folder for the fingerprint, so
    /// call it off the UI thread. A placeholder has no files and uses its stored one.
    /// </summary>
    public static ModSearchKey FromMod(ModInfo mod) => new(
        mod.Id,
        mod.Version,
        mod.Author,
        mod.Repository,
        mod.HomePage,
        mod.IsPlaceholder ? ModFingerprint.Decode(mod.Fingerprint) : ModFingerprint.Compute(mod.FolderPath));
}

public interface INexusModFinder
{
    /// <summary>
    /// Looks the mod up on Nexus by its folder, DLL, file fingerprint and GitHub repo.
    /// Never throws for network or API errors; those return <see cref="NexusMatchCertainty.Failed"/>.
    /// </summary>
    Task<NexusMatch> FindAsync(ModSearchKey key, CancellationToken ct = default);
}
