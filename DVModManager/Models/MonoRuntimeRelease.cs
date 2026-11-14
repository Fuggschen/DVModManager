namespace DVModManager.Models;

/// <summary>Which patched Mono runtime build to install.</summary>
public enum MonoRuntimeChannel
{
    Standard,
    /// <summary>Debug build of the same fix.</summary>
    Debug
}

/// <summary>
/// A patched build of Unity's embedded Mono runtime
/// (<c>MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll</c>) listed in the
/// runtime release feed. <see cref="Sha256"/> is the feed's lowercase
/// <c>Sha256Sum</c>, or null when the entry has none.
/// </summary>
public sealed record MonoRuntimeRelease(string Id, string Version, string DownloadUrl, string? Sha256 = null)
{
    /// <summary>Feed release Id of each channel.</summary>
    public static string GetFeedId(MonoRuntimeChannel channel) => channel switch
    {
        MonoRuntimeChannel.Debug => "GcRootsFixDebug",
        _ => "GcRootsFix"
    };
}
