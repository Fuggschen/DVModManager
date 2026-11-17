using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Helpers;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

/// <summary>One mod in the link editor. Shows its Nexus link as text and only turns it into an input on Edit.</summary>
public partial class ModLinkRowViewModel : ViewModelBase
{
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#9399b2"));

    private readonly ILocalizationService? _loc;
    private readonly string _loadedNexus;
    private IReadOnlyList<NexusCandidate> _candidates = [];

    public ModLinkRowViewModel(ModInfo mod, ILocalizationService? loc)
    {
        _loc = loc;
        Mod = mod;
        _loadedNexus = mod.NexusUrl ?? "";
        _nexusUrl = _loadedNexus;
    }

    public ModInfo Mod { get; }

    public string DisplayName => Mod.EffectiveDisplayName;

    public string ModId => Mod.Id;

    public string Kind => Mod.IsPlaceholder
        ? Localize("mod_links.kind_placeholder", "Placeholder")
        : Mod.IsActive ? Localize("modstate.active", "Active") : Localize("modstate.inactive", "Inactive");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNexusValid), nameof(HasLink), nameof(LinkText), nameof(ShowLink),
        nameof(ShowStatus), nameof(ShowReview), nameof(StatusText), nameof(StatusBrush))]
    private string _nexusUrl;

    /// <summary>Nexus title of the link, when a search or review supplied it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkText))]
    private string? _linkName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLink), nameof(ShowStatus))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(ShowReview), nameof(StatusText), nameof(StatusBrush))]
    private NexusSearchState _searchState;

    public bool IsNexusValid => string.IsNullOrWhiteSpace(NexusUrl) || NexusLinks.IsModPageUrl(NexusUrl);

    public bool HasLink => !string.IsNullOrWhiteSpace(NexusUrl) && NexusLinks.IsModPageUrl(NexusUrl);

    public bool IsSearching => SearchState == NexusSearchState.Searching;

    public bool ShowLink => HasLink && !IsEditing;

    public bool ShowStatus => !HasLink && !IsEditing;

    public bool ShowReview => !HasLink && SearchState == NexusSearchState.Uncertain;

    public string LinkText => LinkName ?? Localize("mod_links.link_fallback", "Nexus mod #{0}", NexusLinks.TryParseModId(NexusUrl));

    public string StatusText => SearchState switch
    {
        NexusSearchState.Searching => NexusSearchPresentation.Label(SearchState, _loc),
        NexusSearchState.Uncertain => Localize("mod_links.needs_review", "Needs review"),
        NexusSearchState.NotFound  => Localize("mod_links.not_found", "Not found on Nexus"),
        NexusSearchState.Failed    => NexusSearchPresentation.Label(SearchState, _loc),
        _                          => Localize("mod_links.no_link", "No link")
    };

    public IBrush StatusBrush => SearchState is NexusSearchState.Idle or NexusSearchState.Searching
        ? MutedBrush
        : NexusSearchPresentation.Brush(SearchState);

    public bool IsDirty => !string.Equals(NexusUrl.Trim(), _loadedNexus, StringComparison.Ordinal);

    /// <summary>Shows the result of a search on this row. Uncertain candidates wait for Review.</summary>
    public void ApplyMatch(NexusMatch match)
    {
        _candidates = match.Candidates;
        switch (match.Certainty)
        {
            case NexusMatchCertainty.Sure:
                LinkName = match.Best!.Name;
                NexusUrl = NexusLinks.ModPageUrl(match.Best.NexusModId);
                SearchState = NexusSearchState.Found;
                break;
            case NexusMatchCertainty.Uncertain:
                SearchState = NexusSearchState.Uncertain;
                break;
            case NexusMatchCertainty.NotFound:
                SearchState = NexusSearchState.NotFound;
                break;
            default:
                SearchState = NexusSearchState.Failed;
                break;
        }
    }

    [RelayCommand]
    private void OpenLink()
    {
        if (HasLink) PlatformHelper.Open(NexusUrl.Trim());
    }

    [RelayCommand]
    private void Edit() => IsEditing = true;

    /// <summary>Leaves edit mode; an invalid link keeps the field open so it can be fixed.</summary>
    [RelayCommand]
    private void DoneEditing()
    {
        if (!IsNexusValid) return;
        NexusUrl = NexusUrl.Trim();
        if (NexusUrl != _loadedNexus) LinkName = null;
        IsEditing = false;
    }

    /// <summary>Lists the uncertain candidates; the one the user picks becomes the link.</summary>
    [RelayCommand]
    private async Task ReviewAsync(Avalonia.Controls.Window owner)
    {
        var vm = new NexusCandidatesViewModel(DisplayName, _candidates, canUse: true);
        await new Views.NexusCandidatesDialog { DataContext = vm }.ShowDialog(owner);
        if (vm.Chosen == null) return;

        LinkName = vm.Chosen.Name;
        NexusUrl = vm.Chosen.Url;
        SearchState = NexusSearchState.Found;
    }

    private string Localize(string key, string fallback, params object?[] args)
    {
        var text = _loc?.GetString(key, args);
        return string.IsNullOrEmpty(text) || text == key ? string.Format(fallback, args) : text;
    }
}
