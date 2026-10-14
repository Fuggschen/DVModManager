namespace DVModManager.Services;

public enum StorageMoveCheck
{
    /// <summary>The target can receive the storage data.</summary>
    Ok,
    /// <summary>Old and new path are the same folder; nothing to move.</summary>
    Unchanged,
    /// <summary>The target is not an absolute path.</summary>
    InvalidPath,
    /// <summary>The target already contains files.</summary>
    NotEmpty,
    /// <summary>The target lies inside one of the folders that would be moved.</summary>
    InsideMovedData,
}

public enum StorageMoveResult
{
    Moved,
    /// <summary>Everything was copied and the setting can switch, but some old files could not be removed.</summary>
    MovedWithLeftovers,
    /// <summary>Copying failed; the partial copy was removed and the old storage is untouched.</summary>
    Failed,
}

public interface IStorageMoveService
{
    /// <summary>Checks whether the storage data can be moved from <paramref name="oldPath"/> to <paramref name="newPath"/>.</summary>
    StorageMoveCheck Check(string oldPath, string newPath);

    /// <summary>
    /// Copies the storage data (versions, downloads, profiles) to <paramref name="newPath"/>,
    /// then removes it from <paramref name="oldPath"/>. The old data is only deleted once
    /// the whole copy succeeded.
    /// </summary>
    Task<StorageMoveResult> MoveAsync(string oldPath, string newPath, IProgress<double>? progress = null);
}
