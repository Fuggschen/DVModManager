using Avalonia.Media;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

/// <summary>Text and color of a Nexus search state, shared by the detail panel and the link editor.</summary>
public static class NexusSearchPresentation
{
    private static readonly IBrush FoundBrush = new SolidColorBrush(Color.Parse("#2ecc71"));
    private static readonly IBrush UncertainBrush = new SolidColorBrush(Color.Parse("#f39c12"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#e74c3c"));
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.Parse("#a6adc8"));

    public static string Label(NexusSearchState state, ILocalizationService? loc) => state switch
    {
        NexusSearchState.Searching => Localize(loc, "nexus_search.searching", "Searching…"),
        NexusSearchState.Found     => Localize(loc, "nexus_search.found", "Found!"),
        NexusSearchState.Uncertain => Localize(loc, "nexus_search.uncertain", "Uncertain, check the candidates."),
        NexusSearchState.NotFound  => Localize(loc, "nexus_search.not_found", "Not Found!"),
        NexusSearchState.Failed    => Localize(loc, "nexus_search.failed", "Search failed. Check your connection."),
        _                          => ""
    };

    public static IBrush Brush(NexusSearchState state) => state switch
    {
        NexusSearchState.Found                               => FoundBrush,
        NexusSearchState.Uncertain                           => UncertainBrush,
        NexusSearchState.NotFound or NexusSearchState.Failed => ErrorBrush,
        _                                                    => NeutralBrush
    };

    private static string Localize(ILocalizationService? loc, string key, string fallback)
    {
        var text = loc?.GetString(key);
        return string.IsNullOrEmpty(text) || text == key ? fallback : text;
    }
}
