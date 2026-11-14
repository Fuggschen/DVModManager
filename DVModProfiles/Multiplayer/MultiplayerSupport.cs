using System.Reflection;
using UnityModManagerNet;

namespace DVModProfiles.Multiplayer;

// Hooks into the Multiplayer mod when it's running. Its API ships with it rather than with us, so
// everything that touches the API is in an assembly of its own, loaded only once the Multiplayer
// mod is known to be, and this one never references it.
internal static class MultiplayerSupport
{
    public const string MULTIPLAYER_MOD_ID = "Multiplayer";
    private const string HOOKS_ASSEMBLY = "DVModProfiles.Multiplayer.dll";
    private const string HOOKS_TYPE = "DVModProfiles.Multiplayer.MultiplayerHooks";

    public static void TryInit()
    {
        if (UnityModManager.FindMod(MULTIPLAYER_MOD_ID) is not { Active: true })
        {
            return;
        }

        try
        {
            if (InitHooks())
            {
                HostProfileClient.Init();
                Main.Logger.Log("Multiplayer mod found; hosts share their mod settings with the players who join");
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Couldn't hook into the Multiplayer mod; host mod settings won't be shared",
                                     ex is TargetInvocationException { InnerException: { } inner } ? inner : ex);
        }
    }

    private static bool InitHooks()
    {
        string path = Path.Combine(Main.ModEntry.Path, HOOKS_ASSEMBLY);
        if (!File.Exists(path))
        {
            Main.Logger.Warning($"{HOOKS_ASSEMBLY} is missing; host mod settings won't be shared");
            return false;
        }

        MethodInfo? init = Assembly.LoadFrom(path).GetType(HOOKS_TYPE)?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
        if (init == null)
        {
            Main.Logger.Warning($"{HOOKS_ASSEMBLY} doesn't match this version of the mod; host mod settings won't be " +
                                "shared");
            return false;
        }

        object? hooked = init.Invoke(null,
        [
            (Action<string>)Main.Logger.Log,
            (Action<string, Exception>)Main.Logger.LogException,
            (Func<bool>)(() => Main.Config.shareWithMultiplayerClients),
            (Func<byte[]>)HostProfile.Current,
            (Action)HostProfileClient.Start,
            (Action)HostProfileClient.Stop,
            (Action<int, int, int, byte[]>)HostProfileClient.Receive,
            (Action)HostProfileClient.Unreadable,
        ]);

        if (hooked is not true)
        {
            Main.Logger.Log("The Multiplayer mod is installed but not loaded; host mod settings won't be shared");
            return false;
        }

        return true;
    }
}
