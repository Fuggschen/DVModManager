namespace DVModManager.Services;

/// <summary>
/// Downloads a DVModManager release and swaps it in place of the running executable.
/// </summary>
public interface IManagerUpdateService
{
    /// <summary>
    /// True when the running executable is a published build in a writable folder.
    /// False for <c>dotnet run</c> or protected install folders; callers fall back to the release page.
    /// </summary>
    bool CanSelfUpdate { get; }

    /// <summary>Downloads the release zip and extracts only the executable. Returns the staging folder.</summary>
    Task<string> DownloadAndStageAsync(string version, string? releaseUrl,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Replaces the running executable with the staged one. The old executable is renamed,
    /// not deleted, and restored if the swap fails.
    /// </summary>
    void ApplyUpdate(string stagedDir);

    /// <summary>Starts the (updated) executable as a new process.</summary>
    void StartNewInstance();

    /// <summary>Deletes files left over from a previous update. Retries while the old process exits.</summary>
    Task CleanupOldFilesAsync(CancellationToken ct = default);
}
