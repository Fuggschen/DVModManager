using DVModProfiles.Profiles;
using UnityModManagerNet;

namespace DVModProfiles;

public class Settings : UnityModManager.ModSettings
{
    public bool useSteamCloud = true;

    // Send our mod settings to the players who join a multiplayer game we're hosting
    public bool shareWithMultiplayerClients = true;

    // The mods whose settings we keep to ourselves when sharing, by mod ID
    public List<string> notSharedWithMultiplayerClients = [];

    public List<HostProfilePreference> hostProfilePreferences = [];
}
