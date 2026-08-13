using System;
using System.IO;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace DVModProfiles.Profiles;

// Loads/saves ProfileSettings objects as JSON through ProfileStorage (local files or Steam Cloud),
// and captures the current configuration of all installed mods for a profile.
public static class SettingsStore
{
    private const string SETTINGS_FILE = "Settings.xml";
    private const string PROFILE_PREFIX = "profiles/";

    private static string KeyFor(string profileName) => PROFILE_PREFIX + Sanitize(profileName) + ".json";

    public static bool Exists(string profileName) =>
        ProfileStorage.Backend.TryRead(KeyFor(profileName), out _);

    public static ProfileSettings? Load(string profileName)
    {
        string key = KeyFor(profileName);

        try
        {
            if (!ProfileStorage.Backend.TryRead(key, out string json))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<ProfileSettings>(json);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to load mod settings for profile '{profileName}'", ex);
            return null;
        }
    }

    public static void Save(ProfileSettings settings) =>
        ProfileStorage.Backend.Write(KeyFor(settings.Name), JsonConvert.SerializeObject(settings, Formatting.Indented));

    public static void Delete(string profileName) =>
        ProfileStorage.Backend.Delete(KeyFor(profileName));

    // Snapshots the live settings of every installed mod (excluding this one) against a profile
    // name and persists them. First asks each mod to flush its settings via its OnSaveGUI hook.
    public static ProfileSettings Capture(string profileName)
    {
        var settings = new ProfileSettings { Name = profileName };
        string selfId = Main.ModEntry.Info.Id;

        foreach (UnityModManager.ModEntry mod in UnityModManager.modEntries)
        {
            if (mod.Info.Id != selfId)
            {
                CaptureMod(settings, mod);
            }
        }

        Save(settings);
        Main.Logger.Log($"Captured {settings.Settings.Count} mod settings for profile '{profileName}'");
        return settings;
    }

    // Records a single mod's current settings
    private static void CaptureMod(ProfileSettings settings, UnityModManager.ModEntry mod)
    {
        try
        {
            // Ask the mod to write out its current settings before we read them.
            mod.OnSaveGUI?.Invoke(mod);

            string settingsPath = Path.Combine(mod.Path, SETTINGS_FILE);
            if (File.Exists(settingsPath))
            {
                settings.Settings[mod.Info.Id] = File.ReadAllText(settingsPath);
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to capture settings for mod '{mod.Info.Id}'", ex);
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}
