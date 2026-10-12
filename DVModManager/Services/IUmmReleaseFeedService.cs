using DVModManager.Models;

namespace DVModManager.Services;

public interface IUmmReleaseFeedService
{
    /// <summary>
    /// Returns Unity Mod Manager releases from UMM's official self-update feeds,
    /// highest version first. Entries whose version cannot be parsed are skipped.
    /// Returns an empty list when every feed is unreachable or malformed.
    /// </summary>
    Task<IReadOnlyList<UmmReleaseInfo>> GetCandidatesAsync(CancellationToken ct = default);
}
