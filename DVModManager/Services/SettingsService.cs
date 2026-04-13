using System.Text.Json;
using DVModManager.Models;

namespace DVModManager.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DVModManager", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        }
        catch
        {
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
