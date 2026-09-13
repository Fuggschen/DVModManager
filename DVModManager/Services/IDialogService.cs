using Avalonia.Controls;
using DVModManager.Models;

namespace DVModManager.Services;

public enum ExportOption { Json, Zip, Cancel }

public interface IDialogService
{
    void SetOwner(Window owner);
    Task<string?> PickFolderAsync(string title);
    Task<string?> OpenFileAsync(string title, string filterName, string[] extensions);
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, string filterName, string[] extensions);
    Task<string?> SaveFileAsync(string title, string filterName, string[] extensions, string defaultName);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel);
    /// <summary>Shows a list of failed downloads with "Open on Nexus" and "Cancel" buttons. Returns true if the user chose "Open on Nexus".</summary>
    Task<bool> ShowFailedDownloadsAsync(string title, IReadOnlyList<(string ModId, string? HomePageUrl)> failedMods);
    /// <summary>Shows export format choice: JSON, ZIP, or Cancel.</summary>
    Task<ExportOption> ShowExportOptionsAsync(string title);
    /// <summary>Shows the mod list from a profile with a security warning. Returns true if the user confirmed the import.</summary>
    Task<bool> ShowModpackImportConfirmAsync(string title, ModProfile profile);
}
