namespace DVModManager.Models;

/// <summary>
/// Where the manager keeps what it persists, and how it names those files.
/// </summary>
/// <remarks>
/// This file is compiled into the DVModProfiles mod as well as the manager, so
/// anything added here has to stay compilable under net48.
/// </remarks>
public static class ManagerStorage
{
    public const string DirectoryName = "DVModManager";

    public const string SettingsFileName = "settings.json";

    public const string ProfilesDirectoryName = "profiles";

    /// <summary>The profile the manager offers whether or not a file exists for it.</summary>
    public const string DefaultProfileName = "Default";

    /// <summary>
    /// Holds <see cref="SettingsFileName"/>. It does not move with
    /// <see cref="PublicSettings.StoragePath"/>, which is why anything looking for the
    /// manager's data has to start here and follow that path afterwards.
    /// </summary>
    /// <remarks>
    /// Single source of truth for the manager's app-data directory (settings, logs).
    /// On Linux this respects <c>XDG_CONFIG_HOME</c> when it is a valid absolute path,
    /// otherwise falling back to <c>~/.config</c>. Everything that needs this location
    /// must resolve it through here so the paths can never diverge (H8).
    /// </remarks>
    public static string ConfigDirectory
    {
        get
        {
            if (IsLinuxLike)
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                var configBase = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)
                    ? xdg
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(configBase, DirectoryName);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DirectoryName);
        }
    }

    /// <summary>True on Linux (incl. Mono's platform ids); false on Windows and macOS.</summary>
    private static bool IsLinuxLike
    {
        get
        {
            var platform = (int)Environment.OSVersion.Platform;
            return platform == 4 || platform == 128; // PlatformID.Unix / Mono Unix
        }
    }

    public static string SettingsFilePath => Path.Combine(ConfigDirectory, SettingsFileName);

    /// <summary>The file a profile of this name is saved to, within a profiles directory.</summary>
    public static string ProfileFileName(string profileName) =>
        string.Concat(profileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json";
}
