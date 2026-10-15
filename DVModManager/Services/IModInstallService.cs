using DVModManager.Models;

namespace DVModManager.Services;

public interface IModInstallService
{
    Task<bool> ActivateModAsync(ModInfo mod, string gamePath, CancellationToken ct = default);
    Task<bool> DeactivateModAsync(ModInfo mod, string gamePath, CancellationToken ct = default);
    /// <summary>
    /// Installs every mod in the archive (root or one folder deep). When it holds more than one,
    /// <paramref name="confirmMultiple"/> is asked first; false installs nothing. Empty on failure.
    /// </summary>
    Task<IReadOnlyList<ModInfo>> InstallFromArchiveAsync(string archivePath, string gamePath, string storagePath, bool activate = true, Func<IReadOnlyList<ModInfo>, Task<bool>>? confirmMultiple = null, CancellationToken ct = default);
    Task<ModInfo?> InstallFromFolderAsync(string modFolderPath, string gamePath, string storagePath, bool activate = false, CancellationToken ct = default);
    Task<bool> UninstallModAsync(ModInfo mod, string gamePath, string storagePath, bool hardDelete = false, CancellationToken ct = default);
    Task<bool> RollbackToVersionAsync(string modId, string version, string gamePath, string storagePath, CancellationToken ct = default);
    /// <summary>
    /// Updates <paramref name="mod"/> from its release zip. Other mods bundled in the zip are
    /// installed too, unless <paramref name="confirmExtraMods"/> returns false.
    /// </summary>
    Task<bool> UpdateModAsync(ModInfo mod, ModUpdateInfo update, string gamePath, string storagePath, IProgress<double>? progress = null, Func<IReadOnlyList<ModInfo>, Task<bool>>? confirmExtraMods = null, CancellationToken ct = default);
    Task<IReadOnlyList<ModInfo>> DownloadAndInstallFromUrlAsync(string downloadUrl, string gamePath, string storagePath, IProgress<double>? progress = null, CancellationToken ct = default);
}
