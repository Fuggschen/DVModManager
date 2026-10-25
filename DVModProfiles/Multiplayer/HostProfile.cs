using System.Text;
using DVModProfiles.Profiles;
using Newtonsoft.Json;
using Steamworks;

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

    // The mod settings we're playing with right now, as the host
    public static byte[] Current()
    {
        var profile = new HostProfile
        {
            Settings = SettingsStore.Snapshot(SettingsAutoCapture.LoadedProfile ?? ManagerProfiles.ActiveProfileName ?? ""),
        };

        if (SteamClient.IsValid)
        {
            profile.HostId = SteamClient.SteamId.Value.ToString();
            profile.HostName = SteamClient.Name;
        }

        return Utf8.GetBytes(JsonConvert.SerializeObject(profile));
    }

    public static HostProfile? Read(byte[] data) => JsonConvert.DeserializeObject<HostProfile>(Utf8.GetString(data));
}
