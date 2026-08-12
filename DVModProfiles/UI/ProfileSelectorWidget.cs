using System.Collections.Generic;
using System.Linq;
using DV.Common;
using DV.UI;
using DV.UI.PresetEditors;
using DV.UIFramework;
using DVModProfiles.Profiles;
using UnityEngine;

namespace DVModProfiles.UI;

public class ProfileSelectorWidget : MonoBehaviour
{
    private const string NONE_OPTION = "(none)";

    private ContinueLoadNewControllerSingle controller = null!;
    private Selector difficultySelector = null!;
    private GameObject row = null!;
    private Selector profileSelector = null!;
    private readonly List<string> options = [];
    private bool suppressEvents;

    private DifficultyController? difficultyEditor;
    private PopupManager? popupManager;

    public bool Build(ContinueLoadNewControllerSingle owner)
    {
        controller = owner;

        PresetSelectorLogicDifficulty diffLogic = owner.difficultySelectorLogic;
        if (diffLogic == null || diffLogic.selector == null)
        {
            return false;
        }

        difficultySelector = diffLogic.selector;

        // Clone the whole row to keep the exact look-and-feel
        Transform diffRow = difficultySelector.transform.parent;
        if (diffRow == null || owner.editDifficultyButton == null ||
            !owner.editDifficultyButton.transform.IsChildOf(diffRow))
        {
            Main.Logger.Warning("Failed to find difficulty UI element, Derail Valley may have redone the UI.");
            return false;
        }

        string gearPath = RelativePath(diffRow, owner.editDifficultyButton.transform);

        row = Instantiate(diffRow.gameObject, diffRow.parent);
        row.name = "[ModProfile row]";
        row.transform.SetSiblingIndex(diffRow.GetSiblingIndex() + 1);

        // Drop the difficulty preset logic
        foreach (PresetSelectorLogicDifficulty logic in row.GetComponentsInChildren<PresetSelectorLogicDifficulty>(true))
        {
            Destroy(logic);
        }

        profileSelector = row.GetComponentInChildren<Selector>(true);
        if (profileSelector == null)
        {
            Destroy(row);
            return false;
        }

        profileSelector.LocalizedLabel = false;
        profileSelector.SetLabel("Mod Profile");
        profileSelector.SelectionChanged += OnProfileSelectionChanged;

        ButtonDV? gear = SetupGearButton(gearPath);
        OverrideTooltips(gear);

        Main.Logger.Log($"Injected mod profile row into '{owner.name}'");
        return true;
    }

    private ButtonDV? SetupGearButton(string gearPath)
    {
        Transform? gearTf = row.transform.Find(gearPath);
        ButtonDV? gear = gearTf?.GetComponent<ButtonDV>();
        if (gear == null)
        {
            Main.Logger.Warning($"Could not locate the cloned gear button at '{gearPath}'");
            return null;
        }

        gear.Clicked += _ => OnConfigureClicked();
        return gear;
    }

    private void OverrideTooltips(ButtonDV? gear)
    {
        foreach (UIElementTooltip tip in row.GetComponentsInChildren<UIElementTooltip>(true))
        {
            bool isGear = gear != null && tip.transform.IsChildOf(gear.transform);
            tip.enabledKey = "";
            tip.disabledKey = "";

            ProfileTooltipText custom = tip.GetComponent<ProfileTooltipText>()
                                        ?? tip.gameObject.AddComponent<ProfileTooltipText>();
            custom.Text = isGear
                ? "Save the current mod settings for the selected profile, or clear the saved ones."
                : "The mod manager profile this save uses. Its mod settings are applied when the save is loaded.";
        }
    }

    private static string RelativePath(Transform root, Transform node)
    {
        var parts = new List<string>();
        for (Transform? t = node; t != null && t != root; t = t.parent)
        {
            parts.Add(t.name);
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    public void Refresh()
    {
        if (profileSelector == null || row == null)
        {
            return;
        }

        ProfileSync.EnsureSynced();

        bool active = difficultySelector != null && difficultySelector.gameObject.activeSelf;
        if (row.activeSelf != active)
        {
            row.SetActive(active);
        }

        if (!active)
        {
            return;
        }

        string? associated = SaveAssociations.Get(CurrentSession);
        RebuildOptions(associated);
        int index = associated == null ? 0 : Mathf.Max(0, options.IndexOf(associated));

        suppressEvents = true;
        profileSelector.SetValues(options);
        profileSelector.SetSelectedIndex(index, fireEvent: false);
        suppressEvents = false;
    }

    private void RebuildOptions(string? associated)
    {
        options.Clear();
        options.Add(NONE_OPTION);
        options.AddRange(ManagerProfiles.ListNames());

        if (associated != null && !options.Contains(associated))
        {
            Main.Logger.Warning($"Save is associated with mod profile '{associated}', which the mod manager " +
                                "doesn't have");
            options.Add(associated);
        }
    }

    private IGameSession? CurrentSession => controller != null ? controller.CurrentThing : null;

    private string? SelectedProfileName =>
        profileSelector.SelectedIndex > 0 && profileSelector.SelectedIndex < options.Count
            ? options[profileSelector.SelectedIndex]
            : null;

    private void OnProfileSelectionChanged(IClickable _, int selectedIndex)
    {
        if (suppressEvents || CurrentSession is not IGameSession session)
        {
            return;
        }

        string? profileName = selectedIndex <= 0 || selectedIndex >= options.Count
            ? null
            : options[selectedIndex];
        SaveAssociations.Set(session, profileName);
        Main.Logger.Log($"Session '{session.Name}' associated with profile '{profileName ?? NONE_OPTION}'");
    }

    private void OnConfigureClicked()
    {
        if (!TryGetPopups(out PopupManager pm, out DifficultyController editor))
        {
            return;
        }

        string? selected = SelectedProfileName;
        if (selected == null)
        {
            var pick = new PopupLocalizationKeys
            {
                labelKey = "Choose the mod profile this save uses first — mod settings are saved against a " +
                           "profile. Profiles themselves are created in the mod manager.",
                positiveKey = "OK",
                negativeKey = "Cancel",
            };

            pm.ShowPopup(editor.twoButtonPopupPrefab, pick, keepLiteralData: true);
            return;
        }

        bool hasSaved = SettingsStore.Exists(selected);
        var keys = new PopupLocalizationKeys
        {
            labelKey = hasSaved
                ? $"Mod settings for profile '{selected}' — overwrite them with the current settings, or clear them?"
                : $"Save the current mod settings for profile '{selected}'?",
            positiveKey = "Save",
            negativeKey = hasSaved ? "Clear" : "Cancel",
        };

        pm.ShowPopup(editor.twoButtonPopupPrefab, keys, keepLiteralData: true).Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Positive)
            {
                SettingsStore.Capture(selected);
                Refresh();
            }
            else if (result.closedBy == PopupClosedByAction.Negative && hasSaved)
            {
                ShowClearPopup(selected);
            }
        };
    }

    private void ShowClearPopup(string profileName)
    {
        if (!TryGetPopups(out PopupManager pm, out DifficultyController editor))
        {
            return;
        }

        var keys = new PopupLocalizationKeys
        {
            labelKey = $"Clear the mod settings saved for profile '{profileName}'?",
            positiveKey = "Clear",
            negativeKey = "Cancel",
        };

        pm.ShowPopup(editor.deletePopupPrefab, keys, keepLiteralData: true).Closed += result =>
        {
            if (result.closedBy != PopupClosedByAction.Positive)
            {
                return;
            }

            SettingsStore.Delete(profileName);
            Main.Logger.Log($"Cleared the mod settings saved for profile '{profileName}'");
            Refresh();
        };
    }

    private bool TryGetPopups(out PopupManager pm, out DifficultyController editor)
    {
        difficultyEditor ??= Resources.FindObjectsOfTypeAll<DifficultyController>().FirstOrDefault();
        pm = controller.FindPopupManager(ref popupManager);
        editor = difficultyEditor!;

        if (pm == null || editor == null || editor.twoButtonPopupPrefab == null || editor.deletePopupPrefab == null)
        {
            Main.Logger.Warning("Could not resolve popup manager / prefabs for mod settings management");
            return false;
        }

        if (!pm.CanShowPopup())
        {
            Main.Logger.Warning("Popup manager can't show a popup right now");
            return false;
        }

        return true;
    }
}
