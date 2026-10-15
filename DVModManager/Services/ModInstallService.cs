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
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ModInstallService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public ModInstallService(IVersionCacheService versionCache, ISettingsService settings,
        IHttpClientFactory httpFactory, ILogger<ModInstallService> logger)
    {
        _versionCache = versionCache;
        _settings = settings;
        _httpFactory = httpFactory;
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

                // Never silently destroy a leftover/other mod at the target (C3):
                // preserve it under a conflict name instead of deleting it.
                if (Directory.Exists(inactivePath))
                {
                    var conflictPath = GetConflictPath(inactivePath);
                    _logger.LogWarning(
                        "Inactive folder already exists at {Path}; moving it to {Conflict} instead of deleting",
                        inactivePath, conflictPath);
                    MoveDirectory(inactivePath, conflictPath);
                }

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

    public async Task<IReadOnlyList<ModInfo>> InstallFromArchiveAsync(
        string archivePath, string gamePath, string storagePath,
        bool activate = true, Func<IReadOnlyList<ModInfo>, Task<bool>>? confirmMultiple = null,
        CancellationToken ct = default)
    {
        // Extract to a temp dir first to discover the mod ids; always cleaned up in finally (C4)
        var tempDir = Path.Combine(Path.GetTempPath(), "dvmm_" + Guid.NewGuid());
        try
        {
            await Task.Run(() => SafeExtractToDirectory(archivePath, tempDir), ct);

            // An archive may bundle several mods, one per top-level folder (#44)
            var staged = await ReadStagedModsAsync(tempDir, archivePath, ct);
            if (staged.Count == 0)
            {
                _logger.LogError("No installable Info.json found in archive {Archive}", archivePath);
                return [];
            }

            if (staged.Count > 1 && confirmMultiple != null
                && !await confirmMultiple(staged.Select(m => m.Info).ToList()))
            {
                _logger.LogInformation(
                    "Install of {Archive} declined by user ({Count} mods)", archivePath, staged.Count);
                return [];
            }

            var installed = new List<ModInfo>();
            foreach (var entry in staged)
            {
                var modInfo = await InstallStagedModAsync(entry, gamePath, storagePath, activate, ct);
                if (modInfo == null) continue;

                CacheArchive(archivePath, storagePath, modInfo.Id);
                installed.Add(modInfo);
            }
            return installed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install from archive {Archive}", archivePath);
            return [];
        }
        finally
        {
            // Never leak extraction trees under the temp dir, even on failure (C4)
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Moves one extracted mod folder into <c>Mods/</c> or <c>Mods.inactive/</c>,
    /// archiving and replacing an existing install. Returns null on failure.
    /// </summary>
    private async Task<ModInfo?> InstallStagedModAsync(
        StagedMod staged, string gamePath, string storagePath, bool activate, CancellationToken ct)
    {
        var modRoot = staged.Root;
        var modInfo = staged.Info;
        try
        {
            // An already installed mod is updated in place, whichever side it is on.
            (var targetDir, activate) = await Task.Run(
                () => ResolveInstallTarget(gamePath, modInfo.Id, activate), ct);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            // Stage the new content next to the target (same volume) so the final swap
            // is a cheap rename and the target is never left half-written (C4).
            var stagingDir = targetDir + ".staging";
            var backupDir  = targetDir + ".backup";

            await Task.Run(() =>
            {
                TryDeleteDirectory(stagingDir);
                MoveDirectory(modRoot, stagingDir);
            }, ct);

            // Runtime-generated files (e.g. UMM's Settings.xml) survive the reinstall.
            if (Directory.Exists(targetDir))
            {
                await Task.Run(() => CopyMissingFilesRecursive(targetDir, stagingDir), ct);
            }

            // Archive the existing installation before overwriting
            if (Directory.Exists(targetDir))
            {
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
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Archiving previous version of {Id} failed (non-fatal)", modInfo.Id);
                    }
                }
            }

            // Swap: target → backup, staging → target; restore backup if the move fails.
            await Task.Run(() =>
            {
                if (Directory.Exists(targetDir))
                {
                    TryDeleteDirectory(backupDir);
                    MoveDirectory(targetDir, backupDir);
                }

                try
                {
                    MoveDirectory(stagingDir, targetDir);
                }
                catch
                {
                    if (Directory.Exists(backupDir) && !Directory.Exists(targetDir))
                        MoveDirectory(backupDir, targetDir);
                    throw;
                }

                TryDeleteDirectory(backupDir);
            }, ct);

            modInfo.FolderPath = targetDir;
            modInfo.IsActive = activate;
            modInfo.State = activate ? ModState.Active : ModState.Inactive;

            _logger.LogInformation("Installed mod {Id} v{Version}", modInfo.Id, modInfo.Version);
            return modInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install mod {Id} from {Folder}", modInfo.Id, modRoot);
            return null;
        }
    }

    /// <summary>Copies the source archive to the downloads cache of a mod (non-fatal).</summary>
    private void CacheArchive(string archivePath, string storagePath, string modId)
    {
        try
        {
            var downloadsDir = Path.Combine(storagePath, "downloads", modId);
            Directory.CreateDirectory(downloadsDir);
            var destArchive = Path.Combine(downloadsDir, Path.GetFileName(archivePath));
            if (!File.Exists(destArchive)) File.Copy(archivePath, destArchive);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Caching archive {Archive} for {Id} failed (non-fatal)", archivePath, modId);
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

            // Reject malicious/invalid Ids before deriving any filesystem path (C1)
            if (!PathSafety.IsValidModId(modInfo.Id))
            {
                _logger.LogError(
                    "Rejected folder {Folder}: unsafe or empty mod Id '{Id}'", modFolderPath, modInfo.Id);
                return null;
            }

            // An already installed mod is updated in place, whichever side it is on.
            (var targetDir, activate) = await Task.Run(
                () => ResolveInstallTarget(gamePath, modInfo.Id, activate), ct);

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            // Stage next to the target and swap — the existing install is only
            // replaced after the copy fully succeeds (H12/L3).
            var stagingDir = targetDir + ".staging";
            var backupDir  = targetDir + ".backup";

            await Task.Run(() =>
            {
                TryDeleteDirectory(stagingDir);
                CopyDirectoryRecursive(modFolderPath, stagingDir);
            }, ct);

            // Runtime-generated files (e.g. UMM's Settings.xml) survive the install.
            if (Directory.Exists(targetDir))
            {
                await Task.Run(() => CopyMissingFilesRecursive(targetDir, stagingDir), ct);
            }

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
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Archiving previous version of {Id} failed (non-fatal)", modInfo.Id);
                    }
                }
            }

            // Swap: target → backup, staging → target; restore backup if the move fails.
            await Task.Run(() =>
            {
                if (Directory.Exists(targetDir))
                {
                    TryDeleteDirectory(backupDir);
                    MoveDirectory(targetDir, backupDir);
                }

                try
                {
                    MoveDirectory(stagingDir, targetDir);
                }
                catch
                {
                    if (Directory.Exists(backupDir) && !Directory.Exists(targetDir))
                        MoveDirectory(backupDir, targetDir);
                    throw;
                }

                TryDeleteDirectory(backupDir);
            }, ct);

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

            // Resolve the real mod folder via discovery — the folder name may
            // differ from modId, and the mod may live in Mods/ or Mods.inactive/ (H1)
            var activeRoot   = Path.Combine(gamePath, "Mods");
            var inactiveRoot = Path.Combine(gamePath, "Mods.inactive");

            var currentPath = await Task.Run(() =>
                FindModFolderByModId(activeRoot, modId) ??
                FindModFolderByModId(inactiveRoot, modId), ct);

            if (currentPath == null)
            {
                // Not installed under any folder name: fall back to the convention path
                currentPath = PathSafety.SafeCombine(
                    inactiveRoot, PathSafety.IsValidModId(modId) ? modId : null);
            }

            // Explicitly preserve whether the mod currently lives in Mods/ or Mods.inactive/
            var isActive = PathSafety.IsStrictlyUnder(activeRoot, currentPath);
            _logger.LogDebug("Rollback target for {Id} is {Path} (active: {Active})",
                modId, currentPath, isActive);

            // Extract to staging first so a failed extract never destroys the
            // current install (H12/L3).
            var stagingDir = currentPath + ".staging";
            var backupDir  = currentPath + ".backup";

            try
            {
                await Task.Run(() =>
                {
                    TryDeleteDirectory(stagingDir);
                    SafeExtractToDirectory(archivePath, stagingDir);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback aborted for {Id}: extraction of {Zip} failed", modId, archivePath);
                TryDeleteDirectory(stagingDir);
                return false;
            }

            // Runtime-generated files (e.g. UMM's Settings.xml) survive the rollback.
            if (Directory.Exists(currentPath))
            {
                await Task.Run(() => CopyMissingFilesRecursive(currentPath, stagingDir), ct);
            }

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
            }

            // Swap: current → backup, staging → current; restore backup on failure.
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);

                if (Directory.Exists(currentPath))
                {
                    TryDeleteDirectory(backupDir);
                    MoveDirectory(currentPath, backupDir);
                }

                try
                {
                    MoveDirectory(stagingDir, currentPath);
                }
                catch
                {
                    if (Directory.Exists(backupDir) && !Directory.Exists(currentPath))
                        MoveDirectory(backupDir, currentPath);
                    throw;
                }

                TryDeleteDirectory(backupDir);
            }, ct);

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
        IProgress<double>? progress = null, Func<IReadOnlyList<ModInfo>, Task<bool>>? confirmExtraMods = null,
        CancellationToken ct = default)
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

            var http = _httpFactory.CreateClient("github");
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
            var zipModIds = await ReadModIdsFromZipAsync(downloadPath, ct);
            if (zipModIds.Count == 0)
            {
                _logger.LogError(
                    "Update aborted for {Id}: could not find Info.json in the downloaded zip", mod.Id);
                File.Delete(downloadPath);
                return false;
            }
            if (!zipModIds.Contains(mod.Id, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Update aborted for {Id}: zip contains mod(s) '{ZipIds}' — mod ID mismatch",
                    mod.Id, string.Join(", ", zipModIds));
                File.Delete(downloadPath);
                return false;
            }

            // 3. Extract the update to a staging folder first — the live mod stays
            //    untouched until the new version is fully extracted and valid (C2).
            var livePath   = mod.FolderPath;
            var stagingDir = livePath + ".update_staging";
            var backupDir  = livePath + ".update_backup";

            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                    SafeExtractToDirectory(downloadPath, stagingDir);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update aborted for {Id}: extraction of {Zip} failed", mod.Id, downloadPath);
                TryDeleteDirectory(stagingDir);
                return false;
            }

            // Resolve the staged mod root (archives may wrap the mod in a folder or bundle
            // several mods); the other bundled mods are installed after the swap (#44).
            var stagedMods = await ReadStagedModsAsync(stagingDir, downloadPath, ct);
            var target = stagedMods.FirstOrDefault(m =>
                string.Equals(m.Info.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                _logger.LogError("Update aborted for {Id}: no matching Info.json in downloaded zip", mod.Id);
                TryDeleteDirectory(stagingDir);
                return false;
            }
            var stagedRoot = target.Root;
            var extraMods  = stagedMods.Where(m => m != target).ToList();

            // Runtime-generated files (e.g. UMM's Settings.xml) survive the update.
            if (Directory.Exists(livePath))
            {
                var preserved = await Task.Run(() => CopyMissingFilesRecursive(livePath, stagedRoot), ct);
                if (preserved > 0)
                    _logger.LogDebug("Update of {Id}: preserved {Count} file(s) not present in the new version",
                        mod.Id, preserved);
            }

            // 4. Archive current version, then swap: live → backup, staging → live.
            //    On failure the backup is restored, so the old version survives a bad update.
            if (ShouldArchive)
                await _versionCache.ArchiveCurrentVersionAsync(mod, storagePath);

            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);

                    if (Directory.Exists(livePath))
                    {
                        if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                        MoveDirectory(livePath, backupDir);
                    }

                    try
                    {
                        MoveDirectory(stagedRoot, livePath);
                    }
                    catch
                    {
                        // Put the old version back before surfacing the error
                        if (Directory.Exists(backupDir) && !Directory.Exists(livePath))
                            MoveDirectory(backupDir, livePath);
                        throw;
                    }

                    if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update failed for {Id}: swap failed, previous version restored", mod.Id);
                TryDeleteDirectory(stagingDir);
                TryDeleteDirectory(backupDir);
                return false;
            }

            // Install the other mods bundled in the same zip; their failure does not fail the update.
            try
            {
                if (extraMods.Count > 0)
                {
                    if (confirmExtraMods == null
                        || await confirmExtraMods(extraMods.Select(m => m.Info).ToList()))
                    {
                        foreach (var extra in extraMods)
                        {
                            if (await InstallStagedModAsync(extra, gamePath, storagePath, activate: false, ct) == null)
                                _logger.LogWarning("Update of {Id}: bundled mod {Extra} failed to install",
                                    mod.Id, extra.Info.Id);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Update of {Id}: user declined {Count} bundled mod(s)",
                            mod.Id, extraMods.Count);
                    }
                }
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
            }

            // 5. Clean up downloaded zip after successful install
            try { File.Delete(downloadPath); } catch { /* best-effort */ }

            _logger.LogInformation("Updated {Id} to v{Version}", mod.Id, update.LatestVersion);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for {Id}", mod.Id);
            return false;
        }
    }

    // ── Download and install from URL ──────────────────────────────────────────────

    public async Task<IReadOnlyList<ModInfo>> DownloadAndInstallFromUrlAsync(
        string downloadUrl, string gamePath, string storagePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var downloadDir = Path.Combine(storagePath, "downloads", "profile_imports");
            Directory.CreateDirectory(downloadDir);
            var downloadPath = Path.Combine(downloadDir, $"{Guid.NewGuid()}.zip");

            var http = _httpFactory.CreateClient("github");
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

            var result = await InstallFromArchiveAsync(downloadPath, gamePath, storagePath, activate: false, ct: ct);

            try { File.Delete(downloadPath); } catch { /* best-effort cleanup */ }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DownloadAndInstall failed for URL {Url}", downloadUrl);
            return [];
        }
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

    /// <summary>
    /// Extracts a zip entry-by-entry, normalizing Windows-style <c>\</c> separators
    /// in entry paths (which are not separators on Unix) and validating that every
    /// destination stays inside <paramref name="destinationDir"/> (zip-slip) (H4).
    /// </summary>
    private static void SafeExtractToDirectory(string zipPath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        var rootFull = Path.GetFullPath(destinationDir);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
            rootFull += Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            // Normalize entry path: '\' → '/' then to the platform separator
            var relative = entry.FullName.Replace('\\', '/');
            while (relative.StartsWith('/')) relative = relative.TrimStart('/');

            var destination = Path.GetFullPath(Path.Combine(rootFull, relative));
            if (!destination.StartsWith(rootFull, comparison))
                throw new InvalidOperationException($"Zip entry '{entry.FullName}' escapes extraction directory.");

            if (string.IsNullOrEmpty(entry.Name)) // directory entry
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
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

    /// <summary>
    /// Copies files and directories missing from <paramref name="destination"/>, never overwriting.
    /// Keeps runtime-generated files (e.g. UMM's Settings.xml) across updates.
    /// </summary>
    private static int CopyMissingFilesRecursive(string source, string destination)
    {
        var copied = 0;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(destFile)) continue;
            File.Copy(file, destFile);
            copied++;
        }
        foreach (var dir in Directory.GetDirectories(source))
            copied += CopyMissingFilesRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
        return copied;
    }

    /// <summary>
    /// Returns a non-existing sibling path that preserves a conflicting directory
    /// instead of deleting it (e.g. <c>MyMod.conflict-20260924-075500</c>).
    /// </summary>
    private static string GetConflictPath(string path)
    {
        var parent = Path.GetDirectoryName(path)!;
        var name   = Path.GetFileName(path);
        var stamp  = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(parent, $"{name}.conflict-{stamp}");
        var n = 1;
        while (Directory.Exists(candidate))
            candidate = Path.Combine(parent, $"{name}.conflict-{stamp}-{n++}");
        return candidate;
    }

    /// <summary>Best-effort recursive directory delete that never throws.</summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { /* best-effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Returns the existing folder of an installed mod (in <c>Mods/</c> first, then
    /// <c>Mods.inactive/</c>) with its side, so reinstalls update it in place instead of
    /// creating a second copy. Falls back to <c>{Id}</c> on the requested side.
    /// </summary>
    private (string TargetDir, bool IsActive) ResolveInstallTarget(string gamePath, string modId, bool activate)
    {
        var activeRoot   = Path.Combine(gamePath, "Mods");
        var inactiveRoot = Path.Combine(gamePath, "Mods.inactive");

        var existing = FindModFolderByModId(activeRoot, modId);
        if (existing != null) return (existing, true);

        existing = FindModFolderByModId(inactiveRoot, modId);
        if (existing != null) return (existing, false);

        return (PathSafety.SafeCombine(activate ? activeRoot : inactiveRoot, modId), activate);
    }

    /// <summary>
    /// Finds a mod's folder under <paramref name="root"/> by folder name (fast path)
    /// or by the <c>Id</c> field of its Info.json. Comparison is case-insensitive.
    /// </summary>
    private string? FindModFolderByModId(string root, string modId)
    {
        if (!Directory.Exists(root)) return null;

        var dirs = Directory.GetDirectories(root);

        // Fast path: folder name matches the mod id
        foreach (var dir in dirs)
        {
            if (string.Equals(Path.GetFileName(dir), modId, StringComparison.OrdinalIgnoreCase))
                return dir;
        }

        // Slow path: match the Id declared in Info.json
        foreach (var dir in dirs)
        {
            if (IsInstallerWorkDir(dir)) continue;
            var infoPath = InfoJsonLocator.Locate(dir);
            if (infoPath == null) continue;
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(
                    File.ReadAllText(infoPath), JsonOptions);
                if ((doc.TryGetProperty("Id", out var idElem) || doc.TryGetProperty("id", out idElem))
                    && string.Equals(idElem.GetString(), modId, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read {Path} while resolving mod {Id}", infoPath, modId);
            }
        }
        return null;
    }

    /// <summary>
    /// Matches the installer's intermediate folders (same set ModDiscoveryService skips)
    /// plus preserved <c>*.conflict-*</c> folders, which never count as the live install.
    /// </summary>
    private static bool IsInstallerWorkDir(string dir)
    {
        var name = Path.GetFileName(dir);
        return name.EndsWith(".staging", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".update_staging", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".update_backup", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".conflict-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An extracted mod folder and its parsed Info.json.</summary>
    private sealed record StagedMod(string Root, ModInfo Info);

    /// <summary>
    /// Returns the mod folders of an extracted archive: the root itself when it holds
    /// an Info.json, otherwise every top-level folder that holds one.
    /// </summary>
    private static List<string> FindModRoots(string extractedDir)
    {
        // Check if Info.json is directly in the extracted root
        if (InfoJsonLocator.Locate(extractedDir) != null) return [extractedDir];

        // Check one level deep (archive contains one or more mod folders)
        return Directory.GetDirectories(extractedDir)
            .Where(d => InfoJsonLocator.Locate(d) != null)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Parses the Info.json of every mod folder in <paramref name="extractedDir"/>.
    /// Unreadable, unsafe or duplicate mods are logged and skipped.
    /// </summary>
    private async Task<List<StagedMod>> ReadStagedModsAsync(
        string extractedDir, string archivePath, CancellationToken ct)
    {
        var result = new List<StagedMod>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in FindModRoots(extractedDir))
        {
            ModInfo? info;
            try
            {
                var infoPath = InfoJsonLocator.Locate(root)!;
                info = JsonSerializer.Deserialize<ModInfo>(
                    await File.ReadAllTextAsync(infoPath, ct), JsonOptions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Skipping {Folder} in {Archive}: unreadable Info.json", root, archivePath);
                continue;
            }
            if (info == null) continue;

            // Reject malicious/invalid Ids before deriving any filesystem path (C1)
            if (!PathSafety.IsValidModId(info.Id))
            {
                _logger.LogError(
                    "Rejected {Folder} in {Archive}: unsafe or empty mod Id '{Id}'", root, archivePath, info.Id);
                continue;
            }
            if (!seen.Add(info.Id))
            {
                _logger.LogWarning("Skipping {Folder} in {Archive}: duplicate mod Id '{Id}'", root, archivePath, info.Id);
                continue;
            }

            result.Add(new StagedMod(root, info));
        }
        return result;
    }

    /// <summary>
    /// Opens a zip without fully extracting it and returns the mod Ids of every Info.json
    /// at the root or one directory deep. Empty if none is found or readable.
    /// </summary>
    private async Task<List<string>> ReadModIdsFromZipAsync(string zipPath, CancellationToken ct)
    {
        var ids = new List<string>();
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // Find Info.json at the root or one directory deep.
            // Count both '/' and '\' — Windows-made zips may use '\' separators (M8).
            var entries = archive.Entries.Where(e =>
                string.Equals(e.Name, "Info.json", StringComparison.OrdinalIgnoreCase)
                && e.FullName.Count(c => c == '/' || c == '\\') <= 1);

            foreach (var entry in entries)
            {
                await using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync(ct);
                var doc = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
                if ((doc.TryGetProperty("Id", out var idElem) || doc.TryGetProperty("id", out idElem))
                    && idElem.GetString() is { Length: > 0 } id)
                    ids.Add(id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Info.json from zip {Path}", zipPath);
        }
        return ids;
    }
}
