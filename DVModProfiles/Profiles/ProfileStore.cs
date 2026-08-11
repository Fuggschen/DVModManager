using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityModManagerNet;

namespace DVModProfiles.Profiles;

// Loads/saves ModProfile objects as JSON through ProfileStorage (local files or Steam Cloud), and
// captures the current configuration of all installed mods into a profile.
public static class ProfileStore
{
    private const string SETTINGS_FILE = "Settings.xml";
    private const string PROFILE_PREFIX = "profiles/";

    private static string KeyFor(string profileName) => PROFILE_PREFIX + Sanitize(profileName) + ".json";

    // Names of all stored profiles, sorted alphabetically
    public static List<string> ListProfileNames()
    {
        try
        {
            return ProfileStorage.Backend.ListKeys(PROFILE_PREFIX)
                .Select(LoadKey)
                .Where(p => p != null && !string.IsNullOrEmpty(p!.Name))
                .Select(p => p!.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to list profiles", ex);
            return new List<string>();
        }
    }

    public static bool Exists(string profileName) =>
        ProfileStorage.Backend.TryRead(KeyFor(profileName), out _);

    public static ModProfile? Load(string profileName) => LoadKey(KeyFor(profileName));

    private static ModProfile? LoadKey(string key)
    {
        try
        {
            if (!ProfileStorage.Backend.TryRead(key, out string json))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<ModProfile>(json);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to load profile '{key}'", ex);
            return null;
        }
    }

    public static void Save(ModProfile profile) =>
        ProfileStorage.Backend.Write(KeyFor(profile.Name), JsonConvert.SerializeObject(profile, Formatting.Indented));

    public static void Delete(string profileName) =>
        ProfileStorage.Backend.Delete(KeyFor(profileName));

    // Snapshots the live configuration of every installed mod (excluding this one) into a new
    // profile and persists it. First asks each mod to flush its settings via its OnSaveGUI hook.
    public static ModProfile Capture(string profileName)
    {
        var profile = new ModProfile { Name = profileName };
        string selfId = Main.ModEntry.Info.Id;

        foreach (UnityModManager.ModEntry mod in UnityModManager.modEntries)
        {
            if (mod.Info.Id != selfId)
            {
                CaptureMod(profile, mod);
            }
        }

        Save(profile);
        Main.Logger.Log($"Captured profile '{profileName}' ({profile.Settings.Count} settings, " +
                         $"{profile.EnabledMods.Count} enabled mods)");
        return profile;
    }

    // Adds (or refreshes) the given installed mods in an existing profile and persists it. Used
    // when the player chooses to fold mods the profile didn't know about into it.
    public static void AddMods(ModProfile profile, IEnumerable<UnityModManager.ModEntry> mods)
    {
        string selfId = Main.ModEntry.Info.Id;
        foreach (UnityModManager.ModEntry mod in mods)
        {
            if (mod.Info.Id != selfId)
            {
                CaptureMod(profile, mod);
            }
        }

        Save(profile);
    }

    // Records a single mod's current display name, enabled state, and settings into a profile
    private static void CaptureMod(ModProfile profile, UnityModManager.ModEntry mod)
    {
        profile.DisplayNames[mod.Info.Id] = mod.Info.DisplayName;

        if (mod.Enabled && !profile.EnabledMods.Contains(mod.Info.Id))
        {
            profile.EnabledMods.Add(mod.Info.Id);
        }

        try
        {
            // Ask the mod to write out its current settings before we read them.
            mod.OnSaveGUI?.Invoke(mod);

            string settingsPath = Path.Combine(mod.Path, SETTINGS_FILE);
            if (File.Exists(settingsPath))
            {
                profile.Settings[mod.Info.Id] = File.ReadAllText(settingsPath);
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
