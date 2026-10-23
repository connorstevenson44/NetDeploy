using System.ServiceProcess;

namespace NetDeploy.Core.Steps;

internal static class WindowsServices
{
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
