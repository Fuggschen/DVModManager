using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DVModManager.Models;

namespace DVModManager.Services;

public class DialogService : IDialogService
{
    private Window? _owner;
    private readonly ILocalizationService _loc;

    public DialogService(ILocalizationService loc)
    {
        _loc = loc;
    }

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

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, string filterName, string[] extensions)
    {
        if (_owner == null) return [];
        var provider = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (provider == null) return [];

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(filterName)
                {
                    Patterns = extensions.Select(e => e.StartsWith("*.") ? e : "*." + e.TrimStart('.')).ToList()
                }
            ]
        });

        return files.Select(f => f.Path.LocalPath).ToList();
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
            MaxHeight = 600,
            SizeToContent = SizeToContent.Height,
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
            MaxHeight = 600,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
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
                new ScrollViewer
                {
                    MaxHeight = 280,
                    Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                },
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
                new ScrollViewer
                {
                    MaxHeight = 280,
                    Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                },
                buttons
            }
        };
    }

    public async Task<ExportOption> ShowExportOptionsAsync(string title)
    {
        if (_owner == null) return ExportOption.Cancel;

        var tcs = new TaskCompletionSource<ExportOption>();

        var jsonBtn = new Button { Content = _loc.GetString("export.button.json") };
        var zipBtn = new Button { Content = _loc.GetString("export.button.zip") };
        var cancelBtn = new Button { Content = _loc.GetString("settings.button.cancel") };

        jsonBtn.Click += (_, _) => tcs.TrySetResult(ExportOption.Json);
        zipBtn.Click += (_, _) => tcs.TrySetResult(ExportOption.Zip);
        cancelBtn.Click += (_, _) => tcs.TrySetResult(ExportOption.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Children = { jsonBtn, zipBtn, cancelBtn }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children = { buttons }
            }
        };

        dialog.Closing += (_, _) => tcs.TrySetResult(ExportOption.Cancel);
        _ = dialog.ShowDialog(_owner);
        var result = await tcs.Task;
        if (dialog.IsVisible) dialog.Close();
        return result;
    }

    public async Task<bool> ShowModpackImportConfirmAsync(string title, ModProfile profile)
    {
        if (_owner == null) return false;

        var tcs = new TaskCompletionSource<bool>();

        var sb = new System.Text.StringBuilder();
        foreach (var mod in profile.Mods)
            sb.AppendLine($"  \u2022 {mod.ModId}  v{mod.Version}");

        var preamble = new TextBlock
        {
            Text = _loc.GetString("import.modpack.preamble"),
            TextWrapping = TextWrapping.Wrap
        };
        var modList = new TextBlock
        {
            Text = sb.ToString().TrimEnd(),
            FontFamily = new FontFamily("Consolas,Courier New,monospace"),
            TextWrapping = TextWrapping.Wrap
        };
        var warning = new TextBlock
        {
            Text = _loc.GetString("import.modpack.security_warning"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.OrangeRed
        };

        var importBtn = new Button { Content = _loc.GetString("import.button.confirm") };
        var cancelBtn = new Button { Content = _loc.GetString("settings.button.cancel") };

        importBtn.Click += (_, _) => tcs.TrySetResult(true);
        cancelBtn.Click += (_, _) => tcs.TrySetResult(false);

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Children = { importBtn, cancelBtn }
        };

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                preamble,
                new ScrollViewer { MaxHeight = 200, Content = modList },
                warning,
                buttons
            }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 460,
            MaxHeight = 600,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };

        dialog.Closing += (_, _) => tcs.TrySetResult(false);
        _ = dialog.ShowDialog(_owner);
        var result = await tcs.Task;
        if (dialog.IsVisible) dialog.Close();
        return result;
    }

    public async Task<bool> ShowFailedDownloadsAsync(string title, IReadOnlyList<(string ModId, string? HomePageUrl)> failedMods)
    {
        if (_owner == null) return false;

        bool openNexus = false;
        var tcs = new TaskCompletionSource<bool>();

        var nexusBtn = new Button { Content = "Open on Nexus", Width = 130 };
        var cancelBtn = new Button { Content = "Cancel", Width = 80 };

        nexusBtn.Click += (_, _) => { openNexus = true; tcs.TrySetResult(true); };
        cancelBtn.Click += (_, _) => { openNexus = false; tcs.TrySetResult(false); };

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Children = { nexusBtn, cancelBtn }
        };

        int nexusCount = failedMods.Count(m => !string.IsNullOrEmpty(m.HomePageUrl));
        nexusBtn.Content = nexusCount > 0 ? $"Open on Nexus ({nexusCount})" : "Open on Nexus";
        nexusBtn.Width = double.NaN; // auto-width to fit content

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("The following mods could not be downloaded from GitHub:");
        foreach (var (modId, homePageUrl) in failedMods)
        {
            bool hasLink = !string.IsNullOrEmpty(homePageUrl);
            lines.AppendLine(hasLink ? $"  \u2022 {modId}" : $"  \u2022 {modId}  (no link available)");
        }
        if (nexusCount > 0)
            lines.AppendLine($"\nClick \"Open on Nexus ({nexusCount})\" to open their pages in the browser.");
        else
            nexusBtn.IsEnabled = false;

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new ScrollViewer
                {
                    MaxHeight = 280,
                    Content = new TextBlock { Text = lines.ToString().TrimEnd(), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                },
                buttons
            }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 460,
            MaxHeight = 600,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };

        dialog.Closing += (_, _) => tcs.TrySetResult(false);

        _ = dialog.ShowDialog(_owner);
        openNexus = await tcs.Task;
        if (dialog.IsVisible) dialog.Close();
        return openNexus;
    }
}
