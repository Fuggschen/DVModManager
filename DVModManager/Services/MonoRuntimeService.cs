using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

/// <summary>
/// Swaps the game's embedded Mono runtime for a patched build from the
/// geluk/mono release feed and back.
/// </summary>
/// <remarks>
/// Status always comes from hashing the DLL on disk: Steam updates and
/// "Verify integrity of game files" silently put the stock runtime back.
/// The original is kept as <c>mono-2.0-bdwgc-backup.dll</c>, the same name the
/// patch's release notes use for a manual install, so manual installs are
/// restorable too.
/// </remarks>
public partial class MonoRuntimeService : IMonoRuntimeService
{
    public const string RuntimeFileName = "mono-2.0-bdwgc.dll";
    public const string BackupFileName = "mono-2.0-bdwgc-backup.dll";

    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/geluk/mono/refs/heads/unity-main/repository.json";

    /// <summary>
    /// The only Unity version that needs the patch: the feed's builds are made
    /// for it, and a runtime from another Unity version crashes the game at
    /// start. Later Derail Valley versions move to Unity 6, which ships the fix.
    /// </summary>
    public const string SupportedUnityVersion = "2019.4.40f1";

    /// <summary>
    /// SHA-256 of the unmodified runtime. A DLL with any other hash counts as
    /// modified, and only an unmodified DLL is ever backed up or restored.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultStockSha256 =
    [
        // Derail Valley, Unity 2019.4.40f1, Windows x64.
        "bb94b8282379d4dac5fd8e4a8bafab47b09488e3ca0fb1fe5e2493686c71caf7"
    ];

    public static string GetRuntimeDirectory(string gamePath) =>
        Path.Combine(gamePath, "MonoBleedingEdge", "EmbedRuntime");

    public static string GetRuntimePath(string gamePath) =>
        Path.Combine(GetRuntimeDirectory(gamePath), RuntimeFileName);

    public static string GetBackupPath(string gamePath) =>
        Path.Combine(GetRuntimeDirectory(gamePath), BackupFileName);

    private readonly ISettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MonoRuntimeService> _logger;
    private readonly string _feedUrl;
    private readonly IReadOnlyCollection<string> _stockSha256;

    // Last feed read successfully; lets the offline GetStatus identify a
    // runtime by the feed's Sha256Sum.
    private volatile IReadOnlyList<MonoRuntimeRelease> _lastFeed = [];

    public MonoRuntimeService(ISettingsService settings, IHttpClientFactory httpFactory,
        ILogger<MonoRuntimeService> logger)
        : this(settings, httpFactory, logger, DefaultFeedUrl, DefaultStockSha256) { }

    internal MonoRuntimeService(ISettingsService settings, IHttpClientFactory httpFactory,
        ILogger<MonoRuntimeService> logger, string feedUrl, IReadOnlyCollection<string> stockSha256)
    {
        _settings = settings;
        _httpFactory = httpFactory;
        _logger = logger;
        _feedUrl = feedUrl;
        _stockSha256 = stockSha256;
    }

    private bool IsStock(string path) => TryHashFile(path) is { } hash && _stockSha256.Contains(hash);

    /// <summary>Downloads live in <c>mono/&lt;Id&gt;/&lt;Version&gt;/</c>.</summary>
    private string CacheRoot => Path.Combine(_settings.Settings.DownloadsPath, "mono");

    private string GetCachePath(MonoRuntimeRelease release) =>
        Path.Combine(CacheRoot, Sanitize(release.Id), Sanitize(release.Version), RuntimeFileName);

    public MonoRuntimeStatus GetStatus(string gamePath, MonoRuntimeChannel channel)
    {
        var runtime = GetRuntimePath(gamePath);
        var backup = GetBackupPath(gamePath);
        var unityVersion = ReadUnityVersion(gamePath);

        var kind = MonoRuntimeKind.NotFound;
        (string Id, string Version)? patch = null;
        if (File.Exists(runtime))
            (kind, patch) = Classify(runtime, MonoRuntimeRelease.GetFeedId(channel));

        return new MonoRuntimeStatus
        {
            Kind = kind,
            PatchId = patch?.Id,
            PatchVersion = patch?.Version,
            UnityVersion = unityVersion,
            UnityVersionSupported = unityVersion == SupportedUnityVersion,
            BackupPresent = File.Exists(backup)
        };
    }

    private (MonoRuntimeKind Kind, (string Id, string Version)? Patch) Classify(string runtime, string preferredId)
    {
        var hash = TryHashFile(runtime);
        if (hash == null)
        {
            _logger.LogWarning("Could not hash {Path}", runtime);
            return (MonoRuntimeKind.Modified, null);
        }
        if (_stockSha256.Contains(hash)) return (MonoRuntimeKind.Stock, null);

        // Label the modified runtime with the release it matches, from the
        // feed's hashes first, then from the downloads. Several channels may
        // ship the same file, so the selected channel goes first; a re-released
        // file matches several versions, so the highest wins.
        var fromFeed = _lastFeed
            .Where(r => r.Sha256 == hash)
            .OrderBy(r => string.Equals(r.Id, preferredId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(r => UmmReleaseFeedService.ParseVersionOrNull(r.Version))
            .FirstOrDefault();
        if (fromFeed != null) return (MonoRuntimeKind.Modified, (fromFeed.Id, fromFeed.Version));

        if (!Directory.Exists(CacheRoot)) return (MonoRuntimeKind.Modified, null);
        var idDirs = Directory.GetDirectories(CacheRoot)
            .OrderBy(d => Path.GetFileName(d) == preferredId ? 0 : 1);
        foreach (var idDir in idDirs)
        {
            var match = Directory.GetDirectories(idDir)
                .Select(Path.GetFileName)
                .OrderByDescending(UmmReleaseFeedService.ParseVersionOrNull)
                .FirstOrDefault(v => TryHashFile(Path.Combine(idDir, v!, RuntimeFileName)) == hash);
            if (match != null)
                return (MonoRuntimeKind.Modified, (Path.GetFileName(idDir), match));
        }
        return (MonoRuntimeKind.Modified, null);
    }

    // ── Feed ─────────────────────────────────────────────────────────────────

    public async Task<MonoRuntimeRelease?> GetLatestReleaseAsync(MonoRuntimeChannel channel, CancellationToken ct = default)
    {
        var id = MonoRuntimeRelease.GetFeedId(channel);
        try
        {
            var http = _httpFactory.CreateClient("github");
            var json = await http.GetStringAsync(_feedUrl, ct);
            var feed = ParseFeed(json);
            _lastFeed = feed;
            return feed
                .Where(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => UmmReleaseFeedService.ParseVersionOrNull(r.Version))
                .FirstOrDefault();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Mono runtime feed {Url}", _feedUrl);
            return null;
        }
    }

    /// <summary>
    /// Parses the feed shape <c>{"Releases":[{"Id","Version","DownloadUrl","Sha256Sum"}]}</c>;
    /// <c>Sha256Sum</c> is optional.
    /// </summary>
    internal static IReadOnlyList<MonoRuntimeRelease> ParseFeed(string json)
    {
        var result = new List<MonoRuntimeRelease>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!doc.RootElement.TryGetProperty("Releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in releases.EnumerateArray())
            {
                var id = GetString(item, "Id")?.Trim();
                var version = GetString(item, "Version")?.Trim();
                var url = GetString(item, "DownloadUrl")?.Trim();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)
                    || UmmReleaseFeedService.ParseVersionOrNull(version) == null)
                    continue;

                var sha256 = GetString(item, "Sha256Sum")?.Trim().ToLowerInvariant();
                result.Add(new MonoRuntimeRelease(id, version!, url,
                    string.IsNullOrEmpty(sha256) ? null : sha256));
            }
        }
        catch (JsonException)
        {
            // Malformed feed
        }
        return result;
    }

    private static string? GetString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ── Install / restore ────────────────────────────────────────────────────

    public async Task<bool> InstallPatchAsync(string gamePath, MonoRuntimeChannel channel,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var status = GetStatus(gamePath, channel);
        if (status.Kind == MonoRuntimeKind.NotFound)
        {
            _logger.LogError("No {File} in {Game}, cannot patch the runtime", RuntimeFileName, gamePath);
            return false;
        }
        if (!status.UnityVersionSupported)
        {
            _logger.LogError("Patched Mono runtimes target Unity {Expected}, game is {Actual}",
                SupportedUnityVersion, status.UnityVersion ?? "unknown");
            return false;
        }

        var release = await GetLatestReleaseAsync(channel, ct);
        if (release == null)
        {
            _logger.LogError("No {Channel} Mono runtime release in {Url}", channel, _feedUrl);
            return false;
        }
        if (status.Kind == MonoRuntimeKind.Modified
            && status.PatchId == Sanitize(release.Id) && status.PatchVersion == Sanitize(release.Version))
        {
            _logger.LogInformation("Mono runtime is already {Id} {Version}", release.Id, release.Version);
            return true;
        }

        try
        {
            var patched = await GetCachedReleaseAsync(release, progress, ct);
            if (patched == null) return false;

            var runtime = GetRuntimePath(gamePath);
            var backup = GetBackupPath(gamePath);

            // Only the unmodified runtime is backed up, so the backup never
            // holds a patched build. A modified runtime is replaced without one.
            if (status.Kind == MonoRuntimeKind.Stock)
            {
                _logger.LogInformation("Backing up {Runtime} to {Backup}", runtime, backup);
                ReplaceFile(runtime, backup);
            }

            ReplaceFile(patched, runtime);
            _logger.LogInformation("Mono runtime {Id} {Version} installed into {Game}",
                release.Id, release.Version, gamePath);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mono runtime install failed for {Game}", gamePath);
            return false;
        }
    }

    public Task<bool> RestoreOriginalAsync(string gamePath, CancellationToken ct = default)
    {
        var backup = GetBackupPath(gamePath);
        if (!File.Exists(backup))
        {
            _logger.LogError("No {Backup} in {Game}, cannot restore the original runtime", BackupFileName, gamePath);
            return Task.FromResult(false);
        }
        if (!IsStock(backup))
        {
            _logger.LogError("{Backup} in {Game} is not the unmodified runtime; not restoring", BackupFileName, gamePath);
            return Task.FromResult(false);
        }
        // After a game update to another Unity version the backup is a stale
        // 2019.4 runtime; putting it back would crash the game.
        var unityVersion = ReadUnityVersion(gamePath);
        if (unityVersion != SupportedUnityVersion)
        {
            _logger.LogError("{Backup} is a Unity {Expected} runtime, game is {Actual}; not restoring",
                BackupFileName, SupportedUnityVersion, unityVersion ?? "unknown");
            return Task.FromResult(false);
        }

        try
        {
            ReplaceFile(backup, GetRuntimePath(gamePath));
            File.Delete(backup);
            _logger.LogInformation("Original Mono runtime restored in {Game}", gamePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restoring the original Mono runtime failed for {Game}", gamePath);
            return Task.FromResult(false);
        }
    }

    // ── Download ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the cached DLL of <paramref name="release"/>, downloading it
    /// first when it is not cached yet. When the feed gives a Sha256Sum, both
    /// the cached file and the download must match it. Null when the download
    /// fails or does not match.
    /// </summary>
    private async Task<string?> GetCachedReleaseAsync(MonoRuntimeRelease release,
        IProgress<double>? progress, CancellationToken ct)
    {
        var cache = GetCachePath(release);
        if (File.Exists(cache))
        {
            if (release.Sha256 == null || TryHashFile(cache) == release.Sha256) return cache;
            _logger.LogWarning("Cached {Path} does not match the feed's Sha256Sum, downloading again", cache);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var temp = cache + ".download";

        _logger.LogInformation("Downloading Mono runtime {Id} {Version} from {Url}",
            release.Id, release.Version, release.DownloadUrl);
        try
        {
            var http = _httpFactory.CreateClient("github");
            using var response = await http.GetAsync(
                release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var netStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = File.Create(temp))
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

            if (release.Sha256 != null && TryHashFile(temp) is var hash && hash != release.Sha256)
            {
                _logger.LogError("Mono runtime download {Url} has SHA-256 {Actual}, feed says {Expected}",
                    release.DownloadUrl, hash, release.Sha256);
                return null;
            }

            // Only a complete, verified download ever lands at the cache path.
            File.Move(temp, cache, overwrite: true);
            return cache;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Mono runtime {Id} {Version}", release.Id, release.Version);
            return null;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best-effort cleanup */ }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies <paramref name="source"/> over <paramref name="destination"/> via
    /// a temp file in the destination folder, so a failed copy never leaves a
    /// half-written runtime DLL behind.
    /// </summary>
    private static void ReplaceFile(string source, string destination)
    {
        var temp = destination + ".tmp";
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string? TryHashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    // Matches 2019.4.40f1 as well as Unity 6 versions like 6000.0.58f2.
    [GeneratedRegex(@"\d{4}\.\d+\.\d+[abfp]\d+")]
    private static partial Regex UnityVersionPattern();

    /// <summary>
    /// Reads the Unity version (e.g. "2019.4.40f1") from the header of
    /// <c>globalgamemanagers</c>. Works for Windows builds on any host OS,
    /// unlike reading UnityPlayer.dll's version resource.
    /// </summary>
    internal static string? ReadUnityVersion(string gamePath)
    {
        var path = Path.Combine(gamePath, "DerailValley_Data", "globalgamemanagers");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var header = new byte[256];
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            var match = UnityVersionPattern().Match(Encoding.ASCII.GetString(header, 0, read));
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }
}
