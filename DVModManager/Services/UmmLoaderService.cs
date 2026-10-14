using DVModManager.Helpers;
using DVModManager.Models;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

/// <summary>
/// Installs, updates and removes the Unity Mod Manager loader inside the game,
/// replicating UMM's own installer behaviour (Doorstop proxy on Windows,
/// Assembly IL patch on Linux/Proton — or explicit user choice).
/// </summary>
public class UmmLoaderService : IUmmLoaderService
{
    public static readonly string ManagerRelativePath =
        Path.Combine("DerailValley_Data", "Managed", "UnityModManager");

    public static string GetManagerPath(string gamePath) =>
        Path.Combine(gamePath, ManagerRelativePath);

    public static string GetManagedPath(string gamePath) =>
        Path.Combine(gamePath, "DerailValley_Data", "Managed");

    private readonly IUmmPayloadService _payloads;
    private readonly ISettingsService _settings;
    private readonly AssemblyPatcher _patcher;
    private readonly ILogger<UmmLoaderService> _logger;

    public UmmLoaderService(IUmmPayloadService payloads, ISettingsService settings,
        AssemblyPatcher patcher, ILogger<UmmLoaderService> logger)
    {
        _payloads = payloads;
        _settings = settings;
        _patcher = patcher;
        _logger = logger;
    }

    /// <summary>
    /// The configured install method from settings; unrecognized values fall
    /// back to the per-build default (Doorstop on the Windows build, Assembly
    /// on the Linux build).
    /// </summary>
    private UmmLoaderMethod ConfiguredMethod =>
        Enum.TryParse<UmmLoaderMethod>(_settings.Settings.UmmLoaderMethod, out var method)
            ? method
            : Enum.Parse<UmmLoaderMethod>(AppSettings.DefaultUmmLoaderMethod);

    /// <summary>
    /// Entry assembly for the Assembly method: the file named in the game
    /// config's entry point ([Assembly.dll]Class.Method). Falls back to the
    /// known Derail Valley entry when no config is available.
    /// </summary>
    private static string GetEntryAssemblyPath(string gamePath, UmmGameInfo? gameInfo = null)
    {
        gameInfo ??= UmmGameInfo.DerailValleyFallback();
        var fallback = UmmGameInfo.DerailValleyFallback();
        var spec = gameInfo.EntryPoint ?? fallback.EntryPoint!;
        if (!AssemblyPatcher.TryParseEntryPoint(spec, out var assemblyName, out _, out _, out _))
        {
            AssemblyPatcher.TryParseEntryPoint(fallback.EntryPoint, out assemblyName, out _, out _, out _);
        }
        return Path.Combine(GetManagedPath(gamePath), assemblyName);
    }

    public UmmLoaderStatus GetStatus(string gamePath)
    {
        var managerPath = GetManagerPath(gamePath);
        var loaderDll = Path.Combine(managerPath, "UnityModManager.dll");

        string? installedVersion = null;
        if (File.Exists(loaderDll))
        {
            try
            {
                if (UmmPayloadService.TryReadAssemblyIdentity(File.ReadAllBytes(loaderDll), out _, out var version))
                    installedVersion = version;
                else
                    installedVersion = "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read loader version from {Path}", loaderDll);
                installedVersion = "unknown";
            }
        }

        var doorstop = DoorstopInstaller.IsInstalled(gamePath);
        var patched = _patcher.IsPatched(GetEntryAssemblyPath(gamePath));

        UmmLoaderMethod? active = patched ? UmmLoaderMethod.Assembly
                   : doorstop ? UmmLoaderMethod.Doorstop
                   : null;

        return new UmmLoaderStatus
        {
            InstalledVersion = installedVersion,
            ActiveMethod = active,
            DoorstopFilesPresent = doorstop,
            AssemblyPatched = patched
        };
    }

    public async Task<bool> InstallAsync(string gamePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var payload = await _payloads.GetPayloadAsync(progress, ct);
        if (payload == null)
        {
            _logger.LogError("No UMM payload available — cannot install the loader");
            return false;
        }
        return InstallPayload(payload, gamePath, ConfiguredMethod);
    }

    public Task<bool> InstallFromZipAsync(string zipPath, string gamePath, CancellationToken ct = default)
    {
        var payload = _payloads.ImportFromZip(zipPath);
        if (payload == null)
        {
            _logger.LogError("Imported UMM package {Path} is not a full loader package", zipPath);
            return Task.FromResult(false);
        }
        return Task.FromResult(InstallPayload(payload, gamePath, ConfiguredMethod));
    }

    private bool InstallPayload(UmmPayloadInfo payload, string gamePath, UmmLoaderMethod method)
    {
        var entryAssemblyPath = GetEntryAssemblyPath(gamePath, payload.GameInfo);

        _logger.LogInformation("Installing UMM {Version} via {Method} into {Game}",
            payload.Version, method, gamePath);

        try
        {
            if (method == UmmLoaderMethod.Doorstop && _patcher.IsPatched(entryAssemblyPath))
            {
                _logger.LogInformation("Removing Assembly patch before Doorstop install");
                _patcher.Remove(entryAssemblyPath, payload.GameInfo.EntryPoint);
            }
            if (method == UmmLoaderMethod.Assembly && DoorstopInstaller.IsInstalled(gamePath))
            {
                _logger.LogInformation("Removing Doorstop files before Assembly install");
                DoorstopInstaller.Remove(gamePath);
            }

            CopyPayloadLibrary(payload, gamePath);

            switch (method)
            {
                case UmmLoaderMethod.Doorstop:
                    if (!payload.HasDoorstopProxies)
                    {
                        _logger.LogError("UMM {Version} carries no Doorstop proxies — use the Assembly method", payload.Version);
                        return false;
                    }
                    DoorstopInstaller.Install(gamePath, payload.Directory,
                        Path.Combine(ManagerRelativePath, "UnityModManager.dll"),
                        logger: _logger);
                    break;

                case UmmLoaderMethod.Assembly:
                    if (!_patcher.Inject(entryAssemblyPath, payload.GameInfo.EntryPoint))
                        return false;
                    break;

                default:
                    _logger.LogError("Unresolved install method {Method}", method);
                    return false;
            }

            _logger.LogInformation("UMM {Version} installed ({Method})", payload.Version, method);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UMM install failed — restoring previous state");
            TryRestore(entryAssemblyPath, payload.GameInfo.EntryPoint);
            return false;
        }
    }

    public Task<bool> UninstallAsync(string gamePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Removing UMM loader from {Game}", gamePath);
        try
        {
            DoorstopInstaller.Remove(gamePath);

            UmmGameInfo? gameInfo = null;
            var configPath = Path.Combine(GetManagerPath(gamePath), "Config.xml");
            if (File.Exists(configPath))
            {
                try
                {
                    gameInfo = UmmGameInfo.ParseFromGameConfig(File.ReadAllText(configPath));
                }
                catch { /* fall back below */ }
            }
            _patcher.Remove(GetEntryAssemblyPath(gamePath, gameInfo), gameInfo?.EntryPoint);

            RemovePayloadLibrary(gamePath);
            _logger.LogInformation("UMM loader removed");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UMM removal failed for {Game}", gamePath);
            return Task.FromResult(false);
        }
    }

    // ── Payload files ────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the UMM library set into <c>Managed/UnityModManager/</c>, the
    /// same file list UMM's DoactionLibraries installs (including its
    /// System.Xml.dll skip when the game already ships one).
    /// </summary>
    private void CopyPayloadLibrary(UmmPayloadInfo payload, string gamePath)
    {
        var managerPath = GetManagerPath(gamePath);
        Directory.CreateDirectory(managerPath);

        foreach (var rel in UmmPayloadService.RequiredFiles)
        {
            if (rel == "System.Xml.dll"
                && File.Exists(Path.Combine(GetManagedPath(gamePath), "System.Xml.dll")))
                continue;

            var src = Path.Combine(payload.Directory, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src))
            {
                _logger.LogWarning("Payload is missing {File}", rel);
                continue;
            }
            var dest = Path.Combine(managerPath, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
        }
        
        File.WriteAllText(Path.Combine(managerPath, "Config.xml"), payload.GameInfo.ToGameConfigXml());
    }

    private void RemovePayloadLibrary(string gamePath)
    {
        var managerPath = GetManagerPath(gamePath);
        if (!Directory.Exists(managerPath)) return;

        foreach (var rel in UmmPayloadService.RequiredFiles)
        {
            var path = Path.Combine(managerPath, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) File.Delete(path);
        }
        var config = Path.Combine(managerPath, "Config.xml");
        if (File.Exists(config)) File.Delete(config);

        foreach (var dll in Directory.GetFiles(managerPath, "*.dll"))
        {
            try { File.Delete(dll); } catch { /* best-effort */ }
        }

        TryDeleteEmptyDirectory(Path.Combine(managerPath, "Harmony", "2.2"));
        TryDeleteEmptyDirectory(Path.Combine(managerPath, "Harmony", "1.2"));
        TryDeleteEmptyDirectory(Path.Combine(managerPath, "Harmony"));
        TryDeleteEmptyDirectory(managerPath);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }
        catch { /* best-effort */ }
    }

    private void TryRestore(string entryAssemblyPath, string? entryPointSpec)
    {
        try
        {
            if (_patcher.IsPatched(entryAssemblyPath))
                _patcher.Remove(entryAssemblyPath, entryPointSpec);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rollback of the assembly patch failed");
        }
    }
}
