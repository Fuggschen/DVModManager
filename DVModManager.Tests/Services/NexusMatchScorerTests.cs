using DVModManager.Models;
using DVModManager.Services;
using Xunit;

namespace DVModManager.Tests.Services;

public class NexusMatchScorerTests
{
    private static IReadOnlyList<NexusCandidate> Rank(params (string Signal, int[] Ids)[] hits) =>
        NexusMatchScorer.Rank(hits.Select(h => (h.Signal, (IEnumerable<int>)h.Ids)));

    [Fact]
    public void Rank_SumsSignalsPerModHighestFirst()
    {
        var ranked = Rank(("dir", [10]), ("dll", [10, 20]), ("fp1", [10]), ("fp1", [20, 20]));

        Assert.Equal(10, ranked[0].NexusModId);
        Assert.Equal(3, ranked[0].Score);
        Assert.Equal(["dir", "dll", "fp1"], ranked[0].Signals);
        Assert.Equal(2, ranked[1].Score);
    }

    [Fact]
    public void Decide_NoCandidates_IsNotFound()
    {
        var match = NexusMatchScorer.Decide(Rank(("dir", [])), null, "1.0", null);

        Assert.Equal(NexusMatchCertainty.NotFound, match.Certainty);
        Assert.Null(match.Best);
    }

    [Fact]
    public void Decide_ScoreThreeWinner_IsSureEvenWithRivals()
    {
        var match = NexusMatchScorer.Decide(Rank(("dir", [1]), ("dll", [1]), ("fp1", [1, 2])), null, null, null);

        Assert.Equal(NexusMatchCertainty.Sure, match.Certainty);
        Assert.Equal(1, match.Best!.NexusModId);
    }

    [Fact]
    public void Decide_LoneScoreTwo_IsSure()
    {
        var match = NexusMatchScorer.Decide(Rank(("dll", [5]), ("fp1", [5])), null, null, null);

        Assert.Equal(NexusMatchCertainty.Sure, match.Certainty);
    }

    [Fact]
    public void Decide_ScoreTwoWithRival_IsUncertainButKeepsBest()
    {
        var match = NexusMatchScorer.Decide(Rank(("dll", [5]), ("fp1", [5, 6])), null, null, null);

        Assert.Equal(NexusMatchCertainty.Uncertain, match.Certainty);
        Assert.Equal(5, match.Best!.NexusModId);
        Assert.Equal(2, match.Candidates.Count);
    }

    [Fact]
    public void Decide_LoneScoreOne_IsUncertain()
    {
        var match = NexusMatchScorer.Decide(Rank(("repo", [7])), null, null, null);

        Assert.Equal(NexusMatchCertainty.Uncertain, match.Certainty);
    }

    [Fact]
    public void Decide_TieBrokenByVersion()
    {
        var details = new Dictionary<int, NexusModDetails>
        {
            [151] = new("Old", "3.0.0", "a"),
            [794] = new("New", "v4.0.0", "b")
        };
        var ranked = Rank(("dll", [151, 794]), ("fp1", [151]), ("repo", [794]), ("fp2", [151, 794]));

        var match = NexusMatchScorer.Decide(ranked, details, "4.0.0", null);

        Assert.Equal(794, match.Best!.NexusModId);
        Assert.Equal(NexusMatchCertainty.Sure, match.Certainty);
    }

    [Fact]
    public void Decide_TieBrokenByAuthorWhenVersionsDoNotHelp()
    {
        var details = new Dictionary<int, NexusModDetails>
        {
            [1] = new("A", "1.0", "Someone Else"),
            [2] = new("B", "1.0", "Cruzer Blade")
        };
        var ranked = Rank(("dll", [1, 2]), ("fp1", [1, 2]));

        var match = NexusMatchScorer.Decide(ranked, details, "1.0", "CruzerBlade");

        Assert.Equal(2, match.Best!.NexusModId);
        Assert.Equal(NexusMatchCertainty.Uncertain, match.Certainty); // score 2 with a rival
    }

    [Fact]
    public void Decide_TieAtScoreOne_IsNotBroken()
    {
        var details = new Dictionary<int, NexusModDetails>
        {
            [1] = new("A", "1.0.0", null),
            [2] = new("B", "2.0.0", null)
        };

        var match = NexusMatchScorer.Decide(Rank(("fp1", [1, 2])), details, "1.0.0", null);

        Assert.Equal(NexusMatchCertainty.Uncertain, match.Certainty);
        Assert.Null(match.Best);
    }
}
