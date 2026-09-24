using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public sealed class GameDetectionService : IGameDetectionService
{
    private const string ProcessName = "DerailValley";

    private readonly ILogger<GameDetectionService> _logger;
    private readonly Timer _pollingTimer;
    private bool _lastRunningState;

    public event EventHandler<bool>? GameRunningChanged;

    public GameDetectionService(ILogger<GameDetectionService> logger)
    {
        _logger = logger;
        _lastRunningState = IsGameRunning();
        _pollingTimer = new Timer(Poll, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    private void Poll(object? state)
    {
        var running = IsGameRunning();
        if (running != _lastRunningState)
        {
            _lastRunningState = running;
            GameRunningChanged?.Invoke(this, running);
        }
    }

    /// <summary>
    /// True when the Derail Valley process is running. Process instances are always
    /// disposed (the poll loop calls this every 3s and leaking handles adds up) (H10),
    /// and on Linux/Proton the match also covers <c>DerailValley.exe</c>,
    /// <c>wine</c>/<c>reaper</c> hosting the game, so the lockout actually engages (H10).
    /// </summary>
    public bool IsGameRunning()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return false; }

        var found = false;
        try
        {
            foreach (var process in processes)
            {
                if (!found && MatchesGameProcess(process)) found = true;
            }
            return found;
        }
        catch { return found; }
        finally
        {
            // Always dispose every Process handle, matched or not (H10)
            foreach (var process in processes)
            {
                try { process.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

    private static bool MatchesGameProcess(Process process)
    {
        string name;
        try { name = process.ProcessName; }
        catch { return false; }

        // Native Windows / direct match (ProcessName excludes ".exe")
        if (string.Equals(name, ProcessName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
            return true;

        // Under Proton/Wine the visible process is often "wine", "reaper" or the
        // exe name with extension — inspect the module path when accessible.
        if (OperatingSystem.IsLinux() &&
            (name.Equals("wine", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("reaper", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("wine-preloader", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("proton", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var module = process.MainModule?.FileName;
                return module != null && module.Contains(ProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Accessing MainModule of other users' processes throws — not the game
                return false;
            }
        }

        return false;
    }

    public string? DetectGamePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var path = DetectViaRegistryWindows();
            if (path != null) return path;
        }

        return DetectViaSteamConfigFile();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private string? DetectViaRegistryWindows()
    {
        try
        {
            using var key =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam")
                ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");

            if (key?.GetValue("InstallPath") is not string steamPath) return null;
            return FindDerailValleyInLibraries(steamPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Registry-based Steam detection failed");
            return null;
        }
    }

    private string? DetectViaSteamConfigFile()
    {
        var steamPaths = new List<string>();

        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            steamPaths.Add(Path.Combine(home, ".steam", "steam"));
            steamPaths.Add(Path.Combine(home, ".local", "share", "Steam"));
        }

        foreach (var steamPath in steamPaths)
        {
            if (!Directory.Exists(steamPath)) continue;
            var result = FindDerailValleyInLibraries(steamPath);
            if (result != null) return result;
        }
        return null;
    }

    private string? FindDerailValleyInLibraries(string steamPath)
    {
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        var libraryPaths = File.Exists(vdfPath) ? ParseLibraryFolders(vdfPath) : [];
        libraryPaths.Insert(0, steamPath);

        foreach (var lib in libraryPaths)
        {
            var gamePath = Path.Combine(lib, "steamapps", "common", "Derail Valley");
            if (ValidateGamePath(gamePath)) return gamePath;
        }
        return null;
    }

    private List<string> ParseLibraryFolders(string vdfPath)
    {
        var paths = new List<string>();
        try
        {
            var content = File.ReadAllText(vdfPath);
            // Match "path" entries in the VDF (supports both old and new format)
            foreach (Match m in Regex.Matches(content, @"""path""\s+""([^""]+)"""))
            {
                var path = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path)) paths.Add(path);
            }
        }
        catch (Exception ex)
        {
            // Corrupt VDF silently skipping all libraries would hide real paths (H13)
            _logger.LogWarning(ex, "Failed to parse {Path}", vdfPath);
        }
        return paths;
    }

    public bool ValidateGamePath(string path) =>
        Directory.Exists(path) && Directory.Exists(Path.Combine(path, "DerailValley_Data"));

    public void Dispose() => _pollingTimer.Dispose();
}
