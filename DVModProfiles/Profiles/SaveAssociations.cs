using System;
using System.Collections.Generic;
using DV.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DVModProfiles.Profiles;

// Remembers which profile a save session is bound to.
//
// The binding lives inside the session's own GameData, which DV persists to that session's
// sessionData.json. That makes it travel with the save: DV renumbers SessionID whenever it
// detects a UID collision (routinely, when the same save reaches a second machine), so anything
// keyed on SessionID silently stops resolving there. Storing it in the session survives both
// renumbering and renames.
//
// Older versions kept an int-keyed map in cloud storage. That map is still read as a fallback so
// existing bindings aren't lost, and any hit gets migrated into the session on the spot.
public static class SaveAssociations
{
    private const string LEGACY_FILENAME = "associations.json";
    private const string GAME_DATA_KEY = "DVModProfiles.Profile";

    private static Dictionary<int, string>? legacyMap;

    // Sessions we've already tried to migrate this launch. Refresh() fires on every session
    // browse, so without this a session that can't be written to would retry on each one.
    private static readonly HashSet<int> migrationAttempted = [];

    private static Dictionary<int, string> LegacyMap
    {
        get
        {
            if (legacyMap == null)
            {
                Load();
            }

            return legacyMap!;
        }
    }

    public static void Load()
    {
        try
        {
            legacyMap = ProfileStorage.Backend.TryRead(LEGACY_FILENAME, out string json)
                ? JsonConvert.DeserializeObject<Dictionary<int, string>>(json) ?? []
                : [];
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to load legacy save associations", ex);
            legacyMap = [];
        }
    }

    public static string? Get(IGameSession? session)
    {
        if (session == null)
        {
            return null;
        }

        try
        {
            string? name = session.GameData?[GAME_DATA_KEY]?.Value<string>();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            // Not bound in the session yet. Fall back to the legacy map, and if it knows this
            // session, move the binding into the session so it works everywhere from now on.
            if (LegacyMap.TryGetValue(session.SessionID, out string legacy))
            {
                if (migrationAttempted.Add(session.SessionID))
                {
                    Main.Logger.Log($"Migrating association for session {session.SessionID} " +
                                     $"('{session.Name}') into the save: '{legacy}'");
                    Set(session, legacy);
                }

                return legacy;
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to read association for session '{session.Name}'", ex);
        }

        return null;
    }

    public static void Set(IGameSession? session, string? profileName)
    {
        if (session == null)
        {
            return;
        }

        try
        {
            JObject? data = session.GameData;
            if (data == null)
            {
                // DV leaves GameData null until something writes to it. The interface only exposes
                // a getter, so seed it through the concrete type's setter.
                data = new JObject();
                if (!TrySetGameData(session, data))
                {
                    Main.Logger.Warning($"Could not initialise GameData for session '{session.Name}'; " +
                                         "association not saved");
                    return;
                }
            }

            if (string.IsNullOrEmpty(profileName))
            {
                data.Remove(GAME_DATA_KEY);
            }
            else
            {
                data[GAME_DATA_KEY] = profileName;
            }

            session.Save();
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to save association for session '{session.Name}'", ex);
        }
    }

    private static bool TrySetGameData(IGameSession session, JObject data)
    {
        System.Reflection.PropertyInfo? prop = session.GetType().GetProperty(nameof(IGameSession.GameData));
        if (prop?.CanWrite != true)
        {
            return false;
        }

        prop.SetValue(session, data);
        return true;
    }
}
