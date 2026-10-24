using System.ServiceProcess;

namespace NetDeploy.App.ViewModels;

/// <summary>Small helpers for text the UI shows.</summary>
public static class Words
{
    /// <summary>A count and its noun: "1 file", "3 files".</summary>
    public static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    /// <summary>A file's size as a person reads it: "512 bytes", "12 KB", "3.4 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => Count((int)bytes, "byte", "bytes"),
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

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
