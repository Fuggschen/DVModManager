using System.Text;
using System.Xml.Linq;

namespace DVModManager.Models;

/// <summary>
/// A game definition from UMM's <c>UnityModManagerConfig.xml</c> (the package's
/// game database). Used for two things: the Assembly-injection entry point and
/// the <c>Config.xml</c> UMM's runtime expects next to the loader DLL.
/// </summary>
public class UmmGameInfo
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string ModsDirectory { get; set; } = "Mods";
    public string ModInfo { get; set; } = "Info.json";
    public string? GameExe { get; set; }
    public string? EntryPoint { get; set; }
    public string? StartingPoint { get; set; }
    public string? UIStartingPoint { get; set; }
    public string? SessionStartPoint { get; set; }
    public string? MinimalManagerVersion { get; set; }

    /// <summary>
    /// The Derail Valley game name as UMM knows it. The game folder name and
    /// game name match in UMM's config, which is what GetStatus relies on.
    /// </summary>
    public const string DerailValleyName = "Derail Valley";

    /// <summary>
    /// Fallback definition used when the downloaded package carries no game
    /// config (e.g. an incomplete package). Values match UMM 0.33.2's entry
    /// for Derail Valley so the Assembly method stays usable.
    /// </summary>
    public static UmmGameInfo DerailValleyFallback() => new()
    {
        Name = DerailValleyName,
        Folder = DerailValleyName,
        ModsDirectory = "Mods",
        ModInfo = "Info.json",
        GameExe = "DerailValley.exe",
        EntryPoint = "[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After",
        StartingPoint = "[Assembly-CSharp.dll]DV.Globals.AssignSelfToHelpers:After",
        UIStartingPoint = "[Assembly-CSharp.dll]DV.UI.MainMenu.Start:After",
        SessionStartPoint = "[Assembly-CSharp.dll]DV.UI.Inventory.InventoryProvider.Awake:After",
        MinimalManagerVersion = "0.27.0"
    };

    /// <summary>
    /// Finds a game definition by name in a <c>UnityModManagerConfig.xml</c>
    /// document (root <c>&lt;Config&gt;</c> with <c>&lt;GameInfo&gt;</c> children).
    /// Returns null when the document is malformed or the game is absent.
    /// </summary>
    public static UmmGameInfo? ParseFromManagerConfig(string xml, string gameName)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var elem = doc.Root?
                .Elements("GameInfo")
                .FirstOrDefault(e => string.Equals(
                    (string?)e.Attribute("Name"), gameName, StringComparison.OrdinalIgnoreCase));
            if (elem == null) return null;

            string? Val(XName name) => (string?)elem.Element(name);

            return new UmmGameInfo
            {
                Name = (string?)elem.Attribute("Name") ?? gameName,
                Folder = Val("Folder") ?? gameName,
                ModsDirectory = Val("ModsDirectory") ?? "Mods",
                ModInfo = Val("ModInfo") ?? "Info.json",
                GameExe = Val("GameExe"),
                EntryPoint = Val("EntryPoint"),
                StartingPoint = Val("StartingPoint"),
                UIStartingPoint = Val("UIStartingPoint"),
                SessionStartPoint = Val("SessionStartPoint"),
                MinimalManagerVersion = Val("MinimalManagerVersion")
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the game-side <c>Config.xml</c> that UMM's runtime reads (root
    /// element <c>&lt;Config Name="..."&gt;</c> with the fields as children),
    /// e.g. to recover the entry point when uninstalling.
    /// </summary>
    public static UmmGameInfo? ParseFromGameConfig(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var elem = doc.Root;
            if (elem == null || elem.Name.LocalName != "Config") return null;

            // The game-side config carries its identity in the root's Name
            // attribute; the package's game-database XML does not.
            var name = (string?)elem.Attribute("Name");
            if (string.IsNullOrEmpty(name)) return null;

            string? Val(XName elementName) => (string?)elem.Element(elementName);

            return new UmmGameInfo
            {
                Name = name,
                Folder = Val("Folder") ?? "",
                ModsDirectory = Val("ModsDirectory") ?? "Mods",
                ModInfo = Val("ModInfo") ?? "Info.json",
                GameExe = Val("GameExe"),
                EntryPoint = Val("EntryPoint"),
                StartingPoint = Val("StartingPoint"),
                UIStartingPoint = Val("UIStartingPoint"),
                SessionStartPoint = Val("SessionStartPoint"),
                MinimalManagerVersion = Val("MinimalManagerVersion")
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes to the <c>Config.xml</c> UMM's runtime reads at game start
    /// (its <c>GameInfo</c> class, XML root "Config", next to UnityModManager.dll).
    /// </summary>
    public string ToGameConfigXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        sb.AppendLine($@"<Config Name=""{Escape(Name)}"">");
        void Line(string tag, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($"  <{tag}>{Escape(value)}</{tag}>");
        }
        Line("Folder", Folder);
        Line("ModsDirectory", ModsDirectory);
        Line("ModInfo", ModInfo);
        Line("GameExe", GameExe);
        Line("EntryPoint", EntryPoint);
        Line("StartingPoint", StartingPoint);
        Line("UIStartingPoint", UIStartingPoint);
        Line("SessionStartPoint", SessionStartPoint);
        Line("MinimalManagerVersion", MinimalManagerVersion);
        sb.Append("</Config>");
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
