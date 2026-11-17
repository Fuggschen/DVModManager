using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DVModManager.Helpers;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

public enum ModLinksFilter
{
    NeedsAttention,
    Linked,
    All
}

/// <summary>
/// Bulk editor for the Nexus links of all mods. Edits only land in the link store
/// on Save, so an update that rewrites Info.json keeps them.
/// </summary>
public partial class ModLinksViewModel : ViewModelBase
{
    private const int ParallelSearches = 3;

    private readonly INexusModFinder _finder;
    private readonly IModLinkStore _linkStore;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;
    private readonly List<ModLinkRowViewModel> _rows = [];
    private CancellationTokenSource? _searchCts;
    private bool _discardArmed;

    public ModLinksViewModel(INexusModFinder finder, IModLinkStore linkStore,
        ISettingsService settings, ILocalizationService loc)
    {
        _finder = finder;
        _linkStore = linkStore;
        _settings = settings;
        _loc = loc;
    }

    /// <summary>The rows the current filter shows.</summary>
    public ObservableCollection<ModLinkRowViewModel> Rows { get; } = [];

    /// <summary>True after Save wrote the link store; the caller then rescans.</summary>
    public bool Saved { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchMissingCommand))]
    private bool _isSearching;

    [ObservableProperty] private string _progressText = "";

    [ObservableProperty] private string _noticeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAttentionFilter), nameof(IsLinkedFilter), nameof(IsAllFilter))]
    private ModLinksFilter _filter;

    // Two-way targets for the filter toggles. Clicking the active tab would uncheck it,
    // so every set re-raises the three flags and the toggles always mirror Filter.
    public bool IsAttentionFilter { get => Filter == ModLinksFilter.NeedsAttention; set => SetFilter(ModLinksFilter.NeedsAttention, value); }
    public bool IsLinkedFilter { get => Filter == ModLinksFilter.Linked; set => SetFilter(ModLinksFilter.Linked, value); }
    public bool IsAllFilter { get => Filter == ModLinksFilter.All; set => SetFilter(ModLinksFilter.All, value); }

    private void SetFilter(ModLinksFilter filter, bool on)
    {
        if (on) Filter = filter;
        OnPropertyChanged(nameof(IsAttentionFilter));
        OnPropertyChanged(nameof(IsLinkedFilter));
        OnPropertyChanged(nameof(IsAllFilter));
    }

    public string AttentionLabel => _loc.GetString("mod_links.filter_attention", _rows.Count(r => !r.HasLink));
    public string LinkedLabel => _loc.GetString("mod_links.filter_linked", _rows.Count(r => r.HasLink));
    public string AllLabel => _loc.GetString("mod_links.filter_all", _rows.Count);

    public bool IsEmpty => Rows.Count == 0;

    public void Load(IEnumerable<ModInfo> mods)
    {
        foreach (var row in _rows) row.PropertyChanged -= OnRowChanged;
        _rows.Clear();
        foreach (var mod in mods.OrderBy(m => m.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var row = new ModLinkRowViewModel(mod, _loc);
            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }
        Filter = _rows.Any(r => !r.HasLink) ? ModLinksFilter.NeedsAttention : ModLinksFilter.All;
        ApplyFilter();
    }

    partial void OnFilterChanged(ModLinksFilter value) => ApplyFilter();

    /// <summary>
    /// Rebuilds the visible list. Runs on filter changes and after a search only, so
    /// a row the user just fixed does not vanish from under the pointer.
    /// </summary>
    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in _rows.Where(r => Filter switch
                 {
                     ModLinksFilter.NeedsAttention => !r.HasLink,
                     ModLinksFilter.Linked => r.HasLink,
                     _ => true
                 }))
            Rows.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModLinkRowViewModel.HasLink))
        {
            OnPropertyChanged(nameof(AttentionLabel));
            OnPropertyChanged(nameof(LinkedLabel));
        }
        _discardArmed = false;
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchMissingAsync()
    {
        var targets = _rows.Where(r => !r.HasLink && string.IsNullOrWhiteSpace(r.NexusUrl)).ToList();
        if (targets.Count == 0)
        {
            ProgressText = _loc.GetString("mod_links.nothing_missing");
            return;
        }

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        IsSearching = true;
        var done = 0;
        ProgressText = _loc.GetString("mod_links.progress", done, targets.Count);

        using var gate = new SemaphoreSlim(ParallelSearches);
        try
        {
            await Task.WhenAll(targets.Select(async row =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    row.SearchState = NexusSearchState.Searching;
                    var key = await Task.Run(() => ModSearchKey.FromMod(row.Mod), ct);
                    row.ApplyMatch(await _finder.FindAsync(key, ct));
                }
                finally
                {
                    gate.Release();
                }
                ProgressText = _loc.GetString("mod_links.progress", ++done, targets.Count);
            }));
            ProgressText = _loc.GetString("mod_links.search_done",
                targets.Count(r => r.SearchState == NexusSearchState.Found),
                targets.Count(r => r.SearchState == NexusSearchState.Uncertain),
                targets.Count);
        }
        catch (OperationCanceledException)
        {
            foreach (var row in targets.Where(r => r.IsSearching))
                row.SearchState = NexusSearchState.Idle;
        }
        finally
        {
            IsSearching = false;
            _searchCts.Dispose();
            _searchCts = null;
            ApplyFilter();
        }
    }

    private bool CanSearch() => !IsSearching;

    [RelayCommand]
    private async Task SaveAsync(Avalonia.Controls.Window dialog)
    {
        if (_rows.Any(r => !r.IsNexusValid))
        {
            NoticeText = _loc.GetString("mod_links.invalid_links");
            return;
        }

        CancelSearch();
        await _linkStore.LoadAsync(_settings.Settings.StoragePath);
        foreach (var row in _rows.Where(r => r.IsDirty))
        {
            // Only a link that differs from Info.json is stored; a cleared one falls back to Info.json.
            // A stored repository link is kept as it is: the editor does not touch it.
            var nexus = row.NexusUrl.Trim();
            var declared = NexusLinks.IsModPageUrl(row.Mod.HomePage) ? row.Mod.HomePage!.Trim() : "";
            _linkStore.Set(row.ModId, nexus == declared ? null : nexus, _linkStore.Get(row.ModId)?.RepositoryUrl);
        }
        await _linkStore.SaveAsync();
        Saved = true;
        dialog.Close();
    }

    /// <summary>Closes; with unsaved edits the first click only warns and the second discards.</summary>
    [RelayCommand]
    private void Cancel(Avalonia.Controls.Window dialog)
    {
        if (_rows.Any(r => r.IsDirty) && !_discardArmed)
        {
            _discardArmed = true;
            NoticeText = _loc.GetString("mod_links.discard_confirm");
            return;
        }
        dialog.Close();
    }

    /// <summary>Stops a running search; called when the dialog closes in any way.</summary>
    public void CancelSearch() => _searchCts?.Cancel();
}
