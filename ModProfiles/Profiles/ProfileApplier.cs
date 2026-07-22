using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;

namespace ModProfiles.Profiles;

public static class ProfileApplier
{
    public readonly struct ApplyResult(IReadOnlyList<UnityModManager.ModEntry> restartMods, string summary)
    {
        public readonly IReadOnlyList<UnityModManager.ModEntry> RestartMods = restartMods;
        public readonly string Summary = summary;

        public bool RestartRequired => RestartMods.Count > 0;
    }

    private const string SETTINGS_FILE = "Settings.xml";

    // Any active mod the profile wants to disable requires a restart. DV mods can't be trusted to unload
    // cleanly at runtime, so UMM's Toggleable flag is deliberately ignored here.
    public static List<UnityModManager.ModEntry> ModsRequiringRestart(ModProfile profile)
    {
        string selfId = Main.ModEntry.Info.Id;
        return [.. UnityModManager.modEntries
            .Where(m => m.Info.Id != selfId
                        && m.Active
                        && !profile.EnabledMods.Contains(m.Info.Id))];
    }

    public static List<string> MissingEnabledMods(ModProfile profile)
    {
        string selfId = Main.ModEntry.Info.Id;
        return [.. profile.EnabledMods.Where(id => id != selfId && UnityModManager.modEntries.All(m => m.Info.Id != id))];
    }

    public static List<UnityModManager.ModEntry> UnknownInstalledMods(ModProfile profile)
    {
        string selfId = Main.ModEntry.Info.Id;
        return [.. UnityModManager.modEntries.Where(m => m.Info.Id != selfId && !ProfileKnows(profile, m.Info.Id))];
    }

    private static bool ProfileKnows(ModProfile profile, string id) =>
        profile.EnabledMods.Contains(id)
        || profile.Settings.ContainsKey(id)
        || profile.DisplayNames.ContainsKey(id);

    public static ApplyResult Apply(ModProfile profile)
    {
        string selfId = Main.ModEntry.Info.Id;
        int settingsWritten = 0;
        int toggledLive = 0;

        foreach (UnityModManager.ModEntry mod in UnityModManager.modEntries)
        {
            if (mod.Info.Id == selfId)
            {
                continue;
            }

            if (profile.Settings.TryGetValue(mod.Info.Id, out string xml))
            {
                try
                {
                    File.WriteAllText(Path.Combine(mod.Path, SETTINGS_FILE), xml);
                    settingsWritten++;
                }
                catch (Exception ex)
                {
                    Main.Logger.LogException($"Failed to write settings for mod '{mod.Info.Id}'", ex);
                }
            }

            bool desiredEnabled = profile.EnabledMods.Contains(mod.Info.Id);

            // Persist the intent so it survives a restart even if we can't apply it live
            mod.Enabled = desiredEnabled;

            // Only enabling is applied live as DV mods don't necessarily disable cleanly
            if (desiredEnabled && !mod.Active)
            {
                try
                {
                    mod.Active = true;
                    if (mod.Active)
                    {
                        toggledLive++;
                    }
                }
                catch (Exception ex)
                {
                    Main.Logger.LogException($"Failed to enable mod '{mod.Info.Id}' live", ex);
                }
            }
        }

        PersistEnabledState();

        List<string> missing = MissingEnabledMods(profile);
        if (missing.Count > 0)
        {
            Main.Logger.Warning($"Profile '{profile.Name}' expects {missing.Count} mod(s) that aren't " +
                                 $"installed; they can't be applied: {string.Join(", ", missing)}");
        }

        List<UnityModManager.ModEntry> restartMods = ModsRequiringRestart(profile);
        string summary = $"Applied profile '{profile.Name}': {settingsWritten} settings restored, " +
                         $"{toggledLive} mods enabled live" +
                         (restartMods.Count > 0 ? $", {restartMods.Count} need a restart" : "");
        Main.Logger.Log(summary);
        return new ApplyResult(restartMods, summary);
    }

    // Writes the updated ModEntry.Enabled flags to UMM's Params.xml so they persist across
    // launches.
    private static void PersistEnabledState()
    {
        try
        {
            PropertyInfo? paramsProp = typeof(UnityModManager)
                .GetProperty("Params", BindingFlags.NonPublic | BindingFlags.Static);
            object? paramsObj = paramsProp?.GetValue(null);
            paramsObj?.GetType().GetMethod("Save")?.Invoke(paramsObj, null);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to persist mod enabled state", ex);
        }
    }
}
