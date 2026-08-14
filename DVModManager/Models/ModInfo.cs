using System.Text.Json.Serialization;

namespace DVModManager.Models;

/// <summary>
/// Mirrors Unity Mod Manager's Info.json schema.
/// Runtime-only fields are tagged [JsonIgnore].
/// </summary>
public class ModInfo
{
    // --- Info.json fields ---
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string? ManagerVersion { get; set; }
    public string? GameVersion { get; set; }
    public string[] Requirements { get; set; } = [];
    public string[] LoadAfter { get; set; } = [];
    public string? AssemblyName { get; set; }
    public string? EntryMethod { get; set; }
    public string? HomePage { get; set; }
    public string? Repository { get; set; }
    public string? Description { get; set; }

    // --- Runtime-only (not persisted) ---
    [JsonIgnore] public string FolderPath { get; set; } = "";
    [JsonIgnore] public bool IsActive { get; set; }
    [JsonIgnore] public ModState State { get; set; } = ModState.Inactive;
    [JsonIgnore] public ModUpdateInfo? PendingUpdate { get; set; }
    [JsonIgnore] public bool HasMetadata { get; set; } = true;

    /// <summary>Effective display name: falls back to Id if DisplayName is blank.</summary>
    [JsonIgnore] public string EffectiveDisplayName =>
        string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}
