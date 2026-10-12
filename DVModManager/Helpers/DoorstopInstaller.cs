using Microsoft.Extensions.Logging;

namespace DVModManager.Helpers;

/// <summary>
/// Installs/uninstalls the Doorstop proxy the same way Unity Mod Manager does:
/// copy winhttp_x64/x86.dll to game/winhttp.dll (selected by the game
/// exe's PE arch) and write doorstop_config.ini pointing at the loader DLL.
/// </summary>
public static class DoorstopInstaller
{
    public const string DoorstopFileName = "winhttp.dll";
    public const string ConfigFileName = "doorstop_config.ini";

    public static string GetDoorstopPath(string gamePath) => Path.Combine(gamePath, DoorstopFileName);
    public static string GetConfigPath(string gamePath) => Path.Combine(gamePath, ConfigFileName);

    public static bool IsInstalled(string gamePath) =>
        File.Exists(GetDoorstopPath(gamePath)) && File.Exists(GetConfigPath(gamePath));

    /// <summary>
    /// Installs the Doorstop proxy from <paramref name="payloadDir"/>.
    /// <paramref name="targetAssemblyRelative"/> is the loader DLL path relative
    /// to the game folder (UMM writes it backslash-separated).
    /// Pass <paramref name="is64Bit"/> to skip PE detection (tests, fallbacks);
    /// when detection fails the 64-bit proxy is used (Derail Valley is x64).
    /// </summary>
    public static void Install(string gamePath, string payloadDir, string targetAssemblyRelative,
        bool? is64Bit = null, ILogger? logger = null)
    {
        is64Bit ??= DetectGameBitness(gamePath, logger) ?? true;

        var proxyName = is64Bit == true ? "winhttp_x64.dll" : "winhttp_x86.dll";
        var proxySource = Path.Combine(payloadDir, proxyName);
        if (!File.Exists(proxySource))
            throw new InvalidOperationException(
                $"UMM package does not contain the Doorstop proxy '{proxyName}'");

        File.Copy(proxySource, GetDoorstopPath(gamePath), overwrite: true);

        // Same format UMM writes; Doorstop reads target_assembly relative to
        // the game folder and expects Windows separators for this game.
        var target = targetAssemblyRelative.Replace('/', '\\');
        File.WriteAllText(GetConfigPath(gamePath),
            "[General]" + Environment.NewLine +
            "enabled = true" + Environment.NewLine +
            "target_assembly = " + target);
    }

    public static void Remove(string gamePath)
    {
        var doorstop = GetDoorstopPath(gamePath);
        var config = GetConfigPath(gamePath);
        if (File.Exists(doorstop)) File.Delete(doorstop);
        if (File.Exists(config)) File.Delete(config);
    }

    /// <summary>Detects the game exe bitness from its PE header (like UMM's WindowsFileIs64Bit).</summary>
    public static bool? DetectGameBitness(string gamePath, ILogger? logger = null)
    {
        foreach (var exeName in new[] { "DerailValley.exe", "UnityPlayer.dll" })
        {
            var path = Path.Combine(gamePath, exeName);
            if (!File.Exists(path)) continue;
            var result = ReadPeIs64Bit(path);
            if (result != null) return result;
            logger?.LogWarning("Could not read PE header of {Path}", path);
        }
        return null;
    }

    /// <summary>
    /// Reads the machine type from a PE file: 0x8664/0x200 → 64-bit,
    /// 0x14c → 32-bit. Returns null for unreadable/non-PE files.
    /// </summary>
    public static bool? ReadPeIs64Bit(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs);

            fs.Seek(0x3c, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset > fs.Length - 6) return null;

            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return null; // "PE\0\0"

            var machine = br.ReadUInt16();
            return machine switch
            {
                0x8664 or 0x200 or 0xAA64 => true,   // AMD64 / IA64 / ARM64
                0x14c or 0x1c0 or 0x1c4 => false,    // I386 / ARM / ARMv7
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
