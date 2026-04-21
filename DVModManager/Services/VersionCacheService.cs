using System.IO.Compression;
using System.Text.Json;
using DVModManager.Models;

namespace DVModManager.Services;

public class VersionCacheService : IVersionCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task ArchiveCurrentVersionAsync(ModInfo mod, string storagePath)
    {
        if (!Directory.Exists(mod.FolderPath)) return;

        var modVersionDir = Path.Combine(storagePath, "versions", mod.Id);
        Directory.CreateDirectory(modVersionDir);

        var safeVersion = mod.Version.Replace(' ', '_').Replace('/', '_');
        var archivePath = Path.Combine(modVersionDir, $"{safeVersion}.zip");

        // Overwrite any existing archive for this version
        if (File.Exists(archivePath)) File.Delete(archivePath);

        await Task.Run(() => ZipFile.CreateFromDirectory(mod.FolderPath, archivePath, CompressionLevel.Optimal, false));

        // Update the manifest
        var versions = (await GetVersionHistoryAsync(mod.Id, storagePath)).ToList();
        if (!versions.Any(v => v.Version == mod.Version))
        {
            versions.Insert(0, new ModVersion
            {
                ModId = mod.Id,
                Version = mod.Version,
                ArchivePath = archivePath,
                Source = "modversion.source.cached",
                ArchivedAt = DateTime.UtcNow,
                ArchiveSizeBytes = new FileInfo(archivePath).Length
            });
        }

        await SaveManifestAsync(mod.Id, modVersionDir, versions);
    }

    public async Task<IReadOnlyList<ModVersion>> GetVersionHistoryAsync(string modId, string storagePath)
    {
        var manifestPath = GetManifestPath(modId, storagePath);
        if (!File.Exists(manifestPath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            return JsonSerializer.Deserialize<List<ModVersion>>(json, JsonOptions) ?? [];
        }
        catch { return []; }
    }

    public async Task<string?> GetVersionArchivePathAsync(string modId, string version, string storagePath)
    {
        var versions = await GetVersionHistoryAsync(modId, storagePath);
        var entry = versions.FirstOrDefault(v => v.Version == version);
        return entry?.ArchivePath is { } path && File.Exists(path) ? path : null;
    }

    public async Task DeleteVersionAsync(string modId, string version, string storagePath)
    {
        var versions = (await GetVersionHistoryAsync(modId, storagePath)).ToList();
        var entry = versions.FirstOrDefault(v => v.Version == version);
        if (entry == null) return;

        if (File.Exists(entry.ArchivePath)) File.Delete(entry.ArchivePath);
        versions.Remove(entry);

        var manifestDir = Path.Combine(storagePath, "versions", modId);
        await SaveManifestAsync(modId, manifestDir, versions);
    }

    public Task<long> GetCacheSizeAsync(string storagePath)
    {
        var versionsDir = Path.Combine(storagePath, "versions");
        if (!Directory.Exists(versionsDir)) return Task.FromResult(0L);

        var size = Directory.GetFiles(versionsDir, "*.zip", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
        return Task.FromResult(size);
    }

    public async Task PruneCacheAsync(string storagePath, long maxSizeBytes)
    {
        var versionsDir = Path.Combine(storagePath, "versions");
        if (!Directory.Exists(versionsDir)) return;

        // Collect all version entries across all mods, sorted oldest first
        var allVersions = new List<(string ModId, ModVersion Version, string ManifestDir)>();
        foreach (var modDir in Directory.GetDirectories(versionsDir))
        {
            var modId = Path.GetFileName(modDir);
            var versions = (await GetVersionHistoryAsync(modId, storagePath)).ToList();
            foreach (var v in versions)
                allVersions.Add((modId, v, modDir));
        }

        allVersions.Sort((a, b) => a.Version.ArchivedAt.CompareTo(b.Version.ArchivedAt));

        long totalSize = allVersions.Sum(x => x.Version.ArchiveSizeBytes);
        foreach (var (modId, version, _) in allVersions)
        {
            if (totalSize <= maxSizeBytes) break;
            totalSize -= version.ArchiveSizeBytes;
            await DeleteVersionAsync(modId, version.Version, storagePath);
        }
    }

    private static string GetManifestPath(string modId, string storagePath) =>
        Path.Combine(storagePath, "versions", modId, "versions.json");

    private static async Task SaveManifestAsync(string modId, string modVersionDir, List<ModVersion> versions)
    {
        Directory.CreateDirectory(modVersionDir);
        var json = JsonSerializer.Serialize(versions, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(modVersionDir, "versions.json"), json);
    }
}
