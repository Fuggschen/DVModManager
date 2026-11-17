using CommunityToolkit.Mvvm.Input;
using DVModManager.Helpers;
using DVModManager.Models;
using DVModManager.Services;

namespace DVModManager.ViewModels;

/// <summary>One row of the candidates list: a Nexus mod the search could not rule out.</summary>
public sealed record NexusCandidateRow(string Name, int Score, string Signals, string Url);

/// <summary>
/// Warning shown when the Nexus search found candidates but none is certain.
/// From the link editor it also lets the user pick one as the mod's link.
/// </summary>
public partial class NexusCandidatesViewModel : ViewModelBase
{
    public NexusCandidatesViewModel(string modName, IReadOnlyList<NexusCandidate> candidates, bool canUse = false)
    {
        var loc = App.Services.GetService(typeof(ILocalizationService)) as ILocalizationService;
        Message = loc?.GetString("nexus_candidates.message", modName)
                  ?? $"The search is not sure which Nexus page belongs to {modName}. Check the candidates below.";
        CanUse = canUse;
        Candidates = candidates.Select(c => new NexusCandidateRow(
            c.Name ?? $"#{c.NexusModId}",
            c.Score,
            string.Join(", ", c.Signals),
            NexusLinks.ModPageUrl(c.NexusModId))).ToList();
    }

    public string Message { get; }

    public IReadOnlyList<NexusCandidateRow> Candidates { get; }

    /// <summary>True when each candidate offers "Use this".</summary>
    public bool CanUse { get; }

    /// <summary>The candidate the user picked with "Use this"; null when the dialog was just closed.</summary>
    public NexusCandidateRow? Chosen { get; private set; }

    /// <summary>Raised when a pick should close the dialog.</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Open(NexusCandidateRow row) => PlatformHelper.Open(row.Url);

    [RelayCommand]
    private void Use(NexusCandidateRow row)
    {
        Chosen = row;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close(Avalonia.Controls.Window dialog) => dialog.Close();
}
