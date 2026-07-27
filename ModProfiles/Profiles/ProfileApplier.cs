using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
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
        int settingsAppliedLive = 0;
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

                if (mod.Active && ApplySettingsLive(mod, xml))
                {
                    settingsAppliedLive++;
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
        string summary = $"Applied profile '{profile.Name}': {settingsWritten} settings restored " +
                         $"({settingsAppliedLive} applied live), {toggledLive} mods enabled live" +
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

    // Patches a running mod's live settings object in place from profile XML
    private static bool ApplySettingsLive(UnityModManager.ModEntry mod, string xml)
    {
        try
        {
            Assembly? assembly = mod.Assembly;
            if (assembly == null)
            {
                return false;
            }

            Type? settingsType = FindSettingsType(assembly);
            if (settingsType == null)
            {
                return false;
            }

            MemberInfo? holder = FindLiveSettingsHolder(assembly, settingsType);
            if (holder == null)
            {
                return false;
            }

            object live = holder is FieldInfo field
                ? field.GetValue(null)
                : ((PropertyInfo)holder).GetValue(null, null);

            object updated;
            using (var reader = new StringReader(xml))
            {
                updated = new XmlSerializer(settingsType).Deserialize(reader);
            }

            CopyPublicMembers(updated, live, settingsType);

            if (live is IDrawable drawable)
            {
                drawable.OnChange();
            }

            return true;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to apply live settings for mod '{mod.Info.Id}'", ex);
            return false;
        }
    }

    private static Type? FindSettingsType(Assembly assembly)
    {
        Type baseType = typeof(UnityModManager.ModSettings);
        Type[] candidates = [.. SafeGetTypes(assembly)
            .Where(t => baseType.IsAssignableFrom(t) && t != baseType && !t.IsAbstract)];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static MemberInfo? FindLiveSettingsHolder(Assembly assembly, Type settingsType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Static | BindingFlags.DeclaredOnly;

        var matches = new List<MemberInfo>();
        foreach (Type type in SafeGetTypes(assembly))
        {
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.FieldType == settingsType && field.GetValue(null) != null)
                {
                    matches.Add(field);
                }
            }

            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (prop.PropertyType != settingsType || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                try
                {
                    if (prop.GetValue(null, null) != null)
                    {
                        matches.Add(prop);
                    }
                }
                catch
                {
                    // Property getter threw; not a plain settings holder, skip it.
                }
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static void CopyPublicMembers(object from, object to, Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            field.SetValue(to, field.GetValue(from));
        }

        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
                prop.SetValue(to, prop.GetValue(from, null), null);
            }
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.Where(t => t != null)];
        }
    }
}
