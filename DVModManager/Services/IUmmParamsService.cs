namespace DVModManager.Services;

/// <summary>
/// Reads and writes the per-mod in-game enabled flag that Unity Mod Manager
/// stores in <c>Params.xml</c> (toggled via the in-game UMM window).
/// </summary>
public interface IUmmParamsService
{
    string GetParamsPath(string gamePath);

    /// <summary>
    /// Mod Id → Enabled, as stored by UMM. Mods without an entry are not in the
    /// dictionary (UMM treats them as enabled). Never throws.
    /// </summary>
    IReadOnlyDictionary<string, bool> ReadEnabledStates(string gamePath);

    /// <summary>Sets the in-game enabled flag. Only call while the game is not running.</summary>
    Task SetEnabledAsync(string gamePath, string modId, bool enabled);
}
