namespace DVModManager.Models;

public class AppSettings
{
    public string? GamePath { get; set; }

    /// <summary>Root storage path for downloads, version archives, logs, profiles.</summary>
    public string StoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DVModManager");

    public string? NexusApiKey { get; set; }
    public string? GitHubToken { get; set; }

    /// <summary>"Dark" or "Light"</summary>
    public string ThemeVariant { get; set; } = "Dark";

    public bool AutoCheckUpdatesOnStartup { get; set; } = true;

    public bool BackupBeforeChanges { get; set; } = true;

    /// <summary>Maximum combined size of all version archives before pruning. Default 5 GB.</summary>
    public long MaxCacheSizeBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    public string ActiveProfileName { get; set; } = "Default";

    /// <summary>User-defined visual groups for both mod panels.</summary>
    public List<ModGroup> ModGroups { get; set; } = [];

    /// <summary>Group IDs that are currently collapsed (persisted).</summary>
    public HashSet<string> CollapsedGroupIds { get; set; } = [];

    // --- Derived paths ---
    public string ProfilesPath => Path.Combine(StoragePath, "profiles");
    public string VersionsPath => Path.Combine(StoragePath, "versions");
    public string DownloadsPath => Path.Combine(StoragePath, "downloads");
    public string LogsPath => Path.Combine(StoragePath, "logs");
}
