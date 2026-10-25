using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace NetDeploy.Core.Steps;

internal static class WindowsServices
{
    /// <summary>
    /// Services whose program is inside the folder, read from each service's ImagePath, so services installed by hand
    /// are found too.
    /// </summary>
    public static IReadOnlyList<string> ServicesIn(string folder) =>
        Programs().Where(service => IsInside(service.Program, folder)).Select(service => service.Name).ToList();

    /// <summary>Every service with the program it runs. Empty on other systems than Windows.</summary>
    public static IReadOnlyList<(string Name, string Program)> Programs()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null)
            return [];

        var found = new List<(string, string)>();
        foreach (var name in services.GetSubKeyNames())
        {
            using var service = services.OpenSubKey(name);
            if (service?.GetValue("ImagePath") is string image && ProgramPath(image) is { } program)
                found.Add((name, program));
        }
        return found;
    }

    /// <summary>The service's state, or null when there is no such service.</summary>
    public static ServiceControllerStatus? Status(string name)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var service = new ServiceController(name);
        try
        {
            return service.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

    internal static bool IsInside(string path, string folder) =>
        Path.IsPathFullyQualified(path)
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool Exists(string name) => Status(name) is not null;

    /// <summary>
    /// Returns true if the service was running and has been stopped. A service that runs in a process of its own is
    /// stopped once that process has gone too: Windows says a service has stopped as soon as it says so itself, while
    /// its program is still shutting down and has its files open.
    /// </summary>
    public static bool Stop(string name, TimeSpan timeout)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Stopped)
            return false;
        var process = OwnProcess(service);
        var started = Stopwatch.StartNew();
        if (service.Status != ServiceControllerStatus.StopPending)
            service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        if (process is not null)
            using (process)
                process.WaitForExit(timeout - started.Elapsed is { Ticks: > 0 } left ? left : TimeSpan.Zero);
        return true;
    }

    /// <summary>The service's process, when it has one to itself rather than sharing a host such as svchost.</summary>
    private static Process? OwnProcess(ServiceController service)
    {
        const int ServiceWin32OwnProcess = 0x10;
        try
        {
            if (!QueryServiceStatusEx(service.ServiceHandle, 0, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _)
                || (status.ServiceType & ServiceWin32OwnProcess) == 0 || status.ProcessId == 0)
                return null;
            return Process.GetProcessById(status.ProcessId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public int ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(SafeHandle service, int infoLevel, out ServiceStatusProcess status, int size, out int needed);

    /// <summary>Returns true if the service was not running and has been started.</summary>
    public static bool Start(string name, TimeSpan timeout)
    {
        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Running)
            return false;
        if (service.Status != ServiceControllerStatus.StartPending)
            service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, timeout);
        return true;
    }
}
