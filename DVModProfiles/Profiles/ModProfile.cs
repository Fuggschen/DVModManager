using System.Collections.Generic;

namespace DVModProfiles.Profiles;

public class ModProfile
{
    public string Name = "";

    public Dictionary<string, string> Settings = [];

    public List<string> EnabledMods = [];

    public Dictionary<string, string> DisplayNames = [];
}
