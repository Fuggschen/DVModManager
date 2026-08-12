using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DVModProfiles.Profiles;

// Read-only view of the mod manager's profiles it has persisted and the one it last applied.
public static class ManagerProfiles
{
    private const string MANAGER_DIR = "DVModManager";
    private const string SETTINGS_FILE = "settings.json";
    private const string PROFILES_DIR = "profiles";

    // The manager offers this in its own dropdown whether or not a file exists for it, so we do too
    private const string DEFAULT_PROFILE = "Default";

    // Don't spam load from disk
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(5);

    private static string? configDir;
    private static string? storageRoot;
    private static string? profilesDir;
    private static bool loaded;
    private static DateTime lastChecked;

    private static DateTime settingsStamp;
    private static DateTime profilesStamp;

    private static List<string> names = [];
    private static string? activeProfileName;

    // Names of every profile the manager has persisted, in the order it lists them
    public static List<string> ListNames()
    {
        EnsureFresh();
        return [.. names];
    }

    // The profile the manager applied last or null when the manager's storage can't be
    // found or hasn't recorded one.
    public static string? ActiveProfileName
    {
        get
        {
            EnsureFresh();
            return activeProfileName;
        }
    }

    // Directory the manager keeps settings.json in or null if it isn't installed for this user.
    public static string? ConfigDir
    {
        get
        {
            EnsureFresh();
            return configDir;
        }
    }

    // Directory the manager keeps its profile files in, or null if it isn't installed for this
    // user.
    public static string? ProfilesDir
    {
        get
        {
            EnsureFresh();
            return profilesDir;
        }
    }

    // Root the manager keeps its data under. Null if the manager isn't installed
    // or pointed somewhere we can't resolve such as a relative path.
    public static string? StorageRoot
    {
        get
        {
            EnsureFresh();
            return storageRoot;
        }
    }

    // Drops the cached view, for when the profile files have been changed behind it
    public static void Invalidate() => loaded = false;

    // Re-reads the manager's files when they've changed since the last look.
    private static void EnsureFresh()
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            if (loaded && now - lastChecked < RecheckInterval)
            {
                return;
            }

            lastChecked = now;

            if (loaded && configDir != null && !HasChanged())
            {
                return;
            }

            Load();
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to read the mod manager's profiles", ex);
        }
        finally
        {
            loaded = true;
        }
    }

    private static bool HasChanged() =>
        StampOf(configDir == null ? null : Path.Combine(configDir, SETTINGS_FILE)) != settingsStamp
        || StampOf(profilesDir) != profilesStamp;

    private static void Load()
    {
        names = [];
        activeProfileName = null;
        storageRoot = null;
        profilesDir = null;
        settingsStamp = DateTime.MinValue;
        profilesStamp = DateTime.MinValue;

        string? dir = ResolveConfigDir();
        if (dir == null)
        {
            return;
        }

        string root = dir;
        string settingsPath = Path.Combine(dir, SETTINGS_FILE);

        if (File.Exists(settingsPath))
        {
            settingsStamp = File.GetLastWriteTimeUtc(settingsPath);

            var settings = JObject.Parse(File.ReadAllText(settingsPath));
            activeProfileName = ValueOf(settings, "ActiveProfileName");

            string? storagePath = ValueOf(settings, "StoragePath");
            if (!string.IsNullOrEmpty(storagePath))
            {
                string local = ToLocalPath(storagePath!);

                if (!Path.IsPathRooted(local))
                {
                    Main.Logger.Warning($"The mod manager's storage path '{storagePath}' isn't absolute; " +
                                        "can't tell where its profiles are from in here");
                    return;
                }

                root = local;
            }
        }

        storageRoot = root;
        profilesDir = Path.Combine(root, PROFILES_DIR);
        profilesStamp = StampOf(profilesDir);
        names = ReadNames(profilesDir);
    }

    private static List<string> ReadNames(string dir)
    {
        var found = new List<string>();

        if (Directory.Exists(dir))
        {
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string? name = ValueOf(JObject.Parse(File.ReadAllText(file)), "Name");
                    found.Add(string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(file) : name!);
                }
                catch (Exception ex)
                {
                    Main.Logger.LogException($"Skipping unreadable mod manager profile '{file}'", ex);
                }
            }
        }

        List<string> sorted = [.. found.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        if (!sorted.Contains(DEFAULT_PROFILE, StringComparer.Ordinal))
        {
            sorted.Insert(0, DEFAULT_PROFILE);
        }

        return sorted;
    }

    private static string? ResolveConfigDir()
    {
        string? found = Candidates().FirstOrDefault(Directory.Exists);
        if (found != configDir)
        {
            Main.Logger.Log(found == null
                ? "Mod manager storage not found; no profiles are available to associate saves with"
                : $"Reading mod manager profiles from '{found}'");
            configDir = found;
        }

        return configDir;
    }

    private static IEnumerable<string> Candidates()
    {
        // Native Windows, or a Wine bottle the manager runs in too
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, MANAGER_DIR);
        }

        // Under Proton our %APPDATA% is inside the prefix, while the manager is a Linux build
        // writing to ~/.config. Wine maps the Linux root to Z:, so those paths are still reachable.
        foreach (string dir in LinuxConfigDirs())
        {
            yield return ToLocalPath(dir);
        }
    }

    private static IEnumerable<string> LinuxConfigDirs()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (IsLinuxPath(xdg))
        {
            yield return $"{xdg}/{MANAGER_DIR}";
        }

        string? home = Environment.GetEnvironmentVariable("HOME");
        if (IsLinuxPath(home))
        {
            yield return $"{home}/.config/{MANAGER_DIR}";
        }

        // Neither is guaranteed to survive into the prefix's environment but Steam's own install path
        // is, so walk up out of it looking for the home directory it sits under.
        for (string? dir = Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH");
             IsLinuxPath(dir);
             dir = LinuxParent(dir!))
        {
            yield return $"{dir}/.config/{MANAGER_DIR}";
        }
    }

    private static bool IsLinuxPath(string? path) => path is { Length: > 1 } && path[0] == '/';

    private static string? LinuxParent(string path)
    {
        int slash = path.TrimEnd('/').LastIndexOf('/');
        return slash > 0 ? path.Substring(0, slash) : null;
    }

    // Maps an absolute Linux path onto the Z: drive Wine mounts the Linux root at. Paths that are
    // already Windows-shaped pass through.
    private static string ToLocalPath(string path) =>
        IsLinuxPath(path) ? "Z:" + path.Replace('/', Path.DirectorySeparatorChar) : path;

    private static DateTime StampOf(string? path)
    {
        if (path == null)
        {
            return DateTime.MinValue;
        }

        if (File.Exists(path))
        {
            return File.GetLastWriteTimeUtc(path);
        }

        // A directory's stamp moves when profiles are added or removed, which is the only way the
        // manager changes a profile's name — it writes each one to a file named after it.
        return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    private static string? ValueOf(JObject obj, string name) =>
        obj.Property(name, StringComparison.OrdinalIgnoreCase)?.Value.Value<string>();
}
