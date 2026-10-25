using System.Collections;
using DV.UIFramework;
using DV.Utils;
using DVModProfiles.Profiles;
using DVModProfiles.UI;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace DVModProfiles.Multiplayer;

// Settles, on joining someone else's multiplayer game, whether to play with the host's mod
// settings.
//
// The host sends them as soon as it lets us in, just ahead of the save that sends us into the
// world, so they arrive while we're still in the main menu: early enough to ask, and to apply them
// before any mod has used its own in the world. The world load waits until that's settled.
internal static class HostProfileClient
{
    // How many pieces a host's settings may come in, at 32 KiB each
    private const int MAX_PIECES = 1024;

    // If the save gets here first, how long to give the host's settings to catch up before deciding
    // it isn't sharing any. They're sent first, so they're never more than a moment behind, unless
    // there are enough of them to still be arriving.
    private const float GRACE_SECONDS = 0.5f;
    private const float ARRIVING_SECONDS = 10f;

    // The Multiplayer mod lists each server's details in its Steam lobby, keyed by property name
    private const string LOBBY_NAME_KEY = "Name";

    private static bool connected;

    // Bumped every connection, so nothing left over from an earlier one acts on this one
    private static int connection;

    // Whether the host's settings have been dealt with this connection, one way or another
    private static bool settled;

    // Whether the world load is waiting on that
    private static bool holdingLoad;

    private static int transfer = -1;
    private static byte[]?[] pieces = [];
    private static HostProfile? received;
    private static Popup? prompt;

    // The Steam lobby we last went into, which is how a server from the server browser is joined
    private static Lobby? lobby;

    public static void Init()
    {
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
    }

    private static void OnLobbyEntered(Lobby entered) => lobby = entered;

    public static void Start()
    {
        connection++;
        connected = true;
        settled = false;
        holdingLoad = false;
        transfer = -1;
        pieces = [];
        received = null;

        // It's the host's save being played, so nothing changed while playing belongs to our profiles
        SettingsAutoCapture.ForgetSession();
    }

    public static void Stop()
    {
        if (!connected)
        {
            return;
        }

        connected = false;
        holdingLoad = false;
        pieces = [];
        received = null;

        if (prompt != null)
        {
            Popup open = prompt;
            prompt = null;
            open.RequestClose(PopupClosedByAction.Abortion, "");
        }

        BorrowedSettings.Restore();
    }

    // The host is sharing its settings, but in a form this version can't read
    public static void Unreadable()
    {
        if (!connected || settled)
        {
            return;
        }

        Main.Logger.Warning("The host is sharing its mod settings from a different version of DV Mod Profiles " +
                            "that this one can't read; playing with your own");
        Settle();
    }

    public static void Receive(int id, int index, int count, byte[] data)
    {
        if (!connected || received != null)
        {
            return;
        }

        if (count < 1 || count > MAX_PIECES || index < 0 || index >= count)
        {
            Main.Logger.Warning($"Ignoring a malformed piece of the host's mod settings ({index + 1} of {count})");
            return;
        }

        if (id != transfer || pieces.Length != count)
        {
            transfer = id;
            pieces = new byte[]?[count];
        }

        pieces[index] = data;
        if (pieces.Any(piece => piece == null))
        {
            return;
        }

        byte[] all = [.. pieces.SelectMany(piece => piece!)];
        pieces = [];

        HostProfile? host = null;
        try
        {
            host = HostProfile.Read(all);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Couldn't read the host's mod settings", ex);
        }

        if (host == null)
        {
            if (!settled)
            {
                Settle();
            }

            return;
        }

        OnReceived(host);
    }

    private static void OnReceived(HostProfile host)
    {
        received = host;
        string profileName = host.Settings.Name;
        if (string.IsNullOrEmpty(host.Settings.Name))
        {
            host.Settings.Name = "the host's mod settings";
        }

        Identify(host, out string server, out string label);
        HostProfileChoice choice = server == "" ? HostProfileChoice.Ask : HostProfiles.Get(server);
        Main.Logger.Log($"Received {host.Settings.Settings.Count} mod settings from the host of " +
                        $"'{(label == "" ? "this server" : label)}'; set to {choice}");

        if (settled)
        {
            // The world started loading without them
            if (choice is HostProfileChoice.Always or HostProfileChoice.Once)
            {
                Main.Logger.Log("The host's mod settings arrived after the world started loading; applying them " +
                                "to the running game");
                Apply(choice, host, server);
            }
            else if (choice == HostProfileChoice.Ask)
            {
                Main.Logger.Log("The host's mod settings arrived after the world started loading, too late to ask; " +
                                "playing with your own");
            }

            return;
        }

        if (choice == HostProfileChoice.Ask)
        {
            Ask(host, profileName, server, label);
            return;
        }

        Apply(choice, host, server);
        Settle();
    }

    private static void Ask(HostProfile host, string profileName, string server, string label)
    {
        // The Multiplayer mod's "connecting" popup stays up until the world loads, but its job is done
        if (!Popups.TryGetThreeButton(SingletonBehaviour<CoroutineManager>.Instance, out PopupManager pm,
                                      out Popup prefab, takeOver: true))
        {
            Main.Logger.Warning("The host is sharing its mod settings, but there's no popup to ask with; playing " +
                                "with your own this time");
            Settle();
            return;
        }

        string who = label == "" ? "The host" : $"The host of '{label}'";
        string what = string.IsNullOrEmpty(profileName) ? "its mod settings" : $"its mod settings (profile '{profileName}')";
        string text = $"{who} is sharing {what}. Play with them here?\n\n{HostProfilePrompt.CHOICES}\n\n" +
                      "Your own settings come back when you leave." +
                      (server == "" ? " This server can't be told apart from others, so this won't be remembered." : "");

        int forConnection = connection;
        prompt = HostProfilePrompt.Show(pm, prefab, text, choice =>
        {
            if (forConnection != connection || !connected)
            {
                return;
            }

            prompt = null;
            if (server != "")
            {
                HostProfiles.Set(server, label, choice);
            }

            Apply(choice, host, server);
            Settle();
        });
    }

    private static void Apply(HostProfileChoice choice, HostProfile host, string server)
    {
        switch (choice)
        {
            case HostProfileChoice.Always:
                BorrowedSettings.Use(host.Settings);
                break;

            case HostProfileChoice.Once:
                ProfileSettings? copy = server == "" ? null : HostProfiles.LoadCopy(server);
                if (copy == null)
                {
                    copy = host.Settings;
                    if (server != "")
                    {
                        HostProfiles.SaveCopy(server, copy);
                    }
                }

                BorrowedSettings.Use(copy);
                break;

            default:
                Main.Logger.Log("Playing with your own mod settings rather than the host's");
                break;
        }
    }

    private static void Settle()
    {
        settled = true;
        if (holdingLoad)
        {
            holdingLoad = false;
            SceneSwitcher.SwitchToScene(DVScenes.Game);
        }
    }

    // Which server this is: its name in the server browser when it was joined through its lobby,
    // otherwise its host's Steam ID. Empty when it can't be told apart at all.
    private static void Identify(HostProfile host, out string server, out string label)
    {
        try
        {
            // Only a lobby's members can see who owns it, so this also rules out lobbies since left
            if (lobby is Lobby joined && host.HostId != "" && joined.Owner.Id.Value.ToString() == host.HostId)
            {
                string name = joined.GetData(LOBBY_NAME_KEY);
                if (!string.IsNullOrEmpty(name))
                {
                    server = name;
                    label = name;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Couldn't read the server's Steam lobby", ex);
        }

        server = host.HostId == "" ? "" : "steam:" + host.HostId;
        label = host.HostName == "" ? "" : $"{host.HostName}'s game";
    }

    // Starting the world is what the Multiplayer mod does with the host's save
    [HarmonyPatch(typeof(SceneSwitcher), nameof(SceneSwitcher.SwitchToScene), typeof(DVScenes))]
    private static class SwitchToScenePatch
    {
        private static bool Prefix(DVScenes enumVal) => !HoldLoad(enumVal);
    }

    private static bool HoldLoad(DVScenes scene)
    {
        if (!connected || settled || scene != DVScenes.Game)
        {
            return false;
        }

        if (!holdingLoad)
        {
            holdingLoad = true;

            // With the host's settings in hand it's only waiting on an answer
            if (received == null)
            {
                SingletonBehaviour<CoroutineManager>.Instance.Run(GiveUpWaiting(connection));
            }
        }

        return true;
    }

    private static IEnumerator GiveUpWaiting(int forConnection)
    {
        float started = Time.realtimeSinceStartup;
        while (connected && forConnection == connection && received == null && !settled)
        {
            float waited = Time.realtimeSinceStartup - started;
            if (waited >= ARRIVING_SECONDS || (waited >= GRACE_SECONDS && pieces.Length == 0))
            {
                Main.Logger.Log("The host isn't sharing its mod settings; playing with your own");
                Settle();
                yield break;
            }

            yield return null;
        }
    }
}
