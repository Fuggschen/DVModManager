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
                // Only protocol handlers are shell-executed (http/https/steam).
                // Filesystem paths are opened via explorer.exe so a malicious
                // path (e.g. a .exe/.bat dropped into a mod folder) is never
                // executed by ShellExecute (H6).
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp
                        || uri.Scheme == Uri.UriSchemeHttps
                        || uri.Scheme == "steam"))
                {
                    Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = false
                    };
                    psi.ArgumentList.Add(
                        Directory.Exists(pathOrUrl) ? pathOrUrl : $"/select,{pathOrUrl}");
                    Process.Start(psi);
                }
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
