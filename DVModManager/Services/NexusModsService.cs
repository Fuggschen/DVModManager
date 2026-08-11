using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class NexusModsService : INexusModsService
{
    private const string GameDomain = "derailvalley";
    private const string BaseUrl = "https://api.nexusmods.com/v1";

    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly ILogger<NexusModsService> _logger;

    public NexusModsService(ISettingsService settings, IHttpClientFactory httpClientFactory, ILogger<NexusModsService> logger)
    {
        _settings = settings;
        _http = httpClientFactory.CreateClient("nexus");
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.NexusApiKey);

    public async Task<ModUpdateInfo?> CheckUpdateAsync(ModInfo mod, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        var nexusId = ExtractNexusModId(mod.HomePage);
        if (nexusId == null) return null;

        try
        {
            ConfigureHeaders();
            var response = await _http.GetAsync(
                $"{BaseUrl}/games/{GameDomain}/mods/{nexusId}.json", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nexus API returned {Code} for mod {Id}", response.StatusCode, mod.Id);
                return null;
            }

            var nexusMod = await response.Content.ReadFromJsonAsync<NexusModDto>(cancellationToken: ct);
            if (nexusMod == null) return null;

            var latestVersion = nexusMod.Version?.Trim() ?? "";
            if (!IsNewerVersion(mod.Version, latestVersion)) return null;

            return new ModUpdateInfo
            {
                ModId = mod.Id,
                CurrentVersion = mod.Version,
                LatestVersion = latestVersion,
                Source = "nexus",
                ChangelogUrl = $"https://www.nexusmods.com/{GameDomain}/mods/{nexusId}",
                ReleasedAt = nexusMod.UpdatedTime.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(nexusMod.UpdatedTime.Value).UtcDateTime
                    : null
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
        _http.DefaultRequestHeaders.Remove("apikey");
        _http.DefaultRequestHeaders.Add("apikey", _settings.Settings.NexusApiKey);
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

    // ── Nexus DTO ─────────────────────────────────────────────────────────────

    private sealed class NexusModDto
    {
        public string? Version { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("updated_time")]
        public System.Text.Json.JsonElement? UpdatedTimeRaw { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public long? UpdatedTime
        {
            get
            {
                if (UpdatedTimeRaw is not { } elem) return null;
                try
                {
                    return elem.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.Number => elem.GetInt64(),
                        System.Text.Json.JsonValueKind.String
                            => long.TryParse(elem.GetString(), out var v) ? v : null,
                        _ => null
                    };
                }
                catch { return null; }
            }
        }
    }
}
