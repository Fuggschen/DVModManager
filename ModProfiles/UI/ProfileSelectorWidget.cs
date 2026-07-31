using System.Collections.Generic;
using System.Linq;
using DV.Common;
using DV.UI;
using DV.UI.PresetEditors;
using DV.UIFramework;
using ModProfiles.Profiles;
using TMPro;
using UnityEngine;

namespace ModProfiles.UI;

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
                ? "Save the current mod configuration as a profile, or delete the selected one."
                : "The mod profile applied when this save is loaded.";
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

        bool active = difficultySelector != null && difficultySelector.gameObject.activeSelf;
        if (row.activeSelf != active)
        {
            row.SetActive(active);
        }

        if (!active)
        {
            return;
        }

        RebuildOptions();

        string? associated = SaveAssociations.Get(CurrentSession);
        int index = associated == null ? 0 : Mathf.Max(0, options.IndexOf(associated));

        suppressEvents = true;
        profileSelector.SetValues(options);
        profileSelector.SetSelectedIndex(index, fireEvent: false);
        suppressEvents = false;
    }

    private void RebuildOptions()
    {
        options.Clear();
        options.Add(NONE_OPTION);
        options.AddRange(ProfileStore.ListProfileNames());
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
        if (!TryGetPopups(out PopupManager pm, out DifficultyController editor) ||
            editor.twoButtonPopupPrefab == null)
        {
            return;
        }

        string? selected = SelectedProfileName;
        var keys = new PopupLocalizationKeys
        {
            labelKey = selected == null
                ? "Mod profiles — save the current mod configuration as a new profile?"
                : $"Mod profile '{selected}' — save over it, or delete it?",
            positiveKey = "Save",
            negativeKey = selected == null ? "Cancel" : "Delete",
        };

        pm.ShowPopup(editor.twoButtonPopupPrefab, keys, keepLiteralData: true).Closed += result =>
        {
            if (result.closedBy == PopupClosedByAction.Positive)
            {
                ShowSavePopup();
            }
            else if (result.closedBy == PopupClosedByAction.Negative && selected != null)
            {
                ShowDeletePopup(selected);
            }
        };
    }

    private void ShowSavePopup()
    {
        if (!TryGetPopups(out PopupManager pm, out DifficultyController editor))
        {
            return;
        }

        var keys = new PopupLocalizationKeys
        {
            labelKey = "Name this mod profile",
            positiveKey = "Save",
            negativeKey = "Cancel",
        };

        Popup popup = pm.ShowPopup(editor.renamePopupPrefab, keys, keepLiteralData: true);
        TMP_InputField? input = popup.GetComponentInChildren<TMP_InputField>(true);
        input?.text = SelectedProfileName ?? NextDefaultName();

        popup.Closed += result =>
        {
            if (result.closedBy != PopupClosedByAction.Positive)
            {
                return;
            }

            string name = (result.data ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            ProfileStore.Capture(name);
            SaveAssociations.Set(CurrentSession, name);

            Refresh();
        };
    }

    private void ShowDeletePopup(string name)
    {
        if (!TryGetPopups(out PopupManager pm, out DifficultyController editor))
        {
            return;
        }

        var keys = new PopupLocalizationKeys
        {
            labelKey = $"Delete mod profile '{name}'?",
            positiveKey = "Delete",
            negativeKey = "Cancel",
        };

        pm.ShowPopup(editor.deletePopupPrefab, keys, keepLiteralData: true).Closed += result =>
        {
            if (result.closedBy != PopupClosedByAction.Positive)
            {
                return;
            }

            ProfileStore.Delete(name);

            // Other sessions still pointing at this profile clear themselves the next time they're
            // loaded; this one is in front of us, so unbind it now.
            if (SaveAssociations.Get(CurrentSession) == name)
            {
                SaveAssociations.Set(CurrentSession, null);
            }

            Refresh();
        };
    }

    private bool TryGetPopups(out PopupManager pm, out DifficultyController editor)
    {
        difficultyEditor ??= Resources.FindObjectsOfTypeAll<DifficultyController>().FirstOrDefault();
        pm = controller.FindPopupManager(ref popupManager);
        editor = difficultyEditor!;

        if (pm == null || editor == null || editor.renamePopupPrefab == null || editor.deletePopupPrefab == null)
        {
            Main.Logger.Warning("Could not resolve popup manager / prefabs for profile management");
            return false;
        }

        if (!pm.CanShowPopup())
        {
            Main.Logger.Warning("Popup manager can't show a popup right now");
            return false;
        }

        return true;
    }

    private static string NextDefaultName()
    {
        var existing = new HashSet<string>(ProfileStore.ListProfileNames());
        for (int i = 1; ; i++)
        {
            string candidate = $"Profile {i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
