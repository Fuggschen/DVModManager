using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using UnityModManagerNet;

namespace DVModProfiles.Profiles;

// Restores the mod settings saved against a profile.
public static class SettingsApplier
{
    private const string SETTINGS_FILE = "Settings.xml";

    public static void Apply(string profileName)
    {
        ProfileSettings? settings = SettingsStore.Load(profileName);
        if (settings == null)
        {
            Main.Logger.Log($"No mod settings saved for profile '{profileName}'; leaving mods as they are");
            return;
        }

        Apply(settings);
    }

    public static void Apply(ProfileSettings settings)
    {
        string selfId = Main.ModEntry.Info.Id;
        int written = 0;
        int appliedLive = 0;

        foreach (UnityModManager.ModEntry mod in UnityModManager.modEntries)
        {
            if (mod.Info.Id == selfId || !settings.Settings.TryGetValue(mod.Info.Id, out string xml))
            {
                continue;
            }

            try
            {
                File.WriteAllText(Path.Combine(mod.Path, SETTINGS_FILE), xml);
                written++;
            }
            catch (Exception ex)
            {
                Main.Logger.LogException($"Failed to write settings for mod '{mod.Info.Id}'", ex);
            }

            if (mod.Active && ApplySettingsLive(mod, xml))
            {
                appliedLive++;
            }
        }

        List<string> missing = [.. settings.Settings.Keys
            .Where(id => id != selfId && UnityModManager.modEntries.All(m => m.Info.Id != id))];
        if (missing.Count > 0)
        {
            Main.Logger.Log($"Profile '{settings.Name}' has settings for {missing.Count} mod(s) that aren't " +
                            $"installed; they were skipped: {string.Join(", ", missing)}");
        }

        Main.Logger.Log($"Applied profile '{settings.Name}': {written} mod settings restored " +
                        $"({appliedLive} applied live)");
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
