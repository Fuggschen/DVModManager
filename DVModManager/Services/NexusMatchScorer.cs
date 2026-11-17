using System.Text.RegularExpressions;
using DVModManager.Models;

namespace DVModManager.Services;

/// <summary>Version and author of a Nexus mod, used to break ties between candidates.</summary>
public sealed record NexusModDetails(string? Name, string? Version, string? Author);

/// <summary>
/// Turns search signal hits into a decision. Every signal that hits a Nexus mod
/// adds one point; the highest total wins.
/// </summary>
/// <remarks>
/// Thresholds come from a dry run over 106 installed mods: a winner with 3+
/// points, or 2 points and no rival, was never wrong. A single point is too weak
/// to trust (a shared library or a repo mentioned in another mod's description).
/// </remarks>
public static class NexusMatchScorer
{
    public const int SureScore = 3;
    public const int LoneSureScore = 2;

    /// <summary>Sums the hits per Nexus mod, highest score first.</summary>
    public static IReadOnlyList<NexusCandidate> Rank(IEnumerable<(string Signal, IEnumerable<int> NexusModIds)> hits)
    {
        var signals = new Dictionary<int, List<string>>();
        foreach (var (signal, ids) in hits)
        {
            foreach (var id in ids.Distinct())
            {
                if (!signals.TryGetValue(id, out var list)) signals[id] = list = [];
                list.Add(signal);
            }
        }

        return signals
            .Select(kv => new NexusCandidate(kv.Key, kv.Value.Count, kv.Value))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.NexusModId)
            .ToList();
    }

    /// <summary>True when the top score is shared and high enough for a tie-break.</summary>
    public static bool NeedsTieBreak(IReadOnlyList<NexusCandidate> ranked) =>
        ranked.Count > 1 && ranked[0].Score >= LoneSureScore && ranked[1].Score == ranked[0].Score;

    /// <summary>
    /// Picks the winner. A shared top score is broken by the Nexus version equal to
    /// <paramref name="version"/>, then by the Nexus author equal to <paramref name="author"/>.
    /// </summary>
    public static NexusMatch Decide(
        IReadOnlyList<NexusCandidate> ranked,
        IReadOnlyDictionary<int, NexusModDetails>? details,
        string? version, string? author)
    {
        if (ranked.Count == 0) return NexusMatch.NotFound;

        var top = ranked[0];
        var tied = ranked.Where(c => c.Score == top.Score).ToList();
        NexusCandidate? winner = tied.Count == 1 ? top : null;

        if (winner == null && NeedsTieBreak(ranked) && details != null)
        {
            var byVersion = tied.Where(c => SameVersion(details, c, version)).ToList();
            if (byVersion.Count == 1)
            {
                winner = byVersion[0];
            }
            else
            {
                var pool = byVersion.Count > 0 ? byVersion : tied;
                var byAuthor = pool.Where(c => SameAuthor(details, c, author)).ToList();
                if (byAuthor.Count == 1) winner = byAuthor[0];
            }
        }

        var sure = winner != null
            && (winner.Score >= SureScore || (winner.Score == LoneSureScore && ranked.Count == 1));

        return new NexusMatch
        {
            Certainty = sure ? NexusMatchCertainty.Sure : NexusMatchCertainty.Uncertain,
            Best = winner,
            Candidates = ranked
        };
    }

    private static bool SameVersion(IReadOnlyDictionary<int, NexusModDetails> details, NexusCandidate c, string? version) =>
        !string.IsNullOrWhiteSpace(version)
        && details.TryGetValue(c.NexusModId, out var d)
        && string.Equals(NormalizeVersion(d.Version), NormalizeVersion(version), StringComparison.OrdinalIgnoreCase);

    private static bool SameAuthor(IReadOnlyDictionary<int, NexusModDetails> details, NexusCandidate c, string? author)
    {
        var wanted = NormalizeText(author);
        return wanted.Length > 0
            && details.TryGetValue(c.NexusModId, out var d)
            && NormalizeText(d.Author) == wanted;
    }

    private static string NormalizeVersion(string? v) => (v ?? "").Trim().TrimStart('v', 'V');

    private static string NormalizeText(string? s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^0-9a-z]", "");
}
