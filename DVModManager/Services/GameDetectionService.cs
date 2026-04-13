using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DVModManager.Services;

public sealed class GameDetectionService : IGameDetectionService
{
    private const string ProcessName = "DerailValley";

    private readonly Timer _pollingTimer;
    private bool _lastRunningState;

    public event EventHandler<bool>? GameRunningChanged;

    public GameDetectionService()
    {
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

    public bool IsGameRunning()
    {
        try { return Process.GetProcessesByName(ProcessName).Length > 0; }
        catch { return false; }
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
        catch { return null; }
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

    private static List<string> ParseLibraryFolders(string vdfPath)
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
        catch { /* ignore VDF parse errors */ }
        return paths;
    }

    public bool ValidateGamePath(string path) =>
        Directory.Exists(path) && Directory.Exists(Path.Combine(path, "DerailValley_Data"));

    public void Dispose() => _pollingTimer.Dispose();
}
