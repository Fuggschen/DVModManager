using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ModProfiles.Profiles;

public static class SaveAssociations
{
    private const string FILENAME = "associations.json";

    private static Dictionary<int, string>? map;

    private static Dictionary<int, string> Map
    {
        get
        {
            if (map == null)
            {
                Load();
            }

            return map!;
        }
    }

    public static void Load()
    {
        try
        {
            map = ProfileStorage.Backend.TryRead(FILENAME, out string json)
                ? JsonConvert.DeserializeObject<Dictionary<int, string>>(json) ?? []
                : [];
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to load save associations", ex);
            map = [];
        }
    }

    private static void Save()
    {
        try
        {
            ProfileStorage.Backend.Write(FILENAME, JsonConvert.SerializeObject(Map, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to save associations", ex);
        }
    }

    public static string? Get(int sessionId) =>
        Map.TryGetValue(sessionId, out string name) ? name : null;

    public static void Set(int sessionId, string? profileName)
    {
        if (string.IsNullOrEmpty(profileName))
        {
            Map.Remove(sessionId);
        }
        else
        {
            Map[sessionId] = profileName!;
        }

        Save();
    }

    public static void ForgetProfile(string profileName)
    {
        int[] stale = Map.Where(kvp => kvp.Value == profileName).Select(kvp => kvp.Key).ToArray();
        foreach (int sessionId in stale)
        {
            Map.Remove(sessionId);
        }

        if (stale.Length > 0)
        {
            Save();
        }
    }
}
