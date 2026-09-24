using System.Diagnostics;

namespace DVModManager.Helpers;

public static class PlatformHelper
{
    /// <summary>
    /// Opens a URL in the default browser or a folder in the default file manager,
    /// using the appropriate mechanism for the current OS.
    /// </summary>
    public static void Open(string pathOrUrl)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsLinux())
            {
                // ArgumentList keeps paths with spaces/metacharacters as one argument (H5)
                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(pathOrUrl);
                Process.Start(psi);
            }
        }
        catch
        {
            // Ignore – best-effort shell interaction
        }
    }
}
