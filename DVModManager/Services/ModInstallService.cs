using System.IO.Compression;
using System.Text.Json;
using DVModManager.Helpers;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class ModInstallService : IModInstallService
{
    private readonly IVersionCacheService _versionCache;
    private readonly ISettingsService _settings;
    private readonly ILogger<ModInstallService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public ModInstallService(IVersionCacheService versionCache, ISettingsService settings, ILogger<ModInstallService> logger)
    {
        _versionCache = versionCache;
        _settings = settings;
        _logger = logger;
    }

    private bool ShouldArchive => _settings.Settings.EnableVersionArchiving;

    // ── Activate: Mods.inactive/{folder} → Mods/{folder} ───────────────────────────────────────

    public async Task<bool> ActivateModAsync(ModInfo mod, string gamePath, CancellationToken ct = default)
    {
        // Use the stored FolderPath so we handle mods whose folder name ≠ mod.Id
        var inactivePath = mod.FolderPath;
        var folderName   = Path.GetFileName(inactivePath);
        var activePath   = Path.Combine(gamePath, "Mods", folderName);

        // Fallback: if FolderPath is absent or wrong, try both conventions
        if (!Directory.Exists(inactivePath))
        {
            var byId     = Path.Combine(gamePath, "Mods.inactive", mod.Id);
            var byFolder = Path.Combine(gamePath, "Mods.inactive", folderName);
            inactivePath = Directory.Exists(byId) ? byId
                         : Directory.Exists(byFolder) ? byFolder
                         : null!;
            if (inactivePath == null)
            {
                _logger.LogWarning("Cannot activate {Id}: folder not found in Mods.inactive", mod.Id);
                return false;
            }
            folderName = Path.GetFileName(inactivePath);
            activePath = Path.Combine(gamePath, "Mods", folderName);
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(gamePath, "Mods"));
            await Task.Run(() => MoveDirectory(inactivePath, activePath), ct);
            mod.FolderPath = activePath;
            mod.IsActive   = true;
            mod.State      = ModState.Active;
            _logger.LogInformation("Activated mod {Id} (folder: {Folder})", mod.Id, folderName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate mod {Id}", mod.Id);
            return false;
        }
    }

    // ── Deactivate: Mods/{folder} → Mods.inactive/{folder} ─────────────────────────────────────

    public async Task<bool> DeactivateModAsync(ModInfo mod, string gamePath, CancellationToken ct = default)
    {
        // Use the stored FolderPath so we handle mods whose folder name ≠ mod.Id
        var activePath   = mod.FolderPath;
        var folderName   = Path.GetFileName(activePath);
        var inactivePath = Path.Combine(gamePath, "Mods.inactive", folderName);

        // Fallback: if FolderPath is absent or wrong, try both conventions
        if (!Directory.Exists(activePath))
        {
            var byId     = Path.Combine(gamePath, "Mods", mod.Id);
            var byFolder = Path.Combine(gamePath, "Mods", folderName);
            activePath = Directory.Exists(byId) ? byId
                       : Directory.Exists(byFolder) ? byFolder
                       : null!;
            if (activePath == null)
            {
                _logger.LogWarning("Cannot deactivate {Id}: folder not found in Mods", mod.Id);
                return false;
            }
            folderName   = Path.GetFileName(activePath);
            inactivePath = Path.Combine(gamePath, "Mods.inactive", folderName);
        }

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.Combine(gamePath, "Mods.inactive"));
                if (Directory.Exists(inactivePath)) Directory.Delete(inactivePath, true);
                MoveDirectory(activePath, inactivePath);
            }, ct);

            mod.FolderPath = inactivePath;
            mod.IsActive   = false;
            mod.State      = ModState.Inactive;
            _logger.LogInformation("Deactivated mod {Id} (folder: {Folder})", mod.Id, folderName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate mod {Id}", mod.Id);
            return false;
        }
    }

    // ── Install: extract archive → Mods/ or Mods.inactive/ ───────────────────

    public async Task<ModInfo?> InstallFromArchiveAsync(
        string archivePath, string gamePath, string storagePath,
        bool activate = true, CancellationToken ct = default)
    {
        try
        {
            // Extract to a temp dir first to discover the mod id
            var tempDir = Path.Combine(Path.GetTempPath(), "dvmm_" + Guid.NewGuid());
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, tempDir, overwriteFiles: true), ct);

            // Determine the mod root: either the extracted folder itself or a single subdirectory
            var modRoot = FindModRoot(tempDir);
            if (modRoot == null)
            {
                Directory.Delete(tempDir, true);
                _logger.LogError("No Info.json found in archive {Archive}", archivePath);
                return null;
            }

            var infoPath = InfoJsonLocator.Locate(modRoot);
            if (infoPath == null)
            {
                Directory.Delete(tempDir, true);
                _logger.LogError("No Info.json found in archive {Archive}", archivePath);
                return null;
            }
            var json = await File.ReadAllTextAsync(infoPath, ct);
            var modInfo = JsonSerializer.Deserialize<ModInfo>(json, JsonOptions);
            if (modInfo == null) { Directory.Delete(tempDir, true); return null; }

            var targetDir = activate
                ? Path.Combine(gamePath, "Mods", modInfo.Id)
                : Path.Combine(gamePath, "Mods.inactive", modInfo.Id);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            // Archive the existing installation before overwriting
            if (Directory.Exists(targetDir))
            {
                var existing = new ModInfo
                {
                    Id = modInfo.Id,
                    Version = modInfo.Version,  // use new version as label; existing may differ
                    FolderPath = targetDir
                };
                // Re-read the existing Info.json if available to get accurate version
                var existingInfo = InfoJsonLocator.Locate(targetDir);
                if (existingInfo != null)
                {
                    try
                    {
                        var existingMod = JsonSerializer.Deserialize<ModInfo>(
                            await File.ReadAllTextAsync(existingInfo, ct), JsonOptions);
                        if (existingMod != null)
                        {
                            existingMod.FolderPath = targetDir;
                            if (ShouldArchive)
                                await _versionCache.ArchiveCurrentVersionAsync(existingMod, storagePath);
                        }
                    }
                    catch { /* archive failure is non-fatal */ }
                }
                Directory.Delete(targetDir, true);
            }

            await Task.Run(() =>
            {
                MoveDirectory(modRoot, targetDir);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }, ct);

            modInfo.FolderPath = targetDir;
            modInfo.IsActive = activate;
            modInfo.State = activate ? ModState.Active : ModState.Inactive;

            // Copy archive to downloads cache
            var downloadsDir = Path.Combine(storagePath, "downloads", modInfo.Id);
            Directory.CreateDirectory(downloadsDir);
            var destArchive = Path.Combine(downloadsDir, Path.GetFileName(archivePath));
            if (!File.Exists(destArchive)) File.Copy(archivePath, destArchive);

            _logger.LogInformation("Installed mod {Id} v{Version}", modInfo.Id, modInfo.Version);
            return modInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install from archive {Archive}", archivePath);
            return null;
        }
    }

    // ── Install from folder ─────────────────────────────────────────────────────────────

    public async Task<ModInfo?> InstallFromFolderAsync(
        string modFolderPath, string gamePath, string storagePath,
        bool activate = false, CancellationToken ct = default)
    {
        try
        {
            var infoPath = InfoJsonLocator.Locate(modFolderPath);
            if (infoPath == null)
            {
                _logger.LogError("No Info.json found in folder {Folder}", modFolderPath);
                return null;
            }

            var json = await File.ReadAllTextAsync(infoPath, ct);
            var modInfo = JsonSerializer.Deserialize<ModInfo>(json, JsonOptions);
            if (modInfo == null) return null;

            var targetDir = activate
                ? Path.Combine(gamePath, "Mods",          modInfo.Id)
                : Path.Combine(gamePath, "Mods.inactive", modInfo.Id);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            if (Directory.Exists(targetDir))
            {
                var existingInfoPath = InfoJsonLocator.Locate(targetDir);
                if (existingInfoPath != null)
                {
                    try
                    {
                        var existingMod = JsonSerializer.Deserialize<ModInfo>(
                            await File.ReadAllTextAsync(existingInfoPath, ct), JsonOptions);
                        if (existingMod != null)
                        {
                            existingMod.FolderPath = targetDir;
                            if (ShouldArchive)
                                await _versionCache.ArchiveCurrentVersionAsync(existingMod, storagePath);
                        }
                    }
                    catch { /* archive failure is non-fatal */ }
                }
                Directory.Delete(targetDir, true);
            }

            await Task.Run(() => CopyDirectoryRecursive(modFolderPath, targetDir), ct);

            modInfo.FolderPath = targetDir;
            modInfo.IsActive   = activate;
            modInfo.State      = activate ? ModState.Active : ModState.Inactive;

            _logger.LogInformation("Installed mod {Id} v{Version} from folder", modInfo.Id, modInfo.Version);
            return modInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install from folder {Folder}", modFolderPath);
            return null;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public async Task<bool> UninstallModAsync(ModInfo mod, string gamePath, string storagePath, bool hardDelete = false, CancellationToken ct = default)
    {
        try
        {
            if (hardDelete)
            {
                if (Directory.Exists(mod.FolderPath)) Directory.Delete(mod.FolderPath, true);
                _logger.LogInformation("Hard-deleted mod {Id}", mod.Id);
            }
            else
            {
                // Archive current state into the version cache before removing
                if (ShouldArchive && Directory.Exists(mod.FolderPath))
                    await _versionCache.ArchiveCurrentVersionAsync(mod, storagePath);

                if (Directory.Exists(mod.FolderPath)) Directory.Delete(mod.FolderPath, true);
                _logger.LogInformation("Soft-deleted mod {Id}", mod.Id);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall mod {Id}", mod.Id);
            return false;
        }
    }

    // ── Rollback ──────────────────────────────────────────────────────────────

    public async Task<bool> RollbackToVersionAsync(
        string modId, string version, string gamePath, string storagePath, CancellationToken ct = default)
    {
        try
        {
            var archivePath = await _versionCache.GetVersionArchivePathAsync(modId, version, storagePath);
            if (archivePath == null)
            {
                _logger.LogError("No archive found for {Id} v{Version}", modId, version);
                return false;
            }

            // Determine current folder (active or inactive)
            var activePath = Path.Combine(gamePath, "Mods", modId);
            var inactivePath = Path.Combine(gamePath, "Mods.inactive", modId);
            var currentPath = Directory.Exists(activePath) ? activePath : inactivePath;
            var isActive = currentPath == activePath;

            // Archive current before overwriting
            if (Directory.Exists(currentPath))
            {
                // Get current version from Info.json
                var infoPath = InfoJsonLocator.Locate(currentPath);
                if (infoPath != null)
                {
                    var currentMod = JsonSerializer.Deserialize<ModInfo>(
                        await File.ReadAllTextAsync(infoPath, ct), JsonOptions);
                    if (currentMod != null)
                    {
                        currentMod.FolderPath = currentPath;
                        if (ShouldArchive)
                            await _versionCache.ArchiveCurrentVersionAsync(currentMod, storagePath);
                    }
                }
                Directory.Delete(currentPath, true);
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, currentPath, overwriteFiles: true), ct);
            _logger.LogInformation("Rolled back {Id} to v{Version}", modId, version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for {Id}", modId);
            return false;
        }
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<bool> UpdateModAsync(
        ModInfo mod, ModUpdateInfo update, string gamePath, string storagePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(update.DownloadUrl))
        {
            _logger.LogError("No download URL for update of {Id}", mod.Id);
            return false;
        }

        try
        {
            // 1. Download the new zip first — before touching anything on disk
            var downloadDir = Path.Combine(storagePath, "downloads", mod.Id);
            Directory.CreateDirectory(downloadDir);
            var downloadPath = Path.Combine(downloadDir, $"{update.LatestVersion}.zip");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DVModManager/1.0");
            using var response = await http.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var netStream = await response.Content.ReadAsStreamAsync(ct);
            {
                await using var fileStream = File.Create(downloadPath);
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await netStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    if (totalBytes > 0)
                        progress?.Report((double)downloaded / totalBytes.Value);
                }
            } // fileStream closed & flushed here

            // 2. Validate that the zip actually contains the same mod before touching anything
            var zipModId = await ReadModIdFromZipAsync(downloadPath, ct);
            if (zipModId == null)
            {
                _logger.LogError(
                    "Update aborted for {Id}: could not find Info.json in the downloaded zip", mod.Id);
                File.Delete(downloadPath);
                return false;
            }
            if (!string.Equals(zipModId, mod.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Update aborted for {Id}: zip contains mod '{ZipId}' — mod ID mismatch", mod.Id, zipModId);
                File.Delete(downloadPath);
                return false;
            }

            // 3. Archive current version and remove original folder
            if (ShouldArchive)
                await _versionCache.ArchiveCurrentVersionAsync(mod, storagePath);

            // Remove the original folder now that it is archived.
            // InstallFromArchiveAsync uses modInfo.Id (from the zip) to pick the target path,
            // which may differ from mod.FolderPath (actual folder name on disk).
            if (Directory.Exists(mod.FolderPath))
                await Task.Run(() => Directory.Delete(mod.FolderPath, true), ct);

            // 4. Install — preserves active/inactive state
            var result = await InstallFromArchiveAsync(
                downloadPath, gamePath, storagePath, mod.IsActive, ct);
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for {Id}", mod.Id);
            return false;
        }
    }

    // ── Download and install from URL ──────────────────────────────────────────────

    public async Task<ModInfo?> DownloadAndInstallFromUrlAsync(
        string downloadUrl, string gamePath, string storagePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var downloadDir = Path.Combine(storagePath, "downloads", "profile_imports");
            Directory.CreateDirectory(downloadDir);
            var downloadPath = Path.Combine(downloadDir, $"{Guid.NewGuid()}.zip");

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DVModManager/1.0");
            using var response = await http.GetAsync(
                downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var netStream = await response.Content.ReadAsStreamAsync(ct);
            {
                await using var fileStream = File.Create(downloadPath);
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await netStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    if (totalBytes > 0)
                        progress?.Report((double)downloaded / totalBytes.Value);
                }
            }

            var result = await InstallFromArchiveAsync(downloadPath, gamePath, storagePath, activate: false, ct);

            try { File.Delete(downloadPath); } catch { /* best-effort cleanup */ }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DownloadAndInstall failed for URL {Url}", downloadUrl);
            return null;
        }
    }

    // ── Backup whole Mods folder ──────────────────────────────────────────────

    public async Task<string> BackupModsFolderAsync(string gamePath, string storagePath)
    {
        var modsDir = Path.Combine(gamePath, "Mods");
        var backupsDir = Path.Combine(storagePath, "backups");
        Directory.CreateDirectory(backupsDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupPath = Path.Combine(backupsDir, $"Mods_backup_{timestamp}.zip");

        if (Directory.Exists(modsDir))
            await Task.Run(() => ZipFile.CreateFromDirectory(modsDir, backupPath, CompressionLevel.Fastest, false));

        _logger.LogInformation("Created Mods backup at {Path}", backupPath);
        return backupPath;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves a directory, handling cross-volume scenarios where Directory.Move fails.
    /// Falls back to recursive copy + delete when source and destination are on different roots.
    /// </summary>
    private static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            // Cross-volume: copy then delete
            CopyDirectoryRecursive(source, destination);
            Directory.Delete(source, true);
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static string? FindModRoot(string extractedDir)
    {
        // Check if Info.json is directly in the extracted root
        if (InfoJsonLocator.Locate(extractedDir) != null) return extractedDir;

        // Check one level deep (common pattern: archive contains a single mod folder)
        foreach (var subDir in Directory.GetDirectories(extractedDir))
        {
            if (InfoJsonLocator.Locate(subDir) != null) return subDir;
        }
        return null;
    }

    /// <summary>
    /// Opens a zip without fully extracting it and returns the mod Id from Info.json,
    /// or null if Info.json is missing or the Id field is absent.
    /// </summary>
    private async Task<string?> ReadModIdFromZipAsync(string zipPath, CancellationToken ct)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Find Info.json at the root or one directory deep
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "Info.json", StringComparison.OrdinalIgnoreCase)
                && e.FullName.Count(c => c == '/') <= 1);

            if (entry == null) return null;

            await using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(ct);
            var doc = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
            if (doc.TryGetProperty("Id", out var idElem) || doc.TryGetProperty("id", out idElem))
                return idElem.GetString();

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Info.json from zip {Path}", zipPath);
            return null;
        }
    }
}
