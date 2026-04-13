using Avalonia.Controls;

namespace DVModManager.Services;

public interface IDialogService
{
    void SetOwner(Window owner);
    Task<string?> PickFolderAsync(string title);
    Task<string?> OpenFileAsync(string title, string filterName, string[] extensions);
    Task<string?> SaveFileAsync(string title, string filterName, string[] extensions, string defaultName);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message);
}
