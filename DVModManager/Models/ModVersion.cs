namespace DVModManager.Models;

public class ModVersion
{
    public string ModId { get; set; } = "";
    public string Version { get; set; } = "";

    /// <summary>Path to the .zip archive in the version cache.</summary>
    public string ArchivePath { get; set; } = "";

    /// <summary>"nexus", "github", or "manual"</summary>
    public string Source { get; set; } = "manual";

    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    public long ArchiveSizeBytes { get; set; }
}
