namespace DVModManager.Models;

/// <summary>
/// A profile as it is stored on disk.
/// </summary>
/// <remarks>
/// This file is compiled into the DVModProfiles mod as well as the manager, so
/// anything added here has to stay compilable under net48.
/// </remarks>
public class ModProfile
{
    public string Name { get; set; } = ManagerStorage.DefaultProfileName;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    public List<ProfileModEntry> Mods { get; set; } = [];
}

public class ProfileModEntry
{
    public string ModId { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsActive { get; set; }
    /// <summary>GitHub repository URL (e.g. https://github.com/owner/repo). Used to auto-download on import.</summary>
    public string? RepositoryUrl { get; set; }
    /// <summary>Nexus Mods or other homepage URL. Opened in the browser when auto-download is not possible.</summary>
    public string? HomePageUrl { get; set; }
}
