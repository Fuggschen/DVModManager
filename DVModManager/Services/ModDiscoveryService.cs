using System.Text.Json;
using DVModManager.Helpers;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public sealed class ModDiscoveryService : IModDiscoveryService
{
    private readonly ILogger<ModDiscoveryService> _logger;

    private FileSystemWatcher? _activeWatcher;
    private FileSystemWatcher? _inactiveWatcher;

    public ModDiscoveryService(ILogger<ModDiscoveryService> logger) => _logger = logger;

    public event EventHandler? ModsChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public async Task<IReadOnlyList<ModInfo>> ScanAllModsAsync(string gamePath)
    {
        var mods = new List<ModInfo>();

        var activeDir = Path.Combine(gamePath, "Mods");
        var inactiveDir = Path.Combine(gamePath, "Mods.inactive");

        if (Directory.Exists(activeDir))
        {
            foreach (var dir in Directory.GetDirectories(activeDir))
            {
                if (IsInternalDir(dir)) continue;
                var mod = await ParseModInfoAsync(dir, isActive: true);
                if (mod != null) mods.Add(mod);
            }
        }

        if (Directory.Exists(inactiveDir))
        {
            foreach (var dir in Directory.GetDirectories(inactiveDir))
            {
                if (IsInternalDir(dir)) continue;
                var mod = await ParseModInfoAsync(dir, isActive: false);
                if (mod != null) mods.Add(mod);
            }
        }

        return mods;
    }

    /// <summary>
    /// Skips internal working directories used by the installer
    /// (<c>*.staging</c>, <c>*.backup</c>, <c>*.update_*</c>) so leftover
    /// intermediate folders are never surfaced as mods.
    /// </summary>
    private static bool IsInternalDir(string dir)
    {
        var name = Path.GetFileName(dir);
        return name.EndsWith(".staging", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".update_staging", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".update_backup", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ModInfo?> ParseModInfoAsync(string folderPath, bool isActive)
    {
        var infoPath = InfoJsonLocator.Locate(folderPath);

        ModInfo mod;

        if (infoPath == null)
        {
            // Orphan folder — no Info.json
            mod = new ModInfo
            {
                Id = Path.GetFileName(folderPath),
                DisplayName = Path.GetFileName(folderPath),
                HasMetadata = false,
                State = ModState.NoMetadata
            };
        }
        else
        {
            try
            {
                var json = await File.ReadAllTextAsync(infoPath);
                mod = JsonSerializer.Deserialize<ModInfo>(json, JsonOptions) ?? new ModInfo();
            }
            catch (Exception ex)
            {
                // A corrupt Info.json should be visible in the log, not silently hidden (H13)
                _logger.LogWarning(ex, "Failed to parse {Path}; treating folder as metadata-less", infoPath);
                mod = new ModInfo
                {
                    Id = Path.GetFileName(folderPath),
                    HasMetadata = false,
                    State = ModState.NoMetadata
                };
            }
        }

        mod.FolderPath = folderPath;
        mod.IsActive = isActive;
        if (mod.State == ModState.Inactive || mod.State == ModState.Active)
            mod.State = isActive ? ModState.Active : ModState.Inactive;

        return mod;
    }

    public void StartWatching(string gamePath)
    {
        StopWatching();
        TrySetupWatcher(Path.Combine(gamePath, "Mods"), ref _activeWatcher);
        TrySetupWatcher(Path.Combine(gamePath, "Mods.inactive"), ref _inactiveWatcher);
    }

    private void TrySetupWatcher(string path, ref FileSystemWatcher? watcher)
    {
        try
        {
            Directory.CreateDirectory(path);
            watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Created += OnFileSystemChange;
            watcher.Deleted += OnFileSystemChange;
            watcher.Renamed += OnFileSystemChange;
        }
        catch (Exception ex)
        {
            // Watching is optional, but the reason it failed should not be lost (H13)
            _logger.LogWarning(ex, "Could not watch {Path} for changes", path);
        }
    }

    private CancellationTokenSource? _debounceCts;

    private void OnFileSystemChange(object sender, FileSystemEventArgs e)
    {
        // Debounce: cancel any pending fire, schedule a new one after 500ms.
        // This ensures rapid changes (e.g., unzipping) only fire once.
        // Interlocked.Exchange swaps the CTS atomically and lets us dispose the
        // previous one so cancelled timers don't leak registrations (M1).
        var next = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _debounceCts, next);
        prev?.Cancel();
        prev?.Dispose();

        var token = next.Token;
        _ = Task.Delay(500, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                ModsChanged?.Invoke(this, EventArgs.Empty);
        }, TaskScheduler.Default);
    }

    public void StopWatching()
    {
        _activeWatcher?.Dispose();
        _inactiveWatcher?.Dispose();
        _activeWatcher = null;
        _inactiveWatcher = null;
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        StopWatching();
    }
}
