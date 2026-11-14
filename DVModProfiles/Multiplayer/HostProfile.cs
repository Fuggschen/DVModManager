using System.Text;
using DVModProfiles.Profiles;
using Newtonsoft.Json;
using Steamworks;
using UnityModManagerNet;

namespace DVModProfiles.Multiplayer;

// What a host sends the players joining its game: the mod settings it's playing with, and who it is.
internal sealed class HostProfile
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // The host's Steam ID, which also owns the host's lobby in the server browser
    public string HostId = "";

    // The host's Steam name, to call a server joined without the server browser by
    public string HostName = "";

    public ProfileSettings Settings = new();

    // MP settings can leak credentials for others servers and should never sync (nor is there a need to).
    private static readonly HashSet<string> NeverShared = [MultiplayerSupport.MULTIPLAYER_MOD_ID];

    public static bool CanShare(string modId) => !NeverShared.Contains(modId);

    public static bool Shares(string modId) =>
        CanShare(modId) && !Main.Config.notSharedWithMultiplayerClients.Contains(modId);

    public static List<UnityModManager.ModEntry> ShareableMods() =>
    [
        .. UnityModManager.modEntries
            .Where(mod => mod.Info.Id != Main.ModEntry.Info.Id && CanShare(mod.Info.Id) && SettingsStore.HasSettings(mod))
            .OrderBy(mod => mod.Info.DisplayName, StringComparer.OrdinalIgnoreCase),
    ];

    public static List<string> DropNeverShared(ProfileSettings settings)
    {
        List<string> dropped = [.. settings.Settings.Keys.Where(id => !CanShare(id))];
        foreach (string id in dropped)
        {
            settings.Settings.Remove(id);
        }

        return dropped;
    }

    public static byte[] Current()
    {
        ProfileSettings settings =
            SettingsStore.Snapshot(SettingsAutoCapture.LoadedProfile ?? ManagerProfiles.ActiveProfileName ?? "");
        foreach (string id in settings.Settings.Keys.Where(id => !Shares(id)).ToList())
        {
            settings.Settings.Remove(id);
        }

        var profile = new HostProfile { Settings = settings };

        if (SteamClient.IsValid)
        {
            profile.HostId = SteamClient.SteamId.Value.ToString();
            profile.HostName = SteamClient.Name;
        }

        return Utf8.GetBytes(JsonConvert.SerializeObject(profile));
    }

    public static HostProfile? Read(byte[] data) => JsonConvert.DeserializeObject<HostProfile>(Utf8.GetString(data));
}
