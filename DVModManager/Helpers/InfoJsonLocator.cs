namespace DVModManager.Helpers;

/// <summary>
/// Locates a mod's Info.json on disk regardless of filename casing.
/// Windows installs are case-insensitive, but on Linux many mod archives
/// ship the file as "info.json" or "Info.JSON", which the exact-case lookup
/// previously missed — stripping version/repo metadata from scans and profiles.
/// </summary>
public static class InfoJsonLocator
{
    /// <summary>
    /// Returns the full path of the mod metadata file in <paramref name="folder"/>,
    /// preferring the exact "Info.json" casing and otherwise matching
    /// "info.json" case-insensitively. Returns null if none is found.
    /// </summary>
    public static string? Locate(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;

        var exact = Path.Combine(folder, "Info.json");
        if (File.Exists(exact)) return exact;

        // Case-insensitive fallback for Linux (info.json, Info.JSON, ...).
        try
        {
            var match = Directory.EnumerateFiles(folder)
                .FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), "Info.json", StringComparison.OrdinalIgnoreCase));
            return match;
        }
        catch
        {
            return null;
        }
    }
}