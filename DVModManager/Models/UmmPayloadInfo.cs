namespace DVModManager.Models;

/// <summary>
/// A downloaded and validated UMM package, extracted to the local cache and
/// ready to be copied into the game.
/// </summary>
public class UmmPayloadInfo
{
    public string Version { get; init; } = "";

    public string Directory { get; init; } = "";

    public UmmGameInfo GameInfo { get; init; } = UmmGameInfo.DerailValleyFallback();

    public bool HasDoorstopProxies { get; init; }

    public override string ToString() => $"UMM {Version} @ {Directory}";
}
