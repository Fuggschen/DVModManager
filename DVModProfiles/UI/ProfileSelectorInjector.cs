using System.Reflection;
using DV.Common;
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
            SettingsAutoCapture.ForgetSession();

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

    // Where every button that starts a game converges
    [HarmonyPatch(typeof(MainMenuController), "OnContinueGameRequested")]
    private static class ContinueGamePatch
    {
        private static bool Prefix(MainMenuController __instance, ISaveGame saveGame) =>
            GateAndApply(__instance, "OnContinueGameRequested", saveGame?.ParentSession, saveGame);
    }

    [HarmonyPatch(typeof(MainMenuController), "OnStartNewGameRequested")]
    private static class StartNewGamePatch
    {
        private static bool Prefix(MainMenuController __instance, UIStartGameData data) =>
            GateAndApply(__instance, "OnStartNewGameRequested", data?.session, data);
    }

    private static PopupManager? popupManager;

    // Set immediately before re-invoking a load so the gate is skipped and it proceeds.
    private static bool bypassGate;

    // Intercepts the load. Offers to quit, load anyway, or go back if the loaded profile doesn't
    // match. If it does match, the profile's mod settings are restored and the load carries on.
    private static bool GateAndApply(MainMenuController menu, string methodName, IGameSession? session,
                                     object? argument)
    {
        if (bypassGate)
        {
            bypassGate = false;
            return true;
        }

        string? profileName = SaveAssociations.Get(session);
        if (profileName == null)
        {
            SettingsAutoCapture.ForgetSession();
            return true;
        }

        string? active = ManagerProfiles.ActiveProfileName;

        // With no manager storage to read there's nothing to check against, so take the save at
        // its word and restore what settings we have.
        if (active == null || string.Equals(profileName, active, StringComparison.Ordinal))
        {
            SettingsApplier.Apply(profileName);
            SettingsAutoCapture.TrackSession(profileName);
            return true;
        }

        if (!TryGetPopup(menu, out PopupManager pm, out Popup prefab))
        {
            Main.Logger.Warning($"Session is associated with profile '{profileName}' but the mod manager has " +
                                $"'{active}' applied, and no popup is available to ask; loading anyway.");
            SettingsApplier.Apply(profileName);
            SettingsAutoCapture.ForgetSession();
            return true;
        }

        WarnProfileMismatch(menu, methodName, argument, pm, prefab, profileName, active);
        return false;
    }

    private static void WarnProfileMismatch(MainMenuController menu, string methodName, object? argument,
                                            PopupManager pm, Popup prefab, string profileName, string active)
    {
        try
        {
            var keys = new PopupLocalizationKeys
            {
                labelKey = $"This save uses mod profile '{profileName}', but the mod manager last applied " +
                           $"'{active}', so the mods loaded right now are that profile's. Loading anyway may " +
                           "cause errors or lost progress. Quit to switch profiles in the mod manager, or go " +
                           "back and pick another save.",
                positiveKey = "Quit",
                negativeKey = "Load anyway",
                abortionKey = "Go back",
            };

            Popup popup = pm.ShowPopup(prefab, keys, keepLiteralData: true);
            popup.EscAction = PopupClosedByAction.Abortion;

            popup.Closed += result =>
            {
                switch (result.closedBy)
                {
                    case PopupClosedByAction.Positive:
                        Main.Logger.Log($"Quitting so profile '{profileName}' can be applied in the mod manager");
                        Application.Quit();
                        break;
                    case PopupClosedByAction.Negative:
                        SettingsApplier.Apply(profileName);
                        SettingsAutoCapture.ForgetSession();
                        bypassGate = true;
                        Reinvoke(menu, methodName, argument);
                        break;
                    case PopupClosedByAction.Abortion:
                        Main.Logger.Log($"Load of a save using profile '{profileName}' cancelled");
                        break;
                }
            };
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Profile mismatch gate failed", ex);
        }
    }

    private static bool TryGetPopup(MainMenuController menu, out PopupManager pm, out Popup prefab)
    {
        pm = menu.FindPopupManager(ref popupManager);

        Popup? found = Resources.FindObjectsOfTypeAll<PopupNotificationReferences>()
            .Select(references => references.popup3Buttons)
            .FirstOrDefault(popup => popup != null && popup.abortionButton != null);

        prefab = found!;
        return pm != null && found != null && pm.CanShowPopup();
    }

    private static void Reinvoke(MainMenuController menu, string methodName, object? argument)
    {
        try
        {
            MethodInfo? method = AccessTools.Method(typeof(MainMenuController), methodName);
            method?.Invoke(menu, [argument]);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Failed to re-invoke {methodName}", ex);
        }
    }
}
