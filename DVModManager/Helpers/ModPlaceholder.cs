using System.Text.Json;
using DVModManager.Models;

namespace DVModManager.Helpers;

/// <summary>
/// Placeholder folders stand in for profile mods that could not be installed.
/// A folder only counts as a placeholder while it has no Info.json, so a real
/// install into the same folder takes over from it.
/// </summary>
public static class ModPlaceholder
{
    public const string FileName = "placeholder.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Returns the placeholder file of <paramref name="folder"/>, or null if it has none.</summary>
    public static string? Locate(string folder)
    {
        var path = Path.Combine(folder, FileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Creates <c>&lt;modsDir&gt;/&lt;ModId&gt;/placeholder.json</c>. Returns false when the
    /// id is not a safe folder name or the folder already exists; an existing folder
    /// is never touched.
    /// </summary>
    public static async Task<bool> CreateAsync(string modsDir, ProfileModEntry entry, CancellationToken ct = default)
    {
        if (!PathSafety.IsValidModId(entry.ModId)) return false;

        var folder = PathSafety.SafeCombine(modsDir, entry.ModId);
        if (Directory.Exists(folder)) return false;

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, FileName), JsonSerializer.Serialize(entry, JsonOptions), ct);
        return true;
    }

    /// <summary>Reads a placeholder file. Returns null if it cannot be parsed.</summary>
    public static async Task<ProfileModEntry?> ReadAsync(string placeholderPath, CancellationToken ct = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(placeholderPath, ct);
            return JsonSerializer.Deserialize<ProfileModEntry>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the placeholder file of <paramref name="folder"/>, then the folder
    /// itself if nothing else is left in it.
    /// </summary>
    public static void Remove(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (File.Exists(path)) File.Delete(path);
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }
}
