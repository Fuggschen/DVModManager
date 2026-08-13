using System.Collections.Generic;

namespace DVModProfiles.Profiles;

// The mod settings the game side keeps for one of the mod manager's profiles.
public class ProfileSettings
{
    public string Name = "";

    // Mod ID -> the contents of that mod's Settings.xml
    public Dictionary<string, string> Settings = [];
}
