using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace ModProfiles;

public static class Main
{
    private static bool Load(UnityModManager.ModEntry modEntry)
    {
        Harmony? harmony = null;

        try
        {
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Other plugin startup logic
        }
        catch (Exception ex)
        {
            modEntry.Logger.LogException($"Failed to load {modEntry.Info.DisplayName}:", ex);
            harmony?.UnpatchAll(modEntry.Info.Id);
            return false;
        }

        return true;
    }
}