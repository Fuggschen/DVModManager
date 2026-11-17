namespace DVModManager.Models;

public enum NexusMatchCertainty
{
    Sure,
    Uncertain,
    NotFound,
    Failed   // the request itself failed, so the result is unknown
}

/// <summary>A Nexus mod that one or more search signals pointed at.</summary>
/// <param name="Signals">Which signals hit it, e.g. "dir", "dll", "fp1", "repo".</param>
public sealed record NexusCandidate(int NexusModId, int Score, IReadOnlyList<string> Signals)
{
    /// <summary>Title on Nexus; null when it was not fetched or the page is hidden.</summary>
    public string? Name { get; init; }
}

public sealed class NexusMatch
{
    public NexusMatchCertainty Certainty { get; init; }

    /// <summary>The chosen candidate; null when nothing matched or a tie stayed open.</summary>
    public NexusCandidate? Best { get; init; }

    /// <summary>All candidates, highest score first.</summary>
    public IReadOnlyList<NexusCandidate> Candidates { get; init; } = [];

    public static NexusMatch NotFound { get; } = new() { Certainty = NexusMatchCertainty.NotFound };
    public static NexusMatch Failed { get; } = new() { Certainty = NexusMatchCertainty.Failed };
}
