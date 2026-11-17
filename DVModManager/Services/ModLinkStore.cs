using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public sealed class ModLinkStore : IModLinkStore
{
    public const string DirectoryName = "links";
    public const string FileName = "mod-links.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<ModLinkStore> _logger;
    private readonly object _lock = new();
    private Dictionary<string, ModLinks> _links = new(StringComparer.OrdinalIgnoreCase);
    private string? _filePath;

    public ModLinkStore(ILogger<ModLinkStore> logger) => _logger = logger;

    public async Task LoadAsync(string storagePath)
    {
        var path = Path.Combine(storagePath, DirectoryName, FileName);
        var links = new Dictionary<string, ModLinks>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, ModLinks>>(
                    await File.ReadAllTextAsync(path), JsonOptions);
                foreach (var (id, entry) in stored ?? [])
                    if (!string.IsNullOrWhiteSpace(id) && entry != null)
                        links[id] = entry;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.LogWarning(ex, "Could not read {Path}; starting with no stored links", path);
            }
        }

        lock (_lock)
        {
            _filePath = path;
            _links = links;
        }
    }

    public ModLinks? Get(string modId)
    {
        lock (_lock) return _links.TryGetValue(modId, out var links) ? links : null;
    }

    public void Set(string modId, string? nexusUrl, string? repositoryUrl)
    {
        var entry = new ModLinks(Clean(nexusUrl), Clean(repositoryUrl));
        lock (_lock)
        {
            if (entry.NexusUrl == null && entry.RepositoryUrl == null) _links.Remove(modId);
            else _links[modId] = entry;
        }
    }

    public async Task SaveAsync()
    {
        string path;
        string json;
        lock (_lock)
        {
            if (_filePath == null) throw new InvalidOperationException("LoadAsync must run before SaveAsync.");
            path = _filePath;
            json = JsonSerializer.Serialize(
                _links.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(kv => kv.Key, kv => kv.Value),
                JsonOptions);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Temp file + swap, so a crash mid-write keeps the previous links (same as settings.json)
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private static string? Clean(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
