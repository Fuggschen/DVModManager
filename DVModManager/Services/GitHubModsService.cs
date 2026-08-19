using System.Text.Json;
using System.Text.RegularExpressions;
using DVModManager.Models;
using Microsoft.Extensions.Logging;
using Octokit;

namespace DVModManager.Services;

public class GitHubModsService : IGitHubModsService
{
    private readonly ILogger<GitHubModsService> _logger;
    private GitHubClient? _client;

    public GitHubModsService(ILogger<GitHubModsService> logger)
    {
        _logger = logger;
    }

    private GitHubClient GetClient()
    {
        if (_client != null) return _client;

        _client = new GitHubClient(new ProductHeaderValue("DVModManager"));

        return _client;
    }

    public async Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mod.Repository)) return null;

        // UMM convention: Repository may point directly to a releases JSON file
        if (mod.Repository.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return await CheckUpdateViaJsonAsync(mod, ct);

        var (owner, repo) = ParseGitHubUrl(mod.Repository);
        if (owner == null || repo == null) return null;

        try
        {
            var release = await GetClient().Repository.Release.GetLatest(owner, repo);
            var latestVersion = NormalizeVersion(release.TagName);

            // If we can't parse a valid semver from the tag, skip — don't use string compare
            if (!Version.TryParse(latestVersion, out _))
            {
                _logger.LogDebug(
                    "Skipping update for {Id}: tag '{Tag}' couldn't be parsed as a version",
                    mod.Id, release.TagName);
                return null;
            }

            if (!IsNewerVersion(mod.Version, latestVersion)) return null;

            // Find the best downloadable asset (prefer .zip)
            var asset = release.Assets
                .OrderByDescending(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            return new ModUpdateInfo
            {
                ModId = mod.Id,
                CurrentVersion = mod.Version,
                LatestVersion = latestVersion,
                Source = "github",
                DownloadUrl = asset?.BrowserDownloadUrl,
                ChangelogUrl = release.HtmlUrl,
                ReleasedAt = release.PublishedAt?.UtcDateTime,
                ReleaseNotes = release.Body
            };
        }
        catch (NotFoundException)
        {
            _logger.LogDebug("No GitHub releases found for {Owner}/{Repo}", owner, repo);
            return null;
        }
        catch (RateLimitExceededException ex)
        {
            _logger.LogWarning("GitHub rate limit exceeded. Resets at {Reset}", ex.Reset);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking GitHub update for {Id}", mod.Id);
            return null;
        }
    }

    /// <summary>
    /// UMM mods sometimes set Repository to a raw URL pointing to a releases.json / repository.json.
    /// Format expected: { "Releases": [ { "Version": "1.2.3", "DownloadUrl": "..." } ] }
    /// </summary>
    private async Task<ModUpdateInfo?> CheckUpdateViaJsonAsync(ModInfo mod, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DVModManager/1.0");
            var json = await http.GetStringAsync(mod.Repository, ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Support two common shapes: top-level "Version" or "Releases[0].Version"
            string? latestVersion = null;
            string? downloadUrl = null;

            if (doc.RootElement.TryGetProperty("Version", out var vProp))
                latestVersion = vProp.GetString();

            if (doc.RootElement.TryGetProperty("Releases", out var releases) &&
                releases.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var r in releases.EnumerateArray())
                {
                    if (r.TryGetProperty("Version", out var rv))
                        latestVersion ??= rv.GetString();
                    if (r.TryGetProperty("DownloadUrl", out var du))
                        downloadUrl ??= du.GetString();
                    break; // first entry is the latest
                }
            }

            if (latestVersion == null) return null;
            latestVersion = NormalizeVersion(latestVersion);
            if (!IsNewerVersion(mod.Version, latestVersion)) return null;

            return new ModUpdateInfo
            {
                ModId = mod.Id,
                CurrentVersion = mod.Version,
                LatestVersion = latestVersion,
                Source = "github",
                DownloadUrl = downloadUrl,
                ChangelogUrl = mod.Repository
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching releases JSON for {Id} from {Url}", mod.Id, mod.Repository);
            return null;
        }
    }

    public async Task<string> DownloadReleaseAssetAsync(
        string downloadUrl, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            // GitHub release asset downloads are plain HTTPS — use HttpClient
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DVModManager/1.0");

            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(destinationPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (totalBytes.HasValue)
                    progress?.Report((double)downloaded / totalBytes.Value);
            }

            return destinationPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading release asset from {Url}", downloadUrl);
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string? Owner, string? Repo) ParseGitHubUrl(string url)
    {
        var match = Regex.Match(url, @"github\.com/([^/]+)/([^/\s?#]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return (null, null);
        return (match.Groups[1].Value, match.Groups[2].Value.TrimEnd('/'));
    }

    /// <summary>
    /// Extracts the numeric semver from a release tag.
    /// Handles: "v1.2.3", "V1.2.3", "ModName-v1.2.3", "ModName_v1.2.3", "1.2.3"
    /// </summary>
    private static string NormalizeVersion(string tag)
    {
        var s = tag.Trim();

        // Simple leading-v case
        if (Regex.IsMatch(s, @"^[vV]\d")) return s[1..].Trim();

        // Complex tag: extract trailing semver-like number (e.g. "ModName-v1.2.3" → "1.2.3")
        var match = Regex.Match(s, @"[vV]?(\d+\.\d+(?:\.\d+){0,2})$");
        if (match.Success) return match.Groups[1].Value;

        return s;
    }

    /// <summary>
    /// Returns true only when both versions parse as System.Version and latest &gt; current.
    /// Deliberately returns false when versions are unparseable to avoid false positives.
    /// </summary>
    private static bool IsNewerVersion(string current, string latest)
    {
        if (Version.TryParse(NormalizeVersion(current), out var c) &&
            Version.TryParse(NormalizeVersion(latest), out var l))
            return l > c;

        // Cannot compare reliably — do not report as an update
        return false;
    }
}
