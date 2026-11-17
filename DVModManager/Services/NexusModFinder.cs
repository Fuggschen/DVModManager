using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

/// <summary>
/// Finds the Nexus page of a local mod through the v2 GraphQL API. All signals go
/// out in one aliased request; a second round only fetches names, versions and
/// authors of the candidates. No API key is needed.
/// </summary>
public sealed class NexusModFinder : INexusModFinder
{
    private const int GameId = 2750; // Derail Valley
    private const string GraphQLUrl = "https://api.nexusmods.com/v2/graphql";
    // Candidates past this are dropped unchecked; by then their score is far below the top.
    private const int MaxCandidateDetails = 10;

    private static readonly Regex GitHubRepoPattern = new(
        @"(?:github\.com|raw\.githubusercontent\.com)/([^/\s?#]+)/([^/\s?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<NexusModFinder> _logger;

    public NexusModFinder(IHttpClientFactory httpClientFactory, ILogger<NexusModFinder> logger)
    {
        _http = httpClientFactory.CreateClient("nexus");
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("DVModManager/1.0");
        _logger = logger;
    }

    public async Task<NexusMatch> FindAsync(ModSearchKey key, CancellationToken ct = default)
    {
        try
        {
            var (query, variables) = BuildSignalQuery(key);
            var data = await PostAsync(query, variables, ct);
            if (data == null) return NexusMatch.Failed;

            var ranked = NexusMatchScorer.Rank(ReadHits(data.Value, key));
            if (ranked.Count == 0) return NexusMatch.NotFound;

            // The file index still lists uploads of hidden or deleted mods. Their page
            // cannot be opened, so only candidates whose page still exists take part.
            var details = await FetchDetailsAsync(ranked.Take(MaxCandidateDetails).Select(c => c.NexusModId), ct);
            var visible = ranked
                .Where(c => details.ContainsKey(c.NexusModId))
                .Select(c => c with { Name = details[c.NexusModId].Name })
                .ToList();
            if (visible.Count == 0) return NexusMatch.NotFound;

            return NexusMatchScorer.Decide(visible, details, key.Version, key.Author);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nexus search failed for {Id}", key.ModId);
            return NexusMatch.Failed;
        }
    }

    // The file index filters are case-sensitive, so both common Info.json spellings are queried.
    private static (string Query, Dictionary<string, object> Variables) BuildSignalQuery(ModSearchKey key)
    {
        var variables = new Dictionary<string, object>
        {
            ["gameId"] = GameId,
            ["modId"] = key.ModId,
            ["dll"] = key.ModId + ".dll"
        };
        var declarations = new StringBuilder("$gameId: Int!, $modId: String!, $dll: String!");
        var fields = new StringBuilder();

        fields.Append(FileQuery("dirU", "filePathPartsExact: [{value: $modId}], fileNameWildcard: [{value: \"Info.json\"}]"));
        fields.Append(FileQuery("dirL", "filePathPartsExact: [{value: $modId}], fileNameWildcard: [{value: \"info.json\"}]"));
        fields.Append(FileQuery("dll", "fileNameWildcard: [{value: $dll}]"));

        for (var i = 0; i < key.Fingerprint.Count; i++)
        {
            variables[$"fpName{i}"] = key.Fingerprint[i].Name;
            variables[$"fpSize{i}"] = key.Fingerprint[i].Size.ToString();
            declarations.Append($", $fpName{i}: String!, $fpSize{i}: String!");
            fields.Append(FileQuery($"fp{i}",
                $"fileSize: [{{value: $fpSize{i}, op: EQUALS}}], fileNameWildcard: [{{value: $fpName{i}}}]"));
        }

        if ((RepoSlug(key.RepositoryUrl) ?? RepoSlug(key.HomePageUrl)) is { } slug)
        {
            variables["repo"] = slug;
            declarations.Append(", $repo: String!");
            // The mods filter ignores gameId given as a string, so the domain name is used.
            // EQUALS on the description is a phrase match; MATCHES returns hundreds of unrelated mods.
            fields.Append(" repo: mods(filter: {gameDomainName: [{value: \"derailvalley\"}], description: [{value: $repo, op: EQUALS}]}, count: 10) { nodes { modId } }");
        }

        return ($"query ({declarations}) {{{fields} }}", variables);

        static string FileQuery(string alias, string filter) =>
            $" {alias}: modFileContents(filter: {{gameId: [{{value: $gameId}}], {filter}}}, count: 50) {{ nodes {{ modId filePath }} }}";
    }

    private static IEnumerable<(string, IEnumerable<int>)> ReadHits(JsonElement data, ModSearchKey key)
    {
        // filePathPartsExact matches the folder at any depth; only <ModId>/Info.json is a mod root.
        var rootSuffix = "/" + key.ModId + "/info.json";
        var dirIds = new[] { "dirU", "dirL" }
            .SelectMany(alias => Nodes(data, alias))
            .Where(n => ("/" + n.GetProperty("filePath").GetString()).EndsWith(rootSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(ModIdOf)
            .ToList();
        yield return ("dir", dirIds);
        yield return ("dll", Nodes(data, "dll").Select(ModIdOf).ToList());

        for (var i = 0; i < key.Fingerprint.Count; i++)
            yield return ($"fp{i + 1}", Nodes(data, $"fp{i}").Select(ModIdOf).ToList());

        yield return ("repo", Nodes(data, "repo").Select(ModIdOf).ToList());
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement data, string alias) =>
        data.TryGetProperty(alias, out var field) && field.ValueKind == JsonValueKind.Object
            ? field.GetProperty("nodes").EnumerateArray()
            : [];

    private static int ModIdOf(JsonElement node) => node.GetProperty("modId").GetInt32();

    private static string? RepoSlug(string? url)
    {
        var match = GitHubRepoPattern.Match(url ?? "");
        if (!match.Success) return null;
        var repo = match.Groups[2].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return $"{match.Groups[1].Value}/{repo}";
    }

    /// <summary>
    /// Fetches each candidate on its own: one hidden or deleted mod fails the whole
    /// request, which would lose the others' details.
    /// </summary>
    private async Task<Dictionary<int, NexusModDetails>> FetchDetailsAsync(IEnumerable<int> nexusModIds, CancellationToken ct)
    {
        const string query = "query ($gameId: ID!, $modId: ID!) { mod(gameId: $gameId, modId: $modId) { name version author } }";
        var tasks = nexusModIds.Select(async id =>
        {
            var data = await PostAsync(query, new Dictionary<string, object> { ["gameId"] = GameId.ToString(), ["modId"] = id.ToString() }, ct);
            if (data is not { } d || !d.TryGetProperty("mod", out var mod) || mod.ValueKind != JsonValueKind.Object)
                return (id, (NexusModDetails?)null);
            return (id, new NexusModDetails(Text(mod, "name"), Text(mod, "version"), Text(mod, "author")));
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r.Item2 != null).ToDictionary(r => r.id, r => r.Item2!);

        static string? Text(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>
    /// Returns the <c>data</c> element, or null on GraphQL errors (such as a hidden
    /// or deleted mod). HTTP failures throw, so they are never mistaken for those.
    /// </summary>
    private async Task<JsonElement?> PostAsync(string query, Dictionary<string, object> variables, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(GraphQLUrl, new { query, variables }, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Nexus GraphQL returned {Code}", response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        if (doc == null) return null;

        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            var msg = errors[0].TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            _logger.LogDebug("Nexus GraphQL error: {Error}", msg);
            return null;
        }

        return doc.RootElement.TryGetProperty("data", out var data) ? data.Clone() : null;
    }
}
