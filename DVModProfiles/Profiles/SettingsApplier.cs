using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
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
        var notLive = new List<string>();

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

            if (!mod.Active)
            {
                continue;
            }

            if (ApplySettingsLive(mod, xml, out string reason))
            {
                appliedLive++;
            }
            else
            {
                notLive.Add($"{mod.Info.Id} ({reason})");
            }
        }

        List<string> missing = [.. settings.Settings.Keys
            .Where(id => id != selfId && UnityModManager.modEntries.All(m => m.Info.Id != id))];
        if (missing.Count > 0)
        {
            Main.Logger.Log($"Profile '{settings.Name}' has settings for {missing.Count} mod(s) that aren't " +
                            $"installed; they were skipped: {string.Join(", ", missing)}");
        }

        if (notLive.Count > 0)
        {
            Main.Logger.Warning($"Couldn't apply these mods' settings to the running game: " +
                                $"{string.Join(", ", notLive)}. Profile changes will not be applied to them.");
        }

        Main.Logger.Log($"Applied profile '{settings.Name}': {written} mod settings restored " +
                        $"({appliedLive} applied live)");
    }

    // Patches a running mod's live settings object in place from profile XML
    private static bool ApplySettingsLive(UnityModManager.ModEntry mod, string xml, out string reason)
    {
        try
        {
            Assembly? assembly = mod.Assembly;
            if (assembly == null)
            {
                reason = "its assembly isn't loaded";
                return false;
            }

            Type? settingsType = FindSettingsType(assembly, RootElementOf(xml));
            if (settingsType == null)
            {
                reason = "couldn't tell which of its settings classes the saved XML belongs to";
                return false;
            }

            List<object> live = FindLiveSettings(assembly, settingsType);
            if (live.Count != 1)
            {
                reason = live.Count == 0
                    ? $"nothing static is holding a {settingsType.Name} to patch"
                    : $"{live.Count} separate {settingsType.Name} objects are in play, can't determine "
                    + "which to apply";
                return false;
            }

            object target = live[0];

            object updated;
            using (var reader = new StringReader(xml))
            {
                updated = new XmlSerializer(settingsType).Deserialize(reader);
            }

            CopyPublicMembers(updated, target, settingsType);

            if (target is IDrawable drawable)
            {
                drawable.OnChange();
            }

            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to apply live settings for mod '{mod.Info.Id}'", ex);
            reason = ex.Message;
            return false;
        }
    }

    // The name of the XML's root element, which is what a mod's settings class serializes as.
    private static string? RootElementOf(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml));
            return reader.MoveToContent() == XmlNodeType.Element ? reader.Name : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static Type? FindSettingsType(Assembly assembly, string? rootElement)
    {
        Type baseType = typeof(UnityModManager.ModSettings);
        List<Type> candidates = [.. SafeGetTypes(assembly)
            .Where(t => baseType.IsAssignableFrom(t) && t != baseType && !t.IsAbstract)];

        // A mod with more than one settings class saves each to its own file, so the root element
        // is what says which of them this XML came out of.
        return candidates.Count <= 1
            ? candidates.FirstOrDefault()
            : candidates.FirstOrDefault(t => XmlNameOf(t) == rootElement);
    }

    private static string XmlNameOf(Type type) =>
        Attribute.GetCustomAttribute(type, typeof(XmlRootAttribute)) is XmlRootAttribute root
        && !string.IsNullOrEmpty(root.ElementName)
            ? root.ElementName
            : type.Name;

    // Every distinct settings object the mod keeps statically
    private static List<object> FindLiveSettings(Assembly assembly, Type settingsType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Static | BindingFlags.DeclaredOnly;

        var found = new List<object>();
        foreach (Type type in SafeGetTypes(assembly))
        {
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.FieldType == settingsType)
                {
                    Remember(found, field.GetValue(null));
                }
            }

            foreach (PropertyInfo prop in type.GetProperties(flags))
            {
                if (prop.PropertyType != settingsType || !prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                try
                {
                    Remember(found, prop.GetValue(null, null));
                }
                catch
                {
                    // Property getter threw, skip it
                }
            }
        }

        return found;
    }

    private static void Remember(List<object> found, object? value)
    {
        if (value != null && !found.Any(existing => ReferenceEquals(existing, value)))
        {
            found.Add(value);
        }
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
