using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

/// <summary>
/// Downloads, validates and caches the Unity Mod Manager loader package.
/// </summary>
/// <remarks>
/// Nothing from the package is ever executed: the manifest is checked
/// structurally and <c>UnityModManager.dll</c> is inspected via
/// System.Reflection.Metadata (assembly identity only). Validation rejects
/// UMM's patch-only update zips (they lack the loader DLL) so a fresh install
/// can never end up half-populated.
/// </remarks>
public class UmmPayloadService : IUmmPayloadService
{
    /// <summary>Files UMM itself installs, relative to the loader folder. Missing any = not a full package.</summary>
    public static readonly IReadOnlyList<string> RequiredFiles =
    [
        "UnityModManager.dll",
        "UnityModManager.xml",
        "0Harmony.dll",
        "dnlib.dll",
        "System.Xml.dll",
        Path.Combine("Harmony", "2.2", "0Harmony.dll"),
        Path.Combine("Harmony", "1.2", "0Harmony12.dll"),
        Path.Combine("Harmony", "1.2", "0Harmony-1.2.dll")
    ];

    /// <summary>Known extra files worth extracting when present.</summary>
    public static readonly IReadOnlyList<string> OptionalFiles =
    [
        "winhttp_x64.dll",
        "winhttp_x86.dll",
        "UnityModManagerConfig.xml"
    ];

    private const string LoaderAssemblyName = "UnityModManager";
    private const string ManagerConfigFileName = "UnityModManagerConfig.xml";

    private readonly IUmmReleaseFeedService _feed;
    private readonly ISettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UmmPayloadService> _logger;

    public UmmPayloadService(IUmmReleaseFeedService feed, ISettingsService settings,
        IHttpClientFactory httpFactory, ILogger<UmmPayloadService> logger)
    {
        _feed = feed;
        _settings = settings;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    private string CacheRoot => Path.Combine(_settings.Settings.DownloadsPath, "umm");

    public async Task<UmmPayloadInfo?> GetPayloadAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var candidates = await _feed.GetCandidatesAsync(ct);

        foreach (var candidate in candidates)
        {
            var payload = TryLoadCached(candidate.Version);
            if (payload != null) return payload;

            try
            {
                var zipPath = await DownloadAsync(candidate, progress, ct);
                try
                {
                    payload = ValidateAndExtract(zipPath, candidate.Version);
                    if (payload != null) return payload;
                    _logger.LogWarning(
                        "UMM package {Version} from {Url} failed validation — trying next candidate",
                        candidate.Version, candidate.DownloadUrl);
                }
                finally
                {
                    try { File.Delete(zipPath); } catch { /* best-effort cleanup */ }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch UMM {Version} from {Url}",
                    candidate.Version, candidate.DownloadUrl);
            }
        }

        // Offline / broken feeds: fall back to any cached payload so installs
        // stay possible without network.
        var fallback = FindNewestCachedPayload();
        if (fallback != null)
        {
            _logger.LogWarning("All UMM feeds failed — falling back to cached payload {Version}", fallback.Version);
            return fallback;
        }
        return null;
    }

    public UmmPayloadInfo? ImportFromZip(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            _logger.LogWarning("UMM package {Path} does not exist", zipPath);
            return null;
        }
        return ValidateAndExtract(zipPath, version: null);
    }

    //  Cache 

    private string GetPayloadDirectory(string version) =>
        Path.Combine(CacheRoot, "payload", SanitizeVersion(version));

    private UmmPayloadInfo? TryLoadCached(string version)
    {
        var dir = GetPayloadDirectory(version);
        return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "UnityModManager.dll"))
            ? DescribePayload(dir, version)
            : null;
    }

    private UmmPayloadInfo? FindNewestCachedPayload()
    {
        var payloadRoot = Path.Combine(CacheRoot, "payload");
        if (!Directory.Exists(payloadRoot)) return null;

        UmmPayloadInfo? best = null;
        Version? bestVersion = null;
        foreach (var dir in Directory.GetDirectories(payloadRoot))
        {
            if (!File.Exists(Path.Combine(dir, "UnityModManager.dll"))) continue;
            var version = Path.GetFileName(dir);
            var parsed = UmmReleaseFeedService.ParseVersionOrNull(version) ?? new Version(0, 0);
            if (best == null || parsed > bestVersion)
            {
                best = DescribePayload(dir, version);
                bestVersion = parsed;
            }
        }
        return best;
    }

    //  Download 

    private async Task<string> DownloadAsync(UmmReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheRoot);
        var zipPath = Path.Combine(CacheRoot, $"umm-{SanitizeVersion(release.Version)}.zip");

        _logger.LogInformation("Downloading UMM {Version} from {Url}", release.Version, release.DownloadUrl);

        var http = _httpFactory.CreateClient("github");
        using var response = await http.GetAsync(
            release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var netStream = await response.Content.ReadAsStreamAsync(ct);
        await using (var fileStream = File.Create(zipPath))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await netStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report((double)downloaded / totalBytes.Value);
            }
        }
        return zipPath;
    }

    //  Validate & extract 

    /// <summary>
    /// Validates the zip against the expected UMM package manifest and extracts
    /// it to the cache. <paramref name="version"/> is the feed version, or null
    /// for manual imports (version is then read from UnityModManager.dll).
    /// </summary>
    private UmmPayloadInfo? ValidateAndExtract(string zipPath, string? version)
    {
        var wanted = RequiredFiles.Concat(OptionalFiles).ToList();

        try
        {
            Dictionary<string, ZipArchiveEntry> resolved;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                resolved = ResolveEntries(archive, wanted);
                var missing = RequiredFiles.Where(f => !resolved.ContainsKey(Normalize(f))).ToList();
                if (missing.Count > 0)
                {
                    _logger.LogWarning(
                        "UMM package {Path} is not a full loader package — missing: {Missing}",
                        zipPath, string.Join(", ", missing));
                    return null;
                }

                // Inspect the loader assembly without executing it.
                using (var stream = resolved[Normalize("UnityModManager.dll")].Open())
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    if (!TryReadAssemblyIdentity(ms.ToArray(), out var asmName, out var asmVersion)
                        || !string.Equals(asmName, LoaderAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "UMM package {Path} does not contain a real UnityModManager assembly (got '{Name}')",
                            zipPath, asmName ?? "<none>");
                        return null;
                    }
                    version ??= asmVersion;
                }

                if (version == null)
                {
                    _logger.LogWarning("UMM package {Path}: could not determine loader version", zipPath);
                    return null;
                }

                var targetDir = GetPayloadDirectory(version);
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                Directory.CreateDirectory(targetDir);

                foreach (var (rel, entry) in resolved)
                {
                    var dest = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, true);
                }

                return DescribePayload(targetDir, version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate/extract UMM package {Path}", zipPath);
            return null;
        }
    }

    private UmmPayloadInfo DescribePayload(string dir, string version)
    {
        UmmGameInfo? gameInfo = null;
        var configPath = Path.Combine(dir, ManagerConfigFileName);
        if (File.Exists(configPath))
        {
            try
            {
                gameInfo = UmmGameInfo.ParseFromManagerConfig(
                    File.ReadAllText(configPath), UmmGameInfo.DerailValleyName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse {Path}", configPath);
            }
        }

        return new UmmPayloadInfo
        {
            Version = version,
            Directory = dir,
            GameInfo = gameInfo ?? UmmGameInfo.DerailValleyFallback(),
            HasDoorstopProxies =
                File.Exists(Path.Combine(dir, "winhttp_x64.dll"))
                || File.Exists(Path.Combine(dir, "winhttp_x86.dll"))
        };
    }

    /// <summary>
    /// Maps each expected relative path to a zip entry: exact match first,
    /// otherwise the shortest entry whose path ends with "/expected" (handles
    /// packages nested under a root folder).
    /// </summary>
    internal static Dictionary<string, ZipArchiveEntry> ResolveEntries(
        ZipArchive archive, IEnumerable<string> wanted)
    {
        var entries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name)) // skip directory entries
            .Select(e => (Entry: e, Path: Normalize(e.FullName)))
            .ToList();

        var resolved = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in wanted)
        {
            var key = Normalize(target);
            if (resolved.ContainsKey(key)) continue;

            var exact = entries.FirstOrDefault(x => string.Equals(x.Path, key, StringComparison.OrdinalIgnoreCase));
            if (exact.Entry != null)
            {
                resolved[key] = exact.Entry;
                continue;
            }

            var suffix = "/" + key;
            var match = entries
                .Where(x => x.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Path.Length)
                .FirstOrDefault();
            if (match.Entry != null)
                resolved[key] = match.Entry;
        }
        return resolved;
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('.', '/');

    private static string SanitizeVersion(string version)
    {
        var chars = version.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Reads assembly name/version from PE metadata. Returns false when the
    /// bytes are not a valid .NET assembly — used to reject foreign DLLs.
    /// </summary>
    internal static bool TryReadAssemblyIdentity(byte[] data, out string? name, out string? version)
    {
        name = null;
        version = null;
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            using var peReader = new PEReader(ms, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata) return false;

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly) return false;

            var def = reader.GetAssemblyDefinition();
            name = reader.GetString(def.Name);
            // Normalize "0.33.2.0" → "0.33.2" so feed versions and metadata
            // versions map to the same cache directory.
            var v = def.Version;
            version = v.Revision <= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();
            return !string.IsNullOrEmpty(name);
        }
        catch
        {
            return false;
        }
    }
}
