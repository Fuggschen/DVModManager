namespace DVModManager.Models;

public class ModGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Group";
    /// <summary>Ordered list of mod IDs that belong to this group.</summary>
    public List<string> ModIds { get; set; } = [];
}
