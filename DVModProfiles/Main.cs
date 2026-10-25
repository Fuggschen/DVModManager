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

    private static void OnSaveGUI(UnityModManager.ModEntry modEntry) => Config.Save(modEntry);
}
