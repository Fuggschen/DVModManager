using System.IO.Compression;
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

    public async Task<string> ExportProfileAsZipAsync(ModProfile profile, string gamePath, string zipPath)
    {
        await Task.Run(() =>
        {
            using var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);

            // Add profile.json at root
            var profileEntry = archive.CreateEntry("profile.json");
            using (var entryStream = profileEntry.Open())
            {
                var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions);
                entryStream.Write(json, 0, json.Length);
            }

            // Add mod files under mods/<ModId>/
            foreach (var mod in profile.Mods)
            {
                var activePath   = Path.Combine(gamePath, "Mods",          mod.ModId);
                var inactivePath = Path.Combine(gamePath, "Mods.inactive", mod.ModId);
                var modFolder    = Directory.Exists(activePath)   ? activePath
                                 : Directory.Exists(inactivePath) ? inactivePath
                                 : null;
                if (modFolder == null) continue;

                foreach (var file in Directory.EnumerateFiles(modFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(modFolder, file)
                                       .Replace('\\', '/');
                    var entryName = $"mods/{mod.ModId}/{relative}";
                    archive.CreateEntryFromFile(file, entryName,
                        System.IO.Compression.CompressionLevel.Fastest);
                }
            }
        });
        return zipPath;
    }

    public Task<string> GetUniqueProfileNameAsync(string name, string profilesPath)
    {
        var safeName = Path.GetFileNameWithoutExtension(ManagerStorage.ProfileFileName(name));
        var candidate = safeName;
        var counter = 2;
        while (File.Exists(Path.Combine(profilesPath, ManagerStorage.ProfileFileName(candidate))))
        {
            candidate = $"{safeName} ({counter++})";
        }
        return Task.FromResult(candidate);
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
        var toDownload = new List<ProfileModEntry>();
        var toRedownload = new List<ProfileModEntry>();

        foreach (var entry in profile.Mods)
        {
            if (!currentById.TryGetValue(entry.ModId, out var current))
            {
                // Only download mods that should be active in this profile
                if (entry.IsActive &&
                    (!string.IsNullOrEmpty(entry.RepositoryUrl) || !string.IsNullOrEmpty(entry.HomePageUrl)))
                    toDownload.Add(entry);
                continue;
            }

            if (entry.IsActive && !current.IsActive)
                toActivate.Add(entry.ModId);
            else if (!entry.IsActive && current.IsActive)
                toDeactivate.Add(entry.ModId);

            if (!string.IsNullOrEmpty(entry.Version) && entry.Version != current.Version)
            {
                // If a repository URL is available, prefer re-downloading the correct version
                // over a local rollback (which may not have the right version cached)
                if (!string.IsNullOrEmpty(entry.RepositoryUrl))
                    toRedownload.Add(entry);
                else
                    toRollback.Add((entry.ModId, current.Version, entry.Version));
            }
        }

        // Deactivate any mods that aren't in the profile at all
        var profileModIds = profile.Mods.Select(m => m.ModId).ToHashSet();
        foreach (var mod in currentMods.Where(m => m.IsActive && !profileModIds.Contains(m.Id)))
            toDeactivate.Add(mod.Id);

        return new ProfileDiff(toActivate, toDeactivate, toRollback, toDownload, toRedownload);
    }

    private static string GetProfileFilePath(string name, string profilesPath) =>
        Path.Combine(profilesPath, ManagerStorage.ProfileFileName(name));
}
