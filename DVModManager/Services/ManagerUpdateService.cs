using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class ManagerUpdateService : IManagerUpdateService
{
    /// <summary>Suffix for files replaced by an update; removed on the next start.</summary>
    internal const string OldFileSuffix = ".dvmm-old";

    private const string ReleaseDownloadBase = "https://github.com/Fuggschen/DVModManager/releases/download";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ManagerUpdateService> _logger;
    private readonly string? _processPath;
    private readonly Action<string> _startProcess;

    public ManagerUpdateService(IHttpClientFactory httpFactory, ILogger<ManagerUpdateService> logger,
        string? processPath = null, Action<string>? startProcess = null)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _processPath = processPath ?? Environment.ProcessPath;
        _startProcess = startProcess ?? StartProcess;
    }

    internal static string RuntimeId => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    internal static string ExecutableName => OperatingSystem.IsWindows() ? "DVModManager.exe" : "DVModManager";

    public bool CanSelfUpdate
    {
        get
        {
            if (string.IsNullOrEmpty(_processPath) || !File.Exists(_processPath)) return false;

            // `dotnet run` / `dotnet DVModManager.dll` report the host, not our executable
            if (!string.Equals(Path.GetFileName(_processPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
                return false;

            return IsDirectoryWritable(Path.GetDirectoryName(_processPath)!);
        }
    }

    /// <summary>
    /// Asset URL for the given release. Derived from the release page URL when it is a
    /// GitHub tag link, so a renamed tag scheme keeps working; otherwise built from the version.
    /// </summary>
    internal static string BuildDownloadUrl(string version, string? releaseUrl, string runtimeId)
    {
        var asset = $"DVModManager-{runtimeId}.zip";
        const string tagMarker = "/releases/tag/";
        if (!string.IsNullOrEmpty(releaseUrl))
        {
            var idx = releaseUrl.IndexOf(tagMarker, StringComparison.Ordinal);
            if (idx > 0)
            {
                var tag = releaseUrl[(idx + tagMarker.Length)..].TrimEnd('/');
                if (tag.Length > 0)
                    return $"{releaseUrl[..idx]}/releases/download/{tag}/{asset}";
            }
        }
        return $"{ReleaseDownloadBase}/dvmodmanager-v{version.Trim().TrimStart('v', 'V')}/{asset}";
    }

    public async Task<string> DownloadAndStageAsync(string version, string? releaseUrl,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var url = BuildDownloadUrl(version, releaseUrl, RuntimeId);
        var workDir = Path.Combine(Path.GetTempPath(), "DVModManager-update-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(workDir, "update.zip");
        var stagedDir = Path.Combine(workDir, "staged");
        Directory.CreateDirectory(stagedDir);

        try
        {
            _logger.LogInformation("Downloading manager update {Version} from {Url}", version, url);
            var http = _httpFactory.CreateClient("github");
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zipPath);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total is > 0) progress?.Report((double)read / total.Value);
                }
            }

            // Only the executable is needed; anything else in the package is ignored
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.GetEntry(ExecutableName)
                    ?? throw new InvalidDataException($"Update package does not contain {ExecutableName} at its root.");
                entry.ExtractToFile(Path.Combine(stagedDir, ExecutableName));
            }
            File.Delete(zipPath);

            return stagedDir;
        }
        catch
        {
            TryDeleteDirectory(workDir);
            throw;
        }
    }

    public void ApplyUpdate(string stagedDir)
    {
        if (string.IsNullOrEmpty(_processPath))
            throw new InvalidOperationException("Executable path is unknown.");

        var newExe = Path.Combine(stagedDir, ExecutableName);

        // Preserve the execute bit on Linux; the zip entry may not carry it
        UnixFileMode? exeMode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(_processPath);

        // A running executable cannot be overwritten on Windows, but it can be renamed
        var backup = _processPath + OldFileSuffix;
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(_processPath, backup);
        try
        {
            File.Move(newExe, _processPath);
            if (exeMode is { } mode && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(_processPath, mode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying manager update failed, restoring previous executable");
            try { File.Move(backup, _processPath, overwrite: true); }
            catch (Exception mvEx) { _logger.LogError(mvEx, "Rollback: could not restore {Path}", _processPath); }
            throw;
        }

        TryDeleteDirectory(Path.GetDirectoryName(stagedDir)!);
        _logger.LogInformation("Manager update applied to {Path}", _processPath);
    }

    public void StartNewInstance()
    {
        if (string.IsNullOrEmpty(_processPath))
            throw new InvalidOperationException("Executable path is unknown.");
        _startProcess(_processPath);
    }

    public async Task CleanupOldFilesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_processPath)) return;
        var dir = Path.GetDirectoryName(_processPath);
        if (dir == null || !Directory.Exists(dir)) return;

        string[] leftovers;
        try { leftovers = Directory.GetFiles(dir, "*" + OldFileSuffix); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not scan for old update files");
            return;
        }

        // The previous process may still be shutting down and holding its executable
        const int attempts = 10;
        foreach (var file in leftovers)
        {
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    File.Delete(file);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (i == attempts - 1)
                    {
                        _logger.LogDebug(ex, "Could not delete {File}; will retry next start", file);
                        break;
                    }
                    await Task.Delay(500, ct);
                }
            }
        }
    }

    private static void StartProcess(string path)
    {
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!
        });
    }

    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".dvmm-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete temp folder {Dir}", dir);
        }
    }
}
