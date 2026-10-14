namespace DVModManager.Models;

/// <summary>
/// Snapshot of the UMM loader state inside a game installation.
/// <see cref="LatestKnownVersion"/> / <see cref="UpdateAvailable"/> are filled
/// by callers that also query the release feed (GetStatus itself is offline).
/// </summary>
public class UmmLoaderStatus
{
    public bool IsInstalled => InstalledVersion != null || DoorstopFilesPresent || AssemblyPatched;

    public string? InstalledVersion { get; init; }

    public UmmLoaderMethod? ActiveMethod { get; init; }

    public bool DoorstopFilesPresent { get; init; }
    public bool AssemblyPatched { get; init; }

    public string? EntryPoint { get; init; }

    public string? LatestKnownVersion { get; set; }

    public bool UpdateAvailable { get; set; }
}
