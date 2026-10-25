using Newtonsoft.Json;

namespace DVModProfiles.Profiles;

// Plays with someone else's mod settings for a while, then puts the player's own back.
//
// Applying settings rewrites every mod's Settings.xml, so the player's own are kept on disk until
// they're restored: if the game exits without the restore happening, the next launch does it.
public static class BorrowedSettings
{
    // About this install's Settings.xml files, so it stays on this machine and is never synced
    private const string RESTORE_FILE = "multiplayer-restore.json";

    private static ProfileSettings? own;

    public static void Use(ProfileSettings borrowed)
    {
        if (own == null)
        {
            ProfileSettings snapshot = SettingsStore.Snapshot("your own mod settings");
            try
            {
                File.WriteAllText(RestorePath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Main.Logger.LogException($"Couldn't keep a copy of your own mod settings, so '{borrowed.Name}' " +
                                         "wasn't applied", ex);
                return;
            }

            own = snapshot;
        }

        SettingsApplier.Apply(borrowed);
    }

    public static void Restore()
    {
        if (own == null)
        {
            return;
        }

        ProfileSettings settings = own;
        own = null;

        try
        {
            SettingsApplier.Apply(settings);
            File.Delete(RestorePath);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to put your own mod settings back", ex);
        }
    }

    // Puts the player's own settings back if the game exited while it had borrowed someone else's
    public static void RecoverAfterExit()
    {
        try
        {
            if (!File.Exists(RestorePath))
            {
                return;
            }

            Main.Logger.Log("The game exited while playing with a multiplayer host's mod settings; putting yours back");
            own = JsonConvert.DeserializeObject<ProfileSettings>(File.ReadAllText(RestorePath));
            if (own == null)
            {
                File.Delete(RestorePath);
                return;
            }

            Restore();
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to put your own mod settings back after the game exited mid-game", ex);
        }
    }

    private static string RestorePath => Path.Combine(Main.ModEntry.Path, RESTORE_FILE);
}
