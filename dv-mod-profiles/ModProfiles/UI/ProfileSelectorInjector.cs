using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.UI;
using DV.UI.PresetEditors;
using DV.UIFramework;
using HarmonyLib;
using ModProfiles.Profiles;
using UnityEngine;
using UnityModManagerNet;

namespace ModProfiles.UI;

// Harmony patches that graft the mod-profile UI onto the game's load menu and apply the
// associated profile when a session is loaded / continued / started.
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

    // Set immediately before re-invoking a click so the gate chain is skipped and the load proceeds.
    private static bool bypassGates;

    // Intercepts the load. When re-invoked after the player has cleared every gate it lets the load
    // through; otherwise it starts the async resolution chain and cancels this click. The chain's
    // final step applies the profile and re-invokes the click to actually load.
    private static bool GateAndApply(ContinueLoadNewControllerSingle controller, string methodName)
    {
        if (bypassGates)
        {
            bypassGates = false;
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

        ModProfile? profile = ProfileStore.Load(profileName);
        if (profile == null)
        {
            Main.Logger.Warning($"Session is associated with profile '{profileName}' but it no longer exists");
            SaveAssociations.Set(controller.CurrentThing, null);
            return true;
        }

        if (ProfileApplier.MissingEnabledMods(profile).Count == 0
            && ProfileApplier.UnknownInstalledMods(profile).Count == 0
            && ProfileApplier.ModsRequiringRestart(profile).Count == 0)
        {
            ProfileApplier.Apply(profile);
            return true;
        }

        ResolveMissingMods(controller, methodName, profile);
        return false;
    }

    private static void ResolveMissingMods(ContinueLoadNewControllerSingle controller, string methodName, ModProfile profile)
    {
        try
        {
            List<string> missing = ProfileApplier.MissingEnabledMods(profile);
            if (missing.Count == 0)
            {
                ResolveUnknownMods(controller, methodName, profile);
                return;
            }

            string names = string.Join(", ", missing.Select(id =>
                profile.DisplayNames.TryGetValue(id, out string name) ? name : id));

            if (!TryGetTwoButtonPopup(controller, out PopupManager pm, out Popup prefab))
            {
                Main.Logger.Warning($"Profile '{profile.Name}' is missing mod(s) {names} and no popup is " +
                                     "available; load cancelled.");
                return;
            }

            var keys = new PopupLocalizationKeys
            {
                labelKey = $"Profile '{profile.Name}' expects mod(s) that aren't installed: {names}. Loading " +
                           "this save without them may cause errors or lost progress. Continue anyway?",
                positiveKey = "Continue",
                negativeKey = "Cancel",
            };

            OnPositive(pm, prefab, keys, () => ResolveUnknownMods(controller, methodName, profile));
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Missing-mods gate failed", ex);
        }
    }

    private static void ResolveUnknownMods(ContinueLoadNewControllerSingle controller, string methodName, ModProfile profile)
    {
        try
        {
            List<UnityModManager.ModEntry> unknown = ProfileApplier.UnknownInstalledMods(profile);
            if (unknown.Count == 0)
            {
                ResolveRestart(controller, methodName, profile);
                return;
            }

            string names = string.Join(", ", unknown.Select(m => m.Info.DisplayName));

            if (!TryGetTwoButtonPopup(controller, out PopupManager pm, out Popup prefab))
            {
                Main.Logger.Warning($"Profile '{profile.Name}' doesn't list installed mod(s) {names} and no " +
                                     "popup is available; load cancelled.");
                return;
            }

            var keys = new PopupLocalizationKeys
            {
                labelKey = $"Mod(s) installed but not in profile '{profile.Name}': {names}. Add them to the " +
                           "profile, or disable them for this save?",
                positiveKey = "Add to profile",
                negativeKey = "Disable",
            };

            pm.ShowPopup(prefab, keys, keepLiteralData: true).Closed += result =>
            {
                switch (result.closedBy)
                {
                    case PopupClosedByAction.Positive:
                        ProfileStore.AddMods(profile, unknown);
                        ResolveRestart(controller, methodName, profile);
                        break;
                    case PopupClosedByAction.Negative:
                        ResolveRestart(controller, methodName, profile);
                        break;
                }
            };
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Unknown-mods gate failed", ex);
        }
    }

    private static void ResolveRestart(ContinueLoadNewControllerSingle controller, string methodName, ModProfile profile)
    {
        try
        {
            List<UnityModManager.ModEntry> mods = ProfileApplier.ModsRequiringRestart(profile);
            if (mods.Count == 0)
            {
                ProfileApplier.Apply(profile);
                bypassGates = true;
                Reinvoke(controller, methodName);
                return;
            }

            string names = string.Join(", ", mods.Select(m => m.Info.DisplayName));

            if (!TryGetTwoButtonPopup(controller, out PopupManager pm, out Popup prefab))
            {
                Main.Logger.Warning("Restart popup unavailable; persisting changes for next manual restart.");
                ProfileApplier.Apply(profile);
                return;
            }

            string prompt = GameRestarter.CanAutoRestart
                ? "Restart now to apply?"
                : "Auto-restart isn't available under Proton. Quit now and relaunch from Steam?";

            var keys = new PopupLocalizationKeys
            {
                labelKey = $"Profile '{profile.Name}' needs to turn off mod(s) that can't be unloaded while " +
                           $"the game is running: {names}. {prompt}",
                positiveKey = GameRestarter.CanAutoRestart ? "Restart" : "Quit",
                negativeKey = "Cancel",
            };

            OnPositive(pm, prefab, keys, () =>
            {
                ProfileApplier.Apply(profile);
                GameRestarter.RestartOrQuit();
            });
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Restart gate failed", ex);
        }
    }

    private static void OnPositive(PopupManager pm, Popup prefab, PopupLocalizationKeys keys, Action onPositive)
    {
        pm.ShowPopup(prefab, keys, keepLiteralData: true).Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Positive)
            {
                onPositive();
            }
        };
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
