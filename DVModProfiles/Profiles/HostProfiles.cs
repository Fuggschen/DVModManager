using Newtonsoft.Json;

namespace DVModProfiles.Profiles;

// What to do with a multiplayer host's mod settings when joining its game.
public enum HostProfileChoice
{
    Ask,

    // Use the host's settings every time
    Always,

    // Copy the host's settings on the next join, then keep using that copy without syncing again
    Once,

    Never,
}

public class HostProfilePreference
{
    // The server as the server browser lists it, or its host's Steam ID when it was joined some
    // other way.
    public string Server = "";

    // What to call it in the mod settings
    public string Label = "";

    public HostProfileChoice Choice = HostProfileChoice.Ask;
}

// Remembers, per multiplayer server, what to do with its host's mod settings, along with the copy
// a "once" choice keeps.
public static class HostProfiles
{
    private const string COPY_PREFIX = "multiplayer/";

    private static List<HostProfilePreference> Preferences => Main.Config.hostProfilePreferences;

    public static IEnumerable<HostProfilePreference> All => Preferences;

    // Where the "once" copies live, for moving them with the rest when the storage backend changes
    public static IEnumerable<string> CopyKeys(IStorageBackend backend) => backend.ListKeys(COPY_PREFIX);

    public static HostProfileChoice Get(string server) => Find(server)?.Choice ?? HostProfileChoice.Ask;

    public static void Set(string server, string label, HostProfileChoice choice)
    {
        HostProfilePreference? preference = Find(server);
        if (preference == null)
        {
            preference = new HostProfilePreference { Server = server };
            Preferences.Add(preference);
        }

        preference.Label = label;
        preference.Choice = choice;

        // A copy only means anything while the choice stays "once", so choosing "once" again later
        // takes a fresh one.
        if (choice != HostProfileChoice.Once)
        {
            DeleteCopy(server);
        }

        Main.Config.Save(Main.ModEntry);
        Main.Logger.Log($"Host mod settings for '{label}': {choice}");
    }

    public static void Forget(string server)
    {
        Preferences.RemoveAll(p => p.Server == server);
        DeleteCopy(server);
        Main.Config.Save(Main.ModEntry);
    }

    public static string Describe(string server) => Describe(server, Get(server));

    public static string Describe(string server, HostProfileChoice choice) => choice switch
    {
        HostProfileChoice.Always => "always use the host's",
        HostProfileChoice.Once => HasCopy(server) ? "use the copy synced once" : "sync once on the next join",
        HostProfileChoice.Never => "never use the host's",
        _ => "ask when joining",
    };

    public static ProfileSettings? LoadCopy(string server)
    {
        try
        {
            return ProfileStorage.Backend.TryRead(CopyKey(server), out string json)
                ? JsonConvert.DeserializeObject<ProfileSettings>(json)
                : null;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to load the copy of the host mod settings kept for '{server}'", ex);
            return null;
        }
    }

    public static void SaveCopy(string server, ProfileSettings settings)
    {
        try
        {
            ProfileStorage.Backend.Write(CopyKey(server), JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to keep a copy of the host mod settings for '{server}'", ex);
        }
    }

    private static bool HasCopy(string server)
    {
        try
        {
            return ProfileStorage.Backend.TryRead(CopyKey(server), out _);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to check for a copy of the host mod settings kept for '{server}'", ex);
            return false;
        }
    }

    private static void DeleteCopy(string server)
    {
        try
        {
            ProfileStorage.Backend.Delete(CopyKey(server));
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to delete the copy of the host mod settings kept for '{server}'", ex);
        }
    }

    private static HostProfilePreference? Find(string server) =>
        Preferences.FirstOrDefault(p => string.Equals(p.Server, server, StringComparison.Ordinal));

    private static string CopyKey(string server) => COPY_PREFIX + SettingsStore.Sanitize(server) + ".json";
}
