using System.Text.Json;
using DVModManager.Models;

namespace DVModManager.Services;

public class ProfileService : IProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<ModProfile>> GetProfilesAsync(string profilesPath)
    {
        if (!Directory.Exists(profilesPath)) return [];

        var profiles = new List<ModProfile>();
        foreach (var file in Directory.GetFiles(profilesPath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var profile = JsonSerializer.Deserialize<ModProfile>(json, JsonOptions);
                if (profile != null) profiles.Add(profile);
            }
            catch { /* skip corrupt profiles */ }
        }

        return profiles.OrderBy(p => p.Name).ToList();
    }

    public async Task<ModProfile?> GetProfileAsync(string name, string profilesPath)
    {
        var filePath = GetProfileFilePath(name, profilesPath);
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<ModProfile>(json, JsonOptions);
        }
        catch { return null; }
    }

    public async Task SaveProfileAsync(ModProfile profile, string profilesPath)
    {
        Directory.CreateDirectory(profilesPath);
        profile.LastModifiedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(GetProfileFilePath(profile.Name, profilesPath), json);
    }

    public Task DeleteProfileAsync(string name, string profilesPath)
    {
        var filePath = GetProfileFilePath(name, profilesPath);
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }

    public async Task<string> ExportProfileAsync(ModProfile profile, string destinationFilePath)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(destinationFilePath, json);
        return destinationFilePath;
    }

    public async Task<ModProfile> ImportProfileAsync(string sourceFilePath)
    {
        var json = await File.ReadAllTextAsync(sourceFilePath);
        return JsonSerializer.Deserialize<ModProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("File is not a valid profile.");
    }

    public ProfileDiff ComputeDiff(ModProfile profile, IReadOnlyList<ModInfo> currentMods)
    {
        // Use last-write-wins to tolerate duplicate mod IDs (e.g. same mod in both
        // Mods/ and Mods.inactive/ simultaneously after a failed move).
        var currentById = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in currentMods)
            if (!string.IsNullOrEmpty(m.Id))
                currentById[m.Id] = m;
        var toActivate = new List<string>();
        var toDeactivate = new List<string>();
        var toRollback = new List<(string, string, string)>();

        foreach (var entry in profile.Mods)
        {
            if (!currentById.TryGetValue(entry.ModId, out var current)) continue;

            if (entry.IsActive && !current.IsActive)
                toActivate.Add(entry.ModId);
            else if (!entry.IsActive && current.IsActive)
                toDeactivate.Add(entry.ModId);

            if (!string.IsNullOrEmpty(entry.Version) && entry.Version != current.Version)
                toRollback.Add((entry.ModId, current.Version, entry.Version));
        }

        // Deactivate any mods that aren't in the profile at all
        var profileModIds = profile.Mods.Select(m => m.ModId).ToHashSet();
        foreach (var mod in currentMods.Where(m => m.IsActive && !profileModIds.Contains(m.Id)))
            toDeactivate.Add(mod.Id);

        return new ProfileDiff(toActivate, toDeactivate, toRollback);
    }

    private static string GetProfileFilePath(string name, string profilesPath)
    {
        var safeName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(profilesPath, safeName + ".json");
    }
}
