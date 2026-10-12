using System.Text.Json;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

/// <summary>
/// Reads Unity Mod Manager's official self-update feeds (the same JSON UMM's own
/// updater consumes) and returns release candidates sorted by version.
/// </summary>
/// <remarks>
/// Two feeds exist because UMM carries a legacy one alongside the current one;
/// taking the highest version across both makes the split harmless (the legacy
/// feed tops out at 0.12.x and its download is only a patch package).
/// </remarks>
public class UmmReleaseFeedService : IUmmReleaseFeedService
{
    /// <summary>UMM's published self-update feeds, current first.</summary>
    public static readonly IReadOnlyList<string> FeedUrls =
    [
        "https://raw.githubusercontent.com/newman55/unity-mod-manager/master/Repository_beta_13.json",
        "https://raw.githubusercontent.com/newman55/unity-mod-manager/master/Repository.json"
    ];

    private const string ReleaseId = "UnityModManager";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UmmReleaseFeedService> _logger;

    public UmmReleaseFeedService(IHttpClientFactory httpFactory, ILogger<UmmReleaseFeedService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UmmReleaseInfo>> GetCandidatesAsync(CancellationToken ct = default)
    {
        var releases = new List<UmmReleaseInfo>();

        foreach (var feedUrl in FeedUrls)
        {
            try
            {
                var http = _httpFactory.CreateClient("github");
                using var response = await http.GetAsync(feedUrl, ct);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);

                foreach (var release in ParseFeed(json, feedUrl))
                    releases.Add(release);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read UMM release feed {Url}", feedUrl);
            }
        }

        return releases
            .OrderByDescending(r => ParseVersionOrNull(r.Version) ?? new Version(0, 0))
            .ToList();
    }

    /// <summary>
    /// Parses UMM's feed shape: <c>{"Releases":[{"Id","Version","DownloadUrl"}]}</c>.
    /// Only the UnityModManager self-release is of interest; mod release feeds
    /// use the same schema and must be ignored here.
    /// </summary>
    public static IReadOnlyList<UmmReleaseInfo> ParseFeed(string json, string feedUrl)
    {
        var result = new List<UmmReleaseInfo>();
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
                var id = GetString(item, "Id");
                if (!string.Equals(id, ReleaseId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var version = GetString(item, "Version");
                var url = GetString(item, "DownloadUrl");
                if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url))
                    continue;

                if (ParseVersionOrNull(version) == null)
                    continue;

                result.Add(new UmmReleaseInfo
                {
                    Version = version.Trim(),
                    DownloadUrl = url.Trim(),
                    FeedUrl = feedUrl
                });
            }
        }
        catch (Exception)
        {
            // Malformed feed
        }
        return result;
    }

    /// <summary>
    /// Tolerant version parse in UMM's own style (non-digits stripped per part),
    /// so versions like "0.33.2" or "v0.33" compare correctly.
    /// </summary>
    public static Version? ParseVersionOrNull(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split('.');
        var numbers = new List<int>(4);
        foreach (var part in parts)
        {
            var digits = new string(part.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) continue;
            if (!int.TryParse(digits, out var n)) return null;
            numbers.Add(n);
        }

        return numbers.Count switch
        {
            0 => null,
            1 => new Version(numbers[0], 0),
            2 => new Version(numbers[0], numbers[1]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            _ => new Version(numbers[0], numbers[1], numbers[2], numbers[3])
        };
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }
        return null;
    }
}
