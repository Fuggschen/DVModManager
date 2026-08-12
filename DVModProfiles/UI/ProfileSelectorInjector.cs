using System;
using System.Linq;
using System.Reflection;
using DV.UI;
using DV.UI.PresetEditors;
using DV.UIFramework;
using HarmonyLib;
using DVModProfiles.Profiles;
using UnityEngine;

namespace DVModProfiles.UI;

// Harmony patches that graft the mod-profile UI onto the game's load menu, and that check a
// session's profile against the mod manager's before letting it load.
internal static class ProfileSelectorInjector
{
    [HarmonyPatch(typeof(ContinueLoadNewControllerSingle), "Awake")]
    private static class AwakePatch
    {
        private static void Postfix(ContinueLoadNewControllerSingle __instance)
        {
            if (__instance.GetComponent<ProfileSelectorWidget>() != null)
            {
                return;
            }

            ProfileSelectorWidget widget = __instance.gameObject.AddComponent<ProfileSelectorWidget>();
            if (!widget.Build(__instance))
            {
                UnityEngine.Object.Destroy(widget);
                Main.Logger.Warning($"Could not inject mod profile selector into '{__instance.name}'");
            }
        }
    }

    [HarmonyPatch(typeof(ContinueLoadNewControllerSingle), "RefreshInterface")]
    private static class RefreshInterfacePatch
    {
        private static void Postfix(ContinueLoadNewControllerSingle __instance)
        {
            __instance.GetComponent<ProfileSelectorWidget>()?.Refresh();
        }
    }

    [HarmonyPatch(typeof(ContinueLoadNewControllerSingle), "OnLoadClicked")]
    private static class OnLoadClickedPatch
    {
        private static bool Prefix(ContinueLoadNewControllerSingle __instance) => GateAndApply(__instance, "OnLoadClicked");
    }

    [HarmonyPatch(typeof(ContinueLoadNewControllerSingle), "OnContinueClicked")]
    private static class OnContinueClickedPatch
    {
        private static bool Prefix(ContinueLoadNewControllerSingle __instance) => GateAndApply(__instance, "OnContinueClicked");
    }

    [HarmonyPatch(typeof(ContinueLoadNewControllerSingle), "OnStartNewSessionClicked")]
    private static class OnStartNewSessionClickedPatch
    {
        private static bool Prefix(ContinueLoadNewControllerSingle __instance) => GateAndApply(__instance, "OnStartNewSessionClicked");
    }

    private static PopupManager? popupManager;

    // Set immediately before re-invoking a click so the gate is skipped and the load proceeds.
    private static bool bypassGate;

    // Intercepts the load. Offers to quit if the loaded profile doesn't match.
    // If it does match, the profile's mod settings are restored and the load carries on.
    private static bool GateAndApply(ContinueLoadNewControllerSingle controller, string methodName)
    {
        if (bypassGate)
        {
            bypassGate = false;
            return true;
        }

        if (controller.CurrentThing == null)
        {
            return true;
        }

        string? profileName = SaveAssociations.Get(controller.CurrentThing);
        if (profileName == null)
        {
            return true;
        }

        string? active = ManagerProfiles.ActiveProfileName;

        // With no manager storage to read there's nothing to check against, so take the save at
        // its word and restore what settings we have.
        if (active == null || string.Equals(profileName, active, StringComparison.Ordinal))
        {
            SettingsApplier.Apply(profileName);
            return true;
        }

        if (!TryGetTwoButtonPopup(controller, out PopupManager pm, out Popup prefab))
        {
            Main.Logger.Warning($"Session is associated with profile '{profileName}' but the mod manager has " +
                                $"'{active}' applied, and no popup is available to ask; loading anyway.");
            SettingsApplier.Apply(profileName);
            return true;
        }

        WarnProfileMismatch(controller, methodName, pm, prefab, profileName, active);
        return false;
    }

    private static void WarnProfileMismatch(ContinueLoadNewControllerSingle controller, string methodName,
                                            PopupManager pm, Popup prefab, string profileName, string active)
    {
        try
        {
            var keys = new PopupLocalizationKeys
            {
                labelKey = $"This save uses mod profile '{profileName}', but the mod manager last applied " +
                           $"'{active}', so the mods loaded right now are that profile's. Loading anyway may " +
                           "cause errors or lost progress. Quit, so you can switch profiles in the mod manager?",
                positiveKey = "Quit",
                negativeKey = "Load anyway",
            };

            pm.ShowPopup(prefab, keys, keepLiteralData: true).Closed += result =>
            {
                switch (result.closedBy)
                {
                    case PopupClosedByAction.Positive:
                        Main.Logger.Log($"Quitting so profile '{profileName}' can be applied in the mod manager");
                        Application.Quit();
                        break;
                    case PopupClosedByAction.Negative:
                        SettingsApplier.Apply(profileName);
                        bypassGate = true;
                        Reinvoke(controller, methodName);
                        break;
                }
            };
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Profile mismatch gate failed", ex);
        }
    }

    private static bool TryGetTwoButtonPopup(ContinueLoadNewControllerSingle controller, out PopupManager pm, out Popup prefab)
    {
        pm = controller.FindPopupManager(ref popupManager);
        DifficultyController? editor = Resources.FindObjectsOfTypeAll<DifficultyController>().FirstOrDefault();
        prefab = editor != null ? editor.twoButtonPopupPrefab : null!;
        return pm != null && prefab != null && pm.CanShowPopup();
    }

    private static void Reinvoke(ContinueLoadNewControllerSingle controller, string methodName)
    {
        try
        {
            MethodInfo? method = AccessTools.Method(typeof(ContinueLoadNewControllerSingle), methodName, [typeof(IClickable)]);
            method?.Invoke(controller, [null]);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to re-invoke {methodName}", ex);
        }
    }
}
