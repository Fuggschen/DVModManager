using HarmonyLib;
using UnityModManagerNet;

namespace DVModProfiles.Profiles;

internal static class SettingsAutoCapture
{
    private static string? loadedProfile;

    public static void TrackSession(string profileName) => loadedProfile = profileName;

    public static void ForgetSession() => loadedProfile = null;

    // Where the mod manager flushes every mod's settings, from its Save button and on shutdown.
    [HarmonyPatch(typeof(UnityModManager), nameof(UnityModManager.SaveSettingsAndParams))]
    private static class SaveSettingsAndParamsPatch
    {
        private static void Postfix()
        {
            try
            {
                Capture();
            }
            catch (Exception ex)
            {
                Main.Logger.LogException("Failed to save the changed mod settings to the loaded save's profile", ex);
            }
        }
    }

    private static void Capture()
    {
        string? profileName = loadedProfile;
        if (profileName == null)
        {
            return;
        }

        if (!SettingsStore.Exists(profileName))
        {
            Main.Logger.Log($"Mod settings were saved, but profile '{profileName}' has no association defined, so " +
                            "they were left alone. Use the profile button in the main menu to associate them.");
            return;
        }

        string? active = ManagerProfiles.ActiveProfileName;
        if (active != null && !string.Equals(profileName, active, StringComparison.Ordinal))
        {
            Main.Logger.Warning($"The mod manager now has profile '{active}' applied rather than the loaded " +
                                $"save's '{profileName}', so the changed settings weren't saved to it.");
            return;
        }

        SettingsStore.Capture(profileName);
    }
}
