using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace NetDeploy.App.Services;

internal static class Elevation
{
    public static bool IsAdministrator =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts a new elevated copy of the app. Returns false if the user declined the UAC prompt.</summary>
    public static bool TryRestartElevated(string? packagePath)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = packagePath is null ? "" : $"\"{packagePath}\"",
        };
        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
