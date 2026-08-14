namespace DVModManager.Models;

public class ModUpdateInfo
{
    public string ModId { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";

    /// <summary>"nexus" or "github"</summary>
    public string Source { get; set; } = "";

    public string? DownloadUrl { get; set; }
    public string? ChangelogUrl { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? ReleaseNotes { get; set; }
}
