namespace DVModManager.Models;

public enum ModState
{
    Active,
    Inactive,
    UpdateAvailable,
    MissingDependency,
    NoMetadata,
    Missing,      // installed in a group but no longer found on disk
    Placeholder   // folder holds only a placeholder.json left by a profile apply
}
