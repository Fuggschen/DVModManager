using System.Text.Json;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class SettingsService : ISettingsService
{
    private static string SettingsFilePath => ManagerStorage.SettingsFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger) => _logger = logger;

    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Settings = new AppSettings();
                return;
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath);
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
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", SettingsFilePath);
            Settings = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsFilePath, json);
    }
}
