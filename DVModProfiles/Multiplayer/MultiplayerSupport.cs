using UnityModManagerNet;

namespace DVModProfiles.Multiplayer;

// Hooks into the Multiplayer mod when it's running. Its API ships with it rather than with us, so
// nothing may touch the API's types until the Multiplayer mod is known to be loaded.
internal static class MultiplayerSupport
{
    private const string MULTIPLAYER_MOD_ID = "Multiplayer";

    public static void TryInit()
    {
        if (UnityModManager.FindMod(MULTIPLAYER_MOD_ID) is not { Active: true })
        {
            return;
        }

        try
        {
            if (MultiplayerHooks.Init())
            {
                HostProfileClient.Init();
                Main.Logger.Log("Multiplayer mod found; hosts share their mod settings with the players who join");
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Couldn't hook into the Multiplayer mod; host mod settings won't be shared", ex);
        }
    }
}
