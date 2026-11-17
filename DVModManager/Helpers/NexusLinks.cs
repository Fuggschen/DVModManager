using System.Text.RegularExpressions;

namespace DVModManager.Helpers;

/// <summary>Builds and parses Derail Valley mod page URLs on Nexus Mods.</summary>
public static class NexusLinks
{
    // Nexus serves the same page under /derailvalley/mods/ and /games/derailvalley/mods/.
    private static readonly Regex ModPagePattern = new(
        @"nexusmods\.com/(?:games/)?derailvalley/mods/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ModPageUrl(int nexusModId) => $"https://www.nexusmods.com/derailvalley/mods/{nexusModId}";

    /// <summary>Returns the Nexus mod id of a Derail Valley mod page URL, or null for anything else.</summary>
    public static int? TryParseModId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = ModPagePattern.Match(url);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    public static bool IsModPageUrl(string? url) => TryParseModId(url) != null;
}
