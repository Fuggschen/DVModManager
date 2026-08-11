using System;
using System.Reflection;
using HarmonyLib;
using ModProfiles.Profiles;
using UnityEngine;
using UnityModManagerNet;

namespace ModProfiles;

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

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
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
        bool useCloud = GUILayout.Toggle(Config.useSteamCloud, "  Store profiles in Steam Cloud (when available)");
        if (useCloud != Config.useSteamCloud)
        {
            ProfileStorage.SetUseSteamCloud(useCloud);
        }
    }

    private static void OnSaveGUI(UnityModManager.ModEntry modEntry) => Config.Save(modEntry);
}
