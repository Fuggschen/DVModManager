namespace DVModManager.Models;

/// <summary>Install state of a profile mod on this machine.</summary>
public enum ProfileModStatus
{
    Active,
    Inactive,
    NotInstalled
}

/// <summary>One mod in the import preview, wrapping its profile entry.</summary>
public class ProfileImportPreviewItem
{
    public ProfileModEntry Entry { get; set; } = new();

    /// <summary>Installed mod's name, or the ModId when it isn't installed.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Install state on this machine.</summary>
    public ProfileModStatus Status { get; set; }

    /// <summary>True when the entry has a RepositoryUrl or HomePageUrl.</summary>
    public bool CanAutoDownload { get; set; }

    public string ModId => Entry.ModId;
    public string Version => Entry.Version;
}
