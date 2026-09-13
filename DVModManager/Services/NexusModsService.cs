using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class NexusModsService : INexusModsService
{
    private const int GameId = 2750; // Derail Valley
    private const string GraphQLUrl = "https://api.nexusmods.com/v2/graphql";

    private readonly HttpClient _http;
    private readonly ILogger<NexusModsService> _logger;

    public NexusModsService(IHttpClientFactory httpClientFactory, ILogger<NexusModsService> logger)
    {
        _http = httpClientFactory.CreateClient("nexus");
        _logger = logger;
    }

    public async Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default)
    {
        var nexusId = ExtractNexusModId(mod.HomePage);
        if (nexusId == null) return null;

        try
        {
            var payload = new
            {
                query = @"query ($gameId: ID!, $modId: ID!) {
                    mod(gameId: $gameId, modId: $modId) {
                        version
                        updatedAt
                    }
                }",
                variables = new { gameId = GameId, modId = nexusId.ToString() }
            };

            ConfigureHeaders();
            var response = await _http.PostAsJsonAsync(GraphQLUrl, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nexus GraphQL API returned {Code} for mod {Id}", response.StatusCode, mod.Id);
                return null;
            }

            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc == null) return null;

            // Handle GraphQL errors
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var msg = errors[0].TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                _logger.LogWarning("Nexus GraphQL error for mod {Id}: {Error}", mod.Id, msg);
                return null;
            }

            var data = doc.RootElement.GetProperty("data");
            if (data.ValueKind == JsonValueKind.Null || !data.TryGetProperty("mod", out var modNode) || modNode.ValueKind == JsonValueKind.Null)
                return null;

            var latestVersion = modNode.GetProperty("version").GetString()?.Trim() ?? "";
            if (!IsNewerVersion(mod.Version, latestVersion)) return null;

            // GraphQL returns updatedAt as ISO 8601 string
            DateTime? releasedAt = null;
            if (modNode.TryGetProperty("updatedAt", out var updatedNode) &&
                updatedNode.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(updatedNode.GetString(), out var dto))
            {
                releasedAt = dto.UtcDateTime;
            }

            return new ModUpdateInfo
            {
                ModId = mod.Id,
                CurrentVersion = mod.Version,
                LatestVersion = latestVersion,
                Source = "nexus",
                ChangelogUrl = $"https://www.nexusmods.com/derailvalley/mods/{nexusId}",
                ReleasedAt = releasedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Nexus update for {Id}", mod.Id);
            return null;
        }
    }

    public async Task<string> DownloadModFileAsync(
        string downloadUrl, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ConfigureHeaders();
        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ConfigureHeaders()
    {
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("DVModManager/1.0");
    }

    private static int? ExtractNexusModId(string? homePage)
    {
        if (string.IsNullOrEmpty(homePage)) return null;
        var match = Regex.Match(homePage, @"nexusmods\.com/[^/]+/mods/(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    private static bool IsNewerVersion(string current, string latest)
    {
        if (Version.TryParse(Normalize(current), out var c) &&
            Version.TryParse(Normalize(latest), out var l))
            return l > c;

        // Cannot compare reliably — do not report as an update
        return false;
    }

    private static string Normalize(string v) => v.TrimStart('v', 'V').Trim();
}