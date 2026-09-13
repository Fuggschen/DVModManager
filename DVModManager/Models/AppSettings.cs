namespace DVModManager.Models;

public class AppSettings : PublicSettings
{
    public string? GamePath { get; set; }


    /// <summary>"Dark" or "Light"</summary>
    public string ThemeVariant { get; set; } = "Dark";

    /// <summary>Language code: "en", "de", "fr", etc.</summary>
    public string Language { get; set; } = "en";

    public bool AutoCheckUpdatesOnStartup { get; set; } = true;

    public bool EnableVersionArchiving { get; set; } = true;

    /// <summary>Maximum combined size of all version archives before pruning. Default 5 GB.</summary>
    public long MaxCacheSizeBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>User-defined visual groups for both mod panels.</summary>
    public List<ModGroup> ModGroups { get; set; } = [];

    /// <summary>Group IDs that are currently collapsed in the Available panel (persisted).</summary>
    public HashSet<string> CollapsedGroupIdsAvailable { get; set; } = [];

    /// <summary>Group IDs that are currently collapsed in the Active panel (persisted).</summary>
    public HashSet<string> CollapsedGroupIdsActive { get; set; } = [];

    /// <summary>Legacy field — migrated to per-panel sets on load.</summary>
    public HashSet<string>? CollapsedGroupIds { get; set; }

    // --- Derived paths ---
    public string VersionsPath => Path.Combine(StoragePath, "versions");
    public string DownloadsPath => Path.Combine(StoragePath, "downloads");
    public string LogsPath => Path.Combine(StoragePath, "logs");
}
