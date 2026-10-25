using System.Collections.Generic;
using DVModProfiles.Profiles;
using UnityModManagerNet;

namespace DVModProfiles;

public class Settings : UnityModManager.ModSettings
{
    public bool useSteamCloud = true;

    // Send our mod settings to the players who join a multiplayer game we're hosting
    public bool shareWithMultiplayerClients = true;

    public List<HostProfilePreference> hostProfilePreferences = [];
}
