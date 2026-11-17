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
    /// <summary>DisplayName from the mod's Info.json. Absent in profiles saved before it was added.</summary>
    public string? DisplayName { get; set; }
    public string Version { get; set; } = "";
    public bool IsActive { get; set; }
    /// <summary>GitHub repository URL (e.g. https://github.com/owner/repo). Used to auto-download on import.</summary>
    public string? RepositoryUrl { get; set; }
    /// <summary>Nexus Mods or other homepage URL. Opened in the browser when auto-download is not possible.</summary>
    public string? HomePageUrl { get; set; }
    /// <summary>Author from the mod's Info.json; helps tell Nexus search candidates apart.</summary>
    public string? Author { get; set; }
    /// <summary>Opaque fingerprint of the mod's largest files, used to find it on Nexus when it is missing.</summary>
    public string? Fingerprint { get; set; }
}
