using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class StorageMoveService : IStorageMoveService
{
    /// <summary>
    /// The folders that live under <see cref="PublicSettings.StoragePath"/>. Settings and
    /// logs always stay in <see cref="ManagerStorage.ConfigDirectory"/> and are not moved.
    /// </summary>
    public static readonly string[] MovedFolders = ["versions", "downloads", ManagerStorage.ProfilesDirectoryName];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ILogger<StorageMoveService> _logger;
    private readonly string _configDirectory;

    /// <param name="configDirectory">Defaults to <see cref="ManagerStorage.ConfigDirectory"/>; tests pass a temp folder.</param>
    public StorageMoveService(ILogger<StorageMoveService> logger, string? configDirectory = null)
    {
        _logger = logger;
        _configDirectory = configDirectory ?? ManagerStorage.ConfigDirectory;
    }

    public StorageMoveCheck Check(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath) || !Path.IsPathRooted(newPath))
            return StorageMoveCheck.InvalidPath;

        string oldFull, newFull;
        try
        {
            oldFull = Normalize(oldPath);
            newFull = Normalize(newPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return StorageMoveCheck.InvalidPath;
        }

        if (string.Equals(oldFull, newFull, PathComparison))
            return StorageMoveCheck.Unchanged;

        if (MovedFolders.Any(f => IsSameOrInside(newFull, Path.Combine(oldFull, f))))
            return StorageMoveCheck.InsideMovedData;

        if (!Directory.Exists(newFull))
            return StorageMoveCheck.Ok;

        // The config folder always holds settings.json and logs, so it can never be
        // empty. Moving back there is fine as long as none of the moved folders exist.
        if (string.Equals(newFull, Normalize(_configDirectory), PathComparison))
            return MovedFolders.Any(f => Directory.Exists(Path.Combine(newFull, f)))
                ? StorageMoveCheck.NotEmpty
                : StorageMoveCheck.Ok;

        return Directory.EnumerateFileSystemEntries(newFull).Any()
            ? StorageMoveCheck.NotEmpty
            : StorageMoveCheck.Ok;
    }

    public Task<StorageMoveResult> MoveAsync(string oldPath, string newPath, IProgress<double>? progress = null) =>
        Task.Run(() => Move(Normalize(oldPath), Normalize(newPath), progress));

    private StorageMoveResult Move(string oldPath, string newPath, IProgress<double>? progress)
    {
        var sources = MovedFolders
            .Select(f => (Source: Path.Combine(oldPath, f), Target: Path.Combine(newPath, f)))
            .Where(x => Directory.Exists(x.Source))
            .ToList();

        // Copy first and delete afterwards: copying works across drives, and a
        // failure halfway leaves the old storage complete and still in use
        var created = new List<string>();
        try
        {
            Directory.CreateDirectory(newPath);

            long totalBytes = sources.Sum(x => DirectorySize(x.Source));
            long copiedBytes = 0;
            progress?.Report(0);

            foreach (var (source, target) in sources)
            {
                created.Add(target);
                CopyDirectory(source, target, bytes =>
                {
                    copiedBytes += bytes;
                    if (totalBytes > 0) progress?.Report((double)copiedBytes / totalBytes);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copying storage from {Old} to {New} failed; rolling back", oldPath, newPath);
            foreach (var dir in created)
                TryDelete(dir);
            return StorageMoveResult.Failed;
        }

        progress?.Report(1);

        var leftovers = false;
        foreach (var (source, _) in sources)
            leftovers |= !TryDelete(source);

        _logger.LogInformation("Moved storage from {Old} to {New}", oldPath, newPath);
        return leftovers ? StorageMoveResult.MovedWithLeftovers : StorageMoveResult.Moved;
    }

    private static void CopyDirectory(string source, string target, Action<long> onFileCopied)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: false);
            onFileCopied(new FileInfo(destination).Length);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), onFileCopied);
    }

    private static long DirectorySize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    private bool TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete {Dir}", dir);
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, PathComparison) ||
        path.StartsWith(folder + Path.DirectorySeparatorChar, PathComparison);
}
