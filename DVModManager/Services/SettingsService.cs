using System.Text;
using System.Text.Json;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsService> _logger;
    private readonly string _settingsFilePath;

    // Saves are fired from many places without awaiting each other; without this
    // two of them could write the temp file at the same time
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    // Set when a settings file exists but could neither be read nor backed up.
    // Saving would then replace the user's settings with defaults for good.
    private bool _saveBlocked;

    /// <param name="settingsFilePath">
    /// Where settings are read from and written to. Defaults to
    /// <see cref="ManagerStorage.SettingsFilePath"/>; tests pass a temp path so they
    /// never touch the real settings file.
    /// </param>
    public SettingsService(ILogger<SettingsService> logger, string? settingsFilePath = null)
    {
        _logger = logger;
        _settingsFilePath = settingsFilePath ?? ManagerStorage.SettingsFilePath;
    }

    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync()
    {
        _saveBlocked = false;
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                Settings = new AppSettings();
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Migrate legacy CollapsedGroupIds to per-panel sets
            if (Settings.CollapsedGroupIds is { Count: > 0 })
            {
                if (Settings.CollapsedGroupIdsAvailable.Count == 0)
                    Settings.CollapsedGroupIdsAvailable = new HashSet<string>(Settings.CollapsedGroupIds);
                if (Settings.CollapsedGroupIdsActive.Count == 0)
                    Settings.CollapsedGroupIdsActive = new HashSet<string>(Settings.CollapsedGroupIds);
                Settings.CollapsedGroupIds = null; // clear legacy field
            }
        }
        catch (Exception ex)
        {
            // Don't hide I/O or JSON failures — a corrupt settings file should be visible (H13)
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", _settingsFilePath);
            Settings = new AppSettings();
            BackUpUnreadableSettings();
        }
    }

    /// <summary>
    /// Keeps a copy of a settings file that failed to load, because the next
    /// save overwrites it with defaults. If even the copy fails (e.g. the file is
    /// locked), saving is blocked for this session instead.
    /// </summary>
    private void BackUpUnreadableSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath)) return;

            var backupPath = $"{_settingsFilePath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(_settingsFilePath, backupPath, overwrite: true);
            _logger.LogWarning("Backed up unreadable settings file to {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            _saveBlocked = true;
            _logger.LogError(ex,
                "Could not back up unreadable settings file {Path}; settings will not be saved this session",
                _settingsFilePath);
        }
    }

    public async Task SaveAsync()
    {
        if (_saveBlocked)
        {
            _logger.LogWarning("Skipping settings save: {Path} could not be loaded or backed up", _settingsFilePath);
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            var json = JsonSerializer.Serialize(Settings, JsonOptions);

            // Write to a temp file and swap it in, so a crash or power loss mid-write
            // leaves the previous settings intact instead of a truncated file
            var tempPath = _settingsFilePath + ".tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 4096, useAsync: true))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
