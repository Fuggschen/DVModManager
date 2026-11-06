namespace DVModManager.Models;

public enum MonoRuntimeKind
{
    /// <summary>The game has no <c>mono-2.0-bdwgc.dll</c> (not a Windows/Proton install).</summary>
    NotFound,
    /// <summary>The runtime Unity shipped with the game (matches the known stock SHA-256).</summary>
    Stock,
    /// <summary>Any other runtime, usually a patched build.</summary>
    Modified
}

/// <summary>Snapshot of the Mono runtime DLL the game loads on its next start, read from disk.</summary>
public class MonoRuntimeStatus
{
    public MonoRuntimeKind Kind { get; init; }

    /// <summary>
    /// Feed release of a <see cref="MonoRuntimeKind.Modified"/> runtime, when it
    /// matches a build DVMM downloaded; null for an unknown build.
    /// </summary>
    public string? PatchId { get; init; }
    public string? PatchVersion { get; init; }

    /// <summary>Unity version the game was built with, or null when it cannot be read.</summary>
    public string? UnityVersion { get; init; }

    /// <summary>True when the patched runtimes are built for <see cref="UnityVersion"/>.</summary>
    public bool UnityVersionSupported { get; init; }

    /// <summary>True when a backup of the original runtime exists next to it.</summary>
    public bool BackupPresent { get; init; }
}
