using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ModProfiles;

// Restarts the game to apply mod changes. A detached watchdog process waits for the game to exit
// so Steam sees it as stopped before the relaunch request. On Windows the watchdog is PowerShell,
// under non-Proton Wine the bottle outlives the game and will have Steam so cmd works, but Wine
// has no PowerShell. Under Proton no helper can outlive the game (Steam tears down the container
// on exit), so we just quit and the player relaunches manually.
internal static class GameRestarter
{
    private const uint APP_ID = 588030;

    private enum RestartMode
    {
        PowerShellWatchdog,
        CmdWatchdog,
        QuitOnly,
    }

    [DllImport("kernel32")]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32")]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    public static bool CanAutoRestart => mode.Value != RestartMode.QuitOnly;

    private static readonly Lazy<RestartMode> mode = new(DetectMode);

    private static RestartMode DetectMode()
    {
        try
        {
            if (!IsWine())
            {
                return RestartMode.PowerShellWatchdog;
            }

            return IsProton() ? RestartMode.QuitOnly : RestartMode.CmdWatchdog;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Platform detection failed; assuming native Windows", ex);
            return RestartMode.PowerShellWatchdog;
        }
    }

    private static bool IsWine()
    {
        IntPtr ntdll = GetModuleHandle("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    // Steam sets its compat-tool variables for Proton sessions
    private static bool IsProton() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH"));

    // Quits the game, relaunching it afterwards when the platform allows it.
    public static void RestartOrQuit()
    {
        if (CanAutoRestart)
        {
            try
            {
                StartRelaunchWatchdog();
            }
            catch (Exception ex)
            {
                Main.Logger.LogException("Failed to start relaunch watchdog; quitting without relaunch", ex);
            }
        }

        Main.Logger.Log("Quitting to apply mod enable/disable changes");
        Application.Quit();
    }

    private static void StartRelaunchWatchdog()
    {
        if (mode.Value == RestartMode.PowerShellWatchdog)
        {
            StartPowerShellWatchdog();
        }
        else
        {
            StartCmdWatchdog();
        }
    }

    // Waits for this process to die, gives Steam a moment to register the exit, then relaunches
    // via the steam:// protocol. If we never exit the watchdog gives up.
    private static void StartPowerShellWatchdog()
    {
        int pid = Process.GetCurrentProcess().Id;
        string script =
            $"Wait-Process -Id {pid} -Timeout 120 -ErrorAction SilentlyContinue; " +
            $"if (-not (Get-Process -Id {pid} -ErrorAction SilentlyContinue)) " +
            $"{{ Start-Sleep -Seconds 3; Start-Process 'steam://rungameid/{APP_ID}' }}";

        StartHidden("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"");
        Main.Logger.Log("Relaunch watchdog started (powershell)");
    }

    // Wine's cmd can't reliably wait on a PID, so wait a fixed 10s instead, and if the
    // relaunch fires early Steam ignores it and we're no worse off than quit-only. timeout.exe
    // is preferred, ping is the fallback delay if it's not present.
    private static void StartCmdWatchdog()
    {
        string args = "/c timeout /t 10 /nobreak >nul 2>&1 || ping -n 11 127.0.0.1 >nul & " +
                      $"start steam://rungameid/{APP_ID}";

        StartHidden("cmd.exe", args);
        Main.Logger.Log("Relaunch watchdog started (cmd)");
    }

    private static void StartHidden(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
