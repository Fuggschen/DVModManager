using DVModManager.Models;

namespace DVModManager.Services;

public interface IModInstallService
{
    Task<bool> ActivateModAsync(ModInfo mod, string gamePath, CancellationToken ct = default);
    Task<bool> DeactivateModAsync(ModInfo mod, string gamePath, CancellationToken ct = default);
    Task<ModInfo?> InstallFromArchiveAsync(string archivePath, string gamePath, string storagePath, bool activate = true, CancellationToken ct = default);
    Task<ModInfo?> InstallFromFolderAsync(string modFolderPath, string gamePath, string storagePath, bool activate = false, CancellationToken ct = default);
    Task<bool> UninstallModAsync(ModInfo mod, string gamePath, string storagePath, bool hardDelete = false, CancellationToken ct = default);
    Task<bool> RollbackToVersionAsync(string modId, string version, string gamePath, string storagePath, CancellationToken ct = default);
    Task<bool> UpdateModAsync(ModInfo mod, ModUpdateInfo update, string gamePath, string storagePath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<ModInfo?> DownloadAndInstallFromUrlAsync(string downloadUrl, string gamePath, string storagePath, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<string> BackupModsFolderAsync(string gamePath, string storagePath);
}
