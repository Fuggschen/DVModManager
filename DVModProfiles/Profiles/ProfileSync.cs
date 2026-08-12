using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DVModProfiles.Profiles;

// Keeps the mod manager's profiles in step with Steam Cloud.
//
// A local manifest of what was last synced is what tells a profile that's new on one
// side apart from one that was deleted on the other.
public static class ProfileSync
{
    private const string CLOUD_PREFIX = "manager-profiles/";

    // This records what *this machine* last agreed with the cloud on, so it must never itself be synced.
    private const string MANIFEST_FILE = "manager-sync.json";

    // The manager can save a profile while the game is running, but we don't need to spam load either.
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(15);

    private static DateTime lastRun;
    private static bool everRun;

    // Syncs if it hasn't run recently. Called when the load menu needs profiles, which is late
    // enough that the game has Steamworks up.
    public static void EnsureSynced()
    {
        if (everRun && DateTime.UtcNow - lastRun < MinInterval)
        {
            return;
        }

        lastRun = DateTime.UtcNow;
        everRun = true;
        Run();
    }

    // Forces the next EnsureSynced to do the work, e.g. after the cloud setting is switched on
    public static void Reset() => everRun = false;

    private static void Run()
    {
        try
        {
            if (ProfileStorage.Backend is not CloudBackend cloud)
            {
                Main.Logger.Log("Steam Cloud is off; the mod manager's profiles stay local to this machine");
                return;
            }

            string? root = ManagerProfiles.StorageRoot;
            string? dir = ManagerProfiles.ProfilesDir;
            if (root == null || dir == null)
            {
                Main.Logger.Log("Mod manager storage not found; no profiles to sync");
                return;
            }

            Sync(cloud, root, dir);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to sync the mod manager's profiles with Steam Cloud", ex);
        }
    }

    internal static void Sync(IStorageBackend cloud, string storageRoot, string profilesDir)
    {
        if (!Directory.Exists(storageRoot))
        {
            Main.Logger.Warning($"The mod manager's storage folder '{storageRoot}' isn't there; skipping the " +
                                "profile sync rather than treating its profiles as deleted");
            return;
        }

        // In the event this mod is loaded on a machine that doesn't yet have the mod manager
        Directory.CreateDirectory(profilesDir);

        Dictionary<string, Entry> local = ReadLocal(profilesDir);
        Dictionary<string, Entry> remote = ReadRemote(cloud);
        Dictionary<string, DateTime> synced = ReadManifest(profilesDir);

        var updated = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var deleted = new List<string>();
        int uploaded = 0, downloaded = 0;

        foreach (string name in local.Keys.Concat(remote.Keys).Distinct(StringComparer.Ordinal))
        {
            bool hasLocal = local.TryGetValue(name, out Entry here);
            bool hasRemote = remote.TryGetValue(name, out Entry there);
            bool wasSynced = synced.TryGetValue(name, out DateTime lastAgreed);

            if (hasLocal && hasRemote)
            {
                if (here.Modified > there.Modified)
                {
                    cloud.Write(CloudKey(name), here.Json);
                    updated[name] = here.Modified;
                    uploaded++;
                }
                else if (there.Modified > here.Modified)
                {
                    File.WriteAllText(here.Key, there.Json);
                    updated[name] = there.Modified;
                    downloaded++;
                }
                else
                {
                    updated[name] = here.Modified;
                }
            }
            else if (hasLocal)
            {
                // If it hasn't been touched here since the last sync, that's another machine's delete arriving,
                // otherwise it's ours to sync upwards.
                if (wasSynced && here.Modified <= lastAgreed)
                {
                    File.Delete(here.Key);
                    deleted.Add($"'{name}' here");
                }
                else
                {
                    cloud.Write(CloudKey(name), here.Json);
                    updated[name] = here.Modified;
                    uploaded++;
                }
            }
            else
            {
                // This machine's delete, unless the cloud copy has moved on since.
                if (wasSynced && there.Modified <= lastAgreed)
                {
                    cloud.Delete(there.Key);
                    deleted.Add($"'{name}' from the cloud");
                }
                else
                {
                    Directory.CreateDirectory(profilesDir);
                    File.WriteAllText(Path.Combine(profilesDir, FileNameFor(name)), there.Json);
                    updated[name] = there.Modified;
                    downloaded++;
                }
            }
        }

        WriteManifest(profilesDir, updated);

        if (uploaded + downloaded + deleted.Count > 0)
        {
            // The profiles on disk have moved under it
            ManagerProfiles.Invalidate();
            Main.Logger.Log($"Synced mod manager profiles with Steam Cloud: {uploaded} uploaded, " +
                            $"{downloaded} downloaded" +
                            (deleted.Count > 0 ? $", deleted {string.Join(", ", deleted)}" : ""));
        }
    }

    private static Dictionary<string, Entry> ReadLocal(string dir)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (!Directory.Exists(dir))
        {
            return entries;
        }

        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                Add(entries, file, json, Path.GetFileNameWithoutExtension(file));
            }
            catch (Exception ex)
            {
                Main.Logger.LogException($"Skipping unreadable profile '{file}'", ex);
            }
        }

        return entries;
    }

    private static Dictionary<string, Entry> ReadRemote(IStorageBackend cloud)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        foreach (string key in cloud.ListKeys(CLOUD_PREFIX))
        {
            try
            {
                if (cloud.TryRead(key, out string json))
                {
                    Add(entries, key, json, Path.GetFileNameWithoutExtension(key));
                }
            }
            catch (Exception ex)
            {
                Main.Logger.LogException($"Skipping unreadable cloud profile '{key}'", ex);
            }
        }

        return entries;
    }

    private static void Add(Dictionary<string, Entry> entries, string key, string json, string fallbackName)
    {
        var parsed = JObject.Parse(json);
        string? name = parsed.Property("Name", StringComparison.OrdinalIgnoreCase)?.Value.Value<string>();
        DateTime modified = ParseUtc(parsed.Property("LastModifiedAt", StringComparison.OrdinalIgnoreCase)
            ?.Value.Value<string>());

        entries[string.IsNullOrEmpty(name) ? fallbackName : name!] = new Entry(key, modified, json);
    }

    private static Dictionary<string, DateTime> ReadManifest(string profilesDir)
    {
        var synced = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(ManifestPath))
            {
                return synced;
            }

            Manifest? stored = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(ManifestPath));
            if (stored == null)
            {
                return synced;
            }

            // The player can repoint the manager's storage path whenever they like, and the manager
            // doesn't carry the old profiles over. What's missing from the new folder was never
            // deleted, so nothing here may claim it was.
            if (!SamePath(stored.ProfilesDir, profilesDir))
            {
                if (stored.Profiles.Count > 0)
                {
                    Main.Logger.Log($"The mod manager's profiles now live in '{profilesDir}', not " +
                                    $"'{stored.ProfilesDir}'; merging both sides rather than reading what's " +
                                    "missing from the new folder as deleted");
                }

                return synced;
            }

            foreach (KeyValuePair<string, string> entry in stored.Profiles)
            {
                synced[entry.Key] = ParseUtc(entry.Value);
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Couldn't read the profile sync manifest; treating everything as new", ex);
        }

        return synced;
    }

    private static void WriteManifest(string profilesDir, Dictionary<string, DateTime> synced)
    {
        try
        {
            var manifest = new Manifest
            {
                ProfilesDir = profilesDir,
                Profiles = synced.ToDictionary(
                    e => e.Key,
                    e => e.Value.ToString("o", CultureInfo.InvariantCulture),
                    StringComparer.Ordinal),
            };

            File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to write the profile sync manifest", ex);
        }
    }

    private static bool SamePath(string? a, string? b) =>
        string.Equals(a?.TrimEnd('\\', '/'), b?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string ManifestPath => Path.Combine(Main.ModEntry.Path, MANIFEST_FILE);

    private static string CloudKey(string profileName) => CLOUD_PREFIX + FileNameFor(profileName);

    private static string FileNameFor(string profileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            profileName = profileName.Replace(c, '_');
        }

        return profileName + ".json";
    }

    // The manager writes these with System.Text.Json, so they're round-trippable UTC
    private static DateTime ParseUtc(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
                          DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                          out DateTime parsed)
            ? parsed
            : DateTime.MinValue;

    private sealed class Manifest
    {
        public string ProfilesDir = "";

        // Profile name -> the LastModifiedAt both sides last agreed on
        public Dictionary<string, string> Profiles = [];
    }

    private readonly struct Entry(string key, DateTime modified, string json)
    {
        // Local file path, or cloud key
        public readonly string Key = key;
        public readonly DateTime Modified = modified;
        public readonly string Json = json;
    }
}
