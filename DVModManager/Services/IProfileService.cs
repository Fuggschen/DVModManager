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

    /// <summary>
    /// Returns a list describing what operations would occur if the profile were applied.
    /// </summary>
    ProfileDiff ComputeDiff(ModProfile profile, IReadOnlyList<ModInfo> currentMods);
}

public record ProfileDiff(
    IReadOnlyList<string> ToActivate,
    IReadOnlyList<string> ToDeactivate,
    IReadOnlyList<(string ModId, string FromVersion, string ToVersion)> ToRollback,
    IReadOnlyList<ProfileModEntry> ToDownload);
