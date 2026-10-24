using System.ServiceProcess;
using Microsoft.Win32;

namespace NetDeploy.Core.Steps;

internal static class WindowsServices
{
    /// <summary>
    /// Services whose program is inside the folder, read from each service's ImagePath, so services installed by hand
    /// are found too. Programs inside skipFolders don't count.
    /// </summary>
    public static IReadOnlyList<string> ServicesIn(string folder)
    {
        if (!OperatingSystem.IsWindows())
            return [];
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null)
            return [];

        var found = new List<string>();
        foreach (var name in services.GetSubKeyNames())
        {
            using var service = services.OpenSubKey(name);
            if (service?.GetValue("ImagePath") is string image && ProgramPath(image) is { } program
                && IsInside(program, folder))
                found.Add(name);
        }
        return found;
    }

    /// <summary>The program in a service's command line, which may be quoted and followed by arguments.</summary>
    internal static string? ProgramPath(string imagePath)
    {
        var line = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (line.StartsWith('"'))
        {
            var end = line.IndexOf('"', 1);
            return end < 0 ? null : line[1..end];
        }
        var exe = line.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe < 0 ? line : line[..(exe + 4)];
    }

    private static bool IsInside(string path, string folder) =>
        Path.IsPathFullyQualified(path)
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool Exists(string name)
    {
        using var service = new ServiceController(name);
        try
        {
            _ = service.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Returns true if the service was running and has been stopped.</summary>
    public static bool Stop(string name, TimeSpan timeout)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Stopped)
            return false;
        if (service.Status != ServiceControllerStatus.StopPending)
            service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        return true;
    }

    public static void Start(string name, TimeSpan timeout)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Running)
            return;
        if (service.Status != ServiceControllerStatus.StartPending)
            service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }
}
