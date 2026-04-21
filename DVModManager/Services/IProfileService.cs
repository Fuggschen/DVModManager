using DVModManager.Models;

namespace DVModManager.Services;

public interface IProfileService
{
    Task<IReadOnlyList<ModProfile>> GetProfilesAsync(string profilesPath);
    Task<ModProfile?> GetProfileAsync(string name, string profilesPath);
    Task SaveProfileAsync(ModProfile profile, string profilesPath);
    Task DeleteProfileAsync(string name, string profilesPath);
    Task<string> ExportProfileAsync(ModProfile profile, string destinationFilePath);
    Task<ModProfile> ImportProfileAsync(string sourceFilePath);
    Task<string> ExportProfileAsZipAsync(ModProfile profile, string gamePath, string zipPath);
    /// <summary>Returns a profile name that doesn't collide with existing files, appending (2), (3) etc. as needed.</summary>
    Task<string> GetUniqueProfileNameAsync(string name, string profilesPath);
    ProfileDiff ComputeDiff(ModProfile profile, IReadOnlyList<ModInfo> currentMods);
}

public record ProfileDiff(
    IReadOnlyList<string> ToActivate,
    IReadOnlyList<string> ToDeactivate,
    IReadOnlyList<(string ModId, string FromVersion, string ToVersion)> ToRollback,
    IReadOnlyList<ProfileModEntry> ToDownload);
