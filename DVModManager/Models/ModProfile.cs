namespace DVModManager.Models;

public class ModProfile
{
    public string Name { get; set; } = "Default";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    public List<ProfileModEntry> Mods { get; set; } = [];
}

public class ProfileModEntry
{
    public string ModId { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsActive { get; set; }
}
