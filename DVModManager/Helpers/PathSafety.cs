namespace DVModManager.Helpers;

/// <summary>
/// Validates mod IDs coming from untrusted sources (zip contents / Info.json) and
/// ensures derived paths stay inside their intended root directory.
/// Protects against path traversal via malicious <c>Id</c> values such as
/// <c>""</c>, <c>"..\\..\\evil"</c> or <c>"/etc/foo"</c>.
/// </summary>
public static class PathSafety
{
    /// <summary>Characters that are never allowed inside a mod id on any OS.</summary>
    private static readonly char[] AlwaysInvalid = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>
    /// Returns <c>true</c> if <paramref name="id"/> is a single, safe path segment:
    /// non-empty, no leading/trailing whitespace, no directory separators, no
    /// <c>.</c>/<c>..</c> segments and no invalid file-name characters.
    /// </summary>
    public static bool IsValidModId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id != id.Trim()) return false;
        if (id == "." || id == "..") return false;
        if (id.IndexOfAny(AlwaysInvalid) >= 0) return false;
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (id.IndexOf('\0') >= 0) return false;
        return true;
    }

    /// <summary>
    /// Combines <paramref name="root"/> with a validated single-segment <paramref name="id"/>.
    /// Throws <see cref="InvalidOperationException"/> when the id is unsafe or when the
    /// combined path would escape <paramref name="root"/>.
    /// </summary>
    public static string SafeCombine(string root, string? id)
    {
        if (!IsValidModId(id))
            throw new InvalidOperationException($"Rejected unsafe mod id '{id}'.");

        var combined = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), id!));
        if (!IsStrictlyUnder(root, combined))
            throw new InvalidOperationException($"Path '{combined}' escapes root '{root}'.");
        return combined;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="candidate"/> resolves to a path
    /// strictly inside <paramref name="root"/> (the root itself is not considered "under").
    /// </summary>
    public static bool IsStrictlyUnder(string root, string candidate)
    {
        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candFull = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (candFull.Length <= rootFull.Length) return false;
        if (!candFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;

        var separator = candFull[rootFull.Length];
        return separator == Path.DirectorySeparatorChar
            || separator == Path.AltDirectorySeparatorChar;
    }
}
