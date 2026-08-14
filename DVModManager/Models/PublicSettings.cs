namespace DVModManager.Models;

/// <summary>
/// The part of <see cref="ManagerStorage.SettingsFileName"/> that anything outside the manager
/// depends on. Settings that shouldn't be exposed externally such as API keys should be
/// on <see cref="AppSettings"/>.
/// </summary>
/// <remarks>
/// This file is compiled into the DVModProfiles mod as well as the manager, so
/// anything added here has to stay compilable under net48.
/// </remarks>
public class PublicSettings
{
    /// <summary>Root storage path for downloads, version archives, logs, profiles.</summary>
    public string StoragePath { get; set; } = ManagerStorage.ConfigDirectory;

    /// <summary>The profile the manager last applied to the game's mod folder.</summary>
    public string ActiveProfileName { get; set; } = ManagerStorage.DefaultProfileName;

    /// <summary>The path where profile data is written.</summary>
    public string ProfilesPath => Path.Combine(StoragePath, ManagerStorage.ProfilesDirectoryName);
}
