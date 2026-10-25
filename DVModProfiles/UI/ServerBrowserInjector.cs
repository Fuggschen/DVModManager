using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DVModProfiles.UI;

// Harmony patches that graft the host mod settings choice onto the Multiplayer mod's server
// browser. It isn't ours to reference, so it's found by name, and if it's been reworked out from
// under these patches they stand down rather than fail the mod.
internal static class ServerBrowserInjector
{
    private static readonly Type? Pane = AccessTools.TypeByName("Multiplayer.Components.MainMenu.ServerBrowserPane");

    private static bool CanPatch(MethodBase? target)
    {
        if (Pane != null && target == null)
        {
            Main.Logger.Warning("The Multiplayer mod's server browser has changed; host mod settings can't be " +
                                "chosen from it");
        }

        return target != null;
    }

    [HarmonyPatch]
    private static class AwakePatch
    {
        private static bool Prepare() => CanPatch(TargetMethod());

        private static MethodBase? TargetMethod() => Pane == null ? null : AccessTools.Method(Pane, "Awake");

        private static void Postfix(MonoBehaviour __instance)
        {
            if (__instance.GetComponent<HostProfileBrowserWidget>() != null)
            {
                return;
            }

            HostProfileBrowserWidget widget = __instance.gameObject.AddComponent<HostProfileBrowserWidget>();
            if (!widget.Build(__instance))
            {
                UnityEngine.Object.Destroy(widget);
                Main.Logger.Warning("Could not inject the host mod settings row into the server browser");
            }
        }
    }

    // Where the browser updates for whichever server is selected, or none
    [HarmonyPatch]
    private static class SelectionPatch
    {
        private static bool Prepare() => CanPatch(TargetMethod());

        private static MethodBase? TargetMethod() =>
            Pane == null ? null : AccessTools.Method(Pane, "OnSelectedIndexChanged");

        private static void Postfix(MonoBehaviour __instance)
        {
            __instance.GetComponent<HostProfileBrowserWidget>()?.Refresh();
        }
    }
}
