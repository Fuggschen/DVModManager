namespace DVModManager.Models;

/// <summary>
/// How the UMM loader is wired into the game. Mirrors Unity Mod Manager's
/// own install types: DoorstopProxy (winhttp.dll + doorstop_config.ini) and
/// Assembly (IL patch of the game's entry assembly).
/// </summary>
public enum UmmLoaderMethod
{
    Doorstop,
    Assembly
}
