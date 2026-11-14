using System.Reflection;
using HarmonyLib;
using DVModProfiles.Multiplayer;
using DVModProfiles.Profiles;
using UnityEngine;
using UnityModManagerNet;

namespace DVModProfiles;

public static class Main
{
    public static UnityModManager.ModEntry ModEntry { get; private set; } = null!;

    internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; } = null!;

    public static Settings Config { get; private set; } = new();

    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        Logger = modEntry.Logger;
        Harmony? harmony = null;

        try
        {
            ModEntry = modEntry;
            Config = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            BorrowedSettings.RecoverAfterExit();

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            SettingsAutoCapture.Init();
            MultiplayerSupport.TryInit();
        }
        catch (Exception ex)
        {
            modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
            harmony?.UnpatchAll(modEntry.Info.Id);
            return false;
        }

        return true;
    }

    private static void OnGUI(UnityModManager.ModEntry modEntry)
    {
        bool useCloud = GUILayout.Toggle(Config.useSteamCloud, "  Sync profiles and mod settings through Steam Cloud (when available)");
        if (useCloud != Config.useSteamCloud)
        {
            ProfileStorage.SetUseSteamCloud(useCloud);
            ProfileSync.Reset();
        }

        string? managerDir = ManagerProfiles.ConfigDir;
        GUILayout.Label(managerDir == null
            ? "Mod manager storage not found — install DV Mod Manager to pick a profile for your saves."
            : $"Reading profiles from the mod manager at: {managerDir}");

        Config.shareWithMultiplayerClients = GUILayout.Toggle(Config.shareWithMultiplayerClients,
            "  Share my mod settings with the players who join multiplayer games I host");
        if (Config.shareWithMultiplayerClients)
        {
            DrawSharedMods();
        }

        List<HostProfilePreference> servers = [.. HostProfiles.All];
        if (servers.Count == 0)
        {
            return;
        }

        GUILayout.Label("Host mod settings for multiplayer servers (also changeable from the server browser):");
        foreach (HostProfilePreference server in servers)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"    {server.Label}: {HostProfiles.Describe(server.Server, server.Choice)}");
            if (GUILayout.Button("Forget", GUILayout.ExpandWidth(false)))
            {
                HostProfiles.Forget(server.Server);
            }

            GUILayout.EndHorizontal();
        }
    }

    private static List<UnityModManager.ModEntry>? shareableMods;

    private static void DrawSharedMods()
    {
        List<string> notShared = Config.notSharedWithMultiplayerClients;
        int held = notShared.Count(id => UnityModManager.FindMod(id) != null);
        string which = held == 0 ? "all of them" : $"all but {held}";

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        bool open = GUILayout.Toggle(shareableMods != null, $"  Choose which mods' settings to share ({which})");
        GUILayout.EndHorizontal();

        if (!open)
        {
            shareableMods = null;
            return;
        }

        shareableMods ??= HostProfile.ShareableMods();
        foreach (UnityModManager.ModEntry mod in shareableMods)
        {
            string id = mod.Info.Id;
            bool shared = !notShared.Contains(id);

            GUILayout.BeginHorizontal();
            GUILayout.Space(40);
            if (GUILayout.Toggle(shared, $"  {mod.Info.DisplayName}") != shared)
            {
                if (shared)
                {
                    notShared.Add(id);
                }
                else
                {
                    notShared.Remove(id);
                }
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Space(40);
        GUILayout.Label("The Multiplayer mod's own settings are never shared.");
        GUILayout.EndHorizontal();
    }

    private static void OnSaveGUI(UnityModManager.ModEntry modEntry) => Config.Save(modEntry);
}
