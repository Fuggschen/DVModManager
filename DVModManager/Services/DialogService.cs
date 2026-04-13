using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DVModManager.Services;

public class DialogService : IDialogService
{
    private Window? _owner;

    public void SetOwner(Window owner) => _owner = owner;

    public async Task<string?> PickFolderAsync(string title)
    {
        if (_owner == null) return null;
        var provider = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (provider == null) return null;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> OpenFileAsync(string title, string filterName, string[] extensions)
    {
        if (_owner == null) return null;
        var provider = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (provider == null) return null;

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(filterName)
                {
                    Patterns = extensions.Select(e => e.StartsWith("*.") ? e : "*." + e.TrimStart('.')).ToList()
                }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(string title, string filterName, string[] extensions, string defaultName)
    {
        if (_owner == null) return null;
        var provider = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (provider == null) return null;

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultName,
            FileTypeChoices =
            [
                new FilePickerFileType(filterName)
                {
                    Patterns = extensions.Select(e => e.StartsWith("*.") ? e : "*." + e.TrimStart('.')).ToList()
                }
            ]
        });

        return file?.Path.LocalPath;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        if (_owner == null) return;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = BuildMessageContent(message, null)
        };

        await dialog.ShowDialog(_owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_owner == null) return false;

        bool result = false;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = BuildMessageContent(message, confirmed => { result = confirmed; })
        };

        // We'll close the dialog from the button callbacks via a TaskCompletionSource
        var tcs = new TaskCompletionSource<bool>();
        dialog.Content = BuildConfirmContent(message, answer =>
        {
            result = answer;
            dialog.Close();
            tcs.TrySetResult(answer);
        });

        await dialog.ShowDialog(_owner);
        return result;
    }

    private static Avalonia.Controls.Control BuildMessageContent(string message, Action<bool>? closeCallback)
    {
        var btn = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        btn.Click += (_, _) => closeCallback?.Invoke(true);

        return new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                btn
            }
        };
    }

    private static Avalonia.Controls.Control BuildConfirmContent(string message, Action<bool> callback)
    {
        var okBtn = new Button { Content = "Yes", Width = 80 };
        var cancelBtn = new Button { Content = "No", Width = 80 };

        okBtn.Click += (_, _) => callback(true);
        cancelBtn.Click += (_, _) => callback(false);

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Children = { okBtn, cancelBtn }
        };

        return new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
    }
}
