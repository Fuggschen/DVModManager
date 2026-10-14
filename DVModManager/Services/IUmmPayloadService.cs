using DVModManager.Models;

namespace DVModManager.Services;

public interface IUmmPayloadService
{
    /// <summary>
    /// Resolves the UMM loader payload, downloading and caching it when needed.
    /// The feed only carries the latest release, so this always resolves to the
    /// newest usable version (reusing the cache when it already matches).
    /// Returns null when no usable payload could be obtained.
    /// </summary>
    Task<UmmPayloadInfo?> GetPayloadAsync(
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Validates and caches a locally provided UMM package zip (manual safety
    /// valve when the feeds or Dropbox are unreachable). Returns null when the
    /// zip does not look like a full UMM package.
    /// </summary>
    UmmPayloadInfo? ImportFromZip(string zipPath);
}
