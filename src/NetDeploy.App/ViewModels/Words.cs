using System.ServiceProcess;

namespace NetDeploy.App.ViewModels;

/// <summary>Small helpers for text the UI shows.</summary>
public static class Words
{
    /// <summary>A count and its noun: "1 file", "3 files".</summary>
    public static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    /// <summary>A service's state as a person says it: "running", "start pending".</summary>
    public static string Status(ServiceControllerStatus? status) => status switch
    {
        null => "can't be read",
        ServiceControllerStatus.StartPending => "starting",
        ServiceControllerStatus.StopPending => "stopping",
        ServiceControllerStatus.ContinuePending => "resuming",
        ServiceControllerStatus.PausePending => "pausing",
        { } known => known.ToString().ToLowerInvariant(),
    };
}
