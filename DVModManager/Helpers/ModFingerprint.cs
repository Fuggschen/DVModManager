using System.Text;

namespace DVModManager.Helpers;

/// <summary>One file of a mod fingerprint: its name and exact size in bytes.</summary>
public sealed record FingerprintFile(string Name, long Size);

/// <summary>
/// Identifies a mod by the name and exact size of its largest files. The Nexus
/// file index can be searched by both, which finds the upload the files came from
/// even when the mod's id or name differs from the Nexus page.
/// </summary>
/// <remarks>
/// Stored as an opaque base64url string: <c>v1</c> header, then one
/// <c>size\tname</c> line per file.
/// </remarks>
public static class ModFingerprint
{
    private const int FileCount = 3;
    private const string Header = "v1";

    // Config, text and cache files are often rewritten after install, so their size says nothing.
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".json", ".txt", ".log", ".md", ".cfg", ".cache"
    };

    /// <summary>The largest files of <paramref name="folder"/>; empty if it cannot be read.</summary>
    public static IReadOnlyList<FingerprintFile> Compute(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => !SkippedExtensions.Contains(f.Extension))
                .OrderByDescending(f => f.Length)
                .Take(FileCount)
                .Select(f => new FingerprintFile(f.Name, f.Length))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Encodes <paramref name="files"/>; null when there is nothing to encode.</summary>
    public static string? Encode(IReadOnlyList<FingerprintFile> files)
    {
        if (files.Count == 0) return null;

        var text = new StringBuilder(Header);
        foreach (var file in files)
            text.Append('\n').Append(file.Size).Append('\t').Append(file.Name);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text.ToString()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Decodes a fingerprint string; empty for null, malformed or unknown versions.</summary>
    public static IReadOnlyList<FingerprintFile> Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        string text;
        try
        {
            var base64 = value.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return [];
        }

        var lines = text.Split('\n');
        if (lines[0] != Header) return [];

        var files = new List<FingerprintFile>();
        foreach (var line in lines.Skip(1))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1) return [];
            if (!long.TryParse(line[..tab], out var size) || size < 0) return [];
            files.Add(new FingerprintFile(line[(tab + 1)..], size));
        }
        return files;
    }
}
