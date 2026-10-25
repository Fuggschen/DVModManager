using System.Reflection;
using DV.UIFramework;
using DVModProfiles.Profiles;
using HarmonyLib;
using UnityEngine;

namespace DVModProfiles.UI;

// A row in the Multiplayer mod's server browser for choosing what happens to the selected
// server's host mod settings when joining it.
public class HostProfileBrowserWidget : MonoBehaviour
{
    // How the server browser dims the labels in its details
    private const string LABEL_ALPHA = "<alpha=#50>";
    private const string VALUE_ALPHA = "<alpha=#FF>";

    private MonoBehaviour pane = null!;
    private FieldInfo selectedServer = null!;
    private CollapsibleElement row = null!;

    public bool Build(MonoBehaviour owner)
    {
        pane = owner;
        Type type = owner.GetType();

        FieldInfo? selected = AccessTools.Field(type, "selectedServer");
        Transform? details = AccessTools.Field(type, "detailsContent")?.GetValue(owner) as Transform;
        CollapsibleElement? template = AccessTools.Field(type, "elementExtraMods")?.GetValue(owner) as CollapsibleElement;
        if (selected == null || details == null || template == null)
        {
            return false;
        }

        selectedServer = selected;

        // Cloned from the browser's own mod list heading to look the part
        row = Instantiate(template, details);
        row.name = "[Host mod settings]";
        row.isLeaf = true;
        row.CollapsibleElementClicked += _ => OnClicked();

        // Under the server's details, above its mods
        row.transform.SetSiblingIndex(1);
        row.gameObject.SetActive(false);

        Main.Logger.Log("Injected host mod settings row into the server browser");
        return true;
    }

    public void Refresh()
    {
        if (row == null)
        {
            return;
        }

        string? server = SelectedServer;
        row.gameObject.SetActive(server != null);
        if (server != null)
        {
            row.SetText($"{LABEL_ALPHA}Host mod settings:{VALUE_ALPHA} {HostProfiles.Describe(server)}");
        }
    }

    private string? SelectedServer
    {
        get
        {
            object? selected = selectedServer.GetValue(pane);
            string? name = selected == null ? null : Traverse.Create(selected).Property("Name").GetValue<string>();
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }

    private void OnClicked()
    {
        string? server = SelectedServer;
        if (server == null)
        {
            return;
        }

        if (!Popups.TryGetThreeButton(this, out PopupManager pm, out Popup prefab))
        {
            Main.Logger.Warning("Could not resolve a popup to choose the host mod settings with");
            return;
        }

        HostProfileChoice current = HostProfiles.Get(server);
        string text = $"Mod settings from the host of '{server}': {HostProfiles.Describe(server, current)}.\n\n" +
                      $"{HostProfilePrompt.CHOICES}" +
                      (current == HostProfileChoice.Once
                          ? "\n\nTo take a fresh copy, choose another option and then Once again."
                          : "");

        HostProfilePrompt.Show(pm, prefab, text, choice =>
        {
            if (choice != current)
            {
                HostProfiles.Set(server, server, choice);
            }

            Refresh();
        });
    }
}
