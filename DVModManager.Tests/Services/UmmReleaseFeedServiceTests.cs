using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class UmmReleaseFeedServiceTests
{
    [Fact]
    public void ParseFeed_ReturnsUnityModManagerReleasesOnly()
    {
        var json = """
        {
          "Releases": [
            { "Id": "UnityModManager", "Version": "0.33.2", "DownloadUrl": "https://example.invalid/umm_update.zip" },
            { "Id": "SomeMod", "Version": "9.9.9", "DownloadUrl": "https://example.invalid/mod.zip" }
          ]
        }
        """;

        var releases = UmmReleaseFeedService.ParseFeed(json, "https://example.invalid/feed.json");

        var release = Assert.Single(releases);
        Assert.Equal("0.33.2", release.Version);
        Assert.Equal("https://example.invalid/umm_update.zip", release.DownloadUrl);
    }

    [Fact]
    public void ParseFeed_MalformedJson_ReturnsEmpty()
    {
        Assert.Empty(UmmReleaseFeedService.ParseFeed("not json", "feed"));
    }

    [Theory]
    [InlineData("0.33.2", "0.33.2")]
    [InlineData("v0.33", "0.33")]
    [InlineData("garbage", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseVersionOrNull_HandlesUmmVersionStyles(string? input, string? expected)
    {
        Assert.Equal(expected, UmmReleaseFeedService.ParseVersionOrNull(input)?.ToString());
    }

    [Fact]
    public async Task GetCandidatesAsync_SortsByVersionAcrossFeeds()
    {
        var handler = new StubHttpMessageHandler();
        // The legacy feed reports an older version than the current feed —
        // the merged list must still come out newest-first.
        handler.Map(UmmReleaseFeedService.FeedUrls[0],
            """{ "Releases": [ { "Id": "UnityModManager", "Version": "0.33.2", "DownloadUrl": "https://example.invalid/new.zip" } ] }""");
        handler.Map(UmmReleaseFeedService.FeedUrls[1],
            """{ "Releases": [ { "Id": "UnityModManager", "Version": "0.12.7", "DownloadUrl": "https://example.invalid/old.zip" } ] }""");

        var service = new UmmReleaseFeedService(
            new StubHttpClientFactory(handler), NullLogger<UmmReleaseFeedService>.Instance);

        var candidates = await service.GetCandidatesAsync();

        Assert.Equal(2, candidates.Count);
        Assert.Equal("0.33.2", candidates[0].Version);
        Assert.Equal("0.12.7", candidates[1].Version);
    }

    [Fact]
    public async Task GetCandidatesAsync_SurvivesDeadFeeds()
    {
        var handler = new StubHttpMessageHandler(); // every URL → 404

        var service = new UmmReleaseFeedService(
            new StubHttpClientFactory(handler), NullLogger<UmmReleaseFeedService>.Instance);

        var candidates = await service.GetCandidatesAsync();

        Assert.Empty(candidates);
    }
}
