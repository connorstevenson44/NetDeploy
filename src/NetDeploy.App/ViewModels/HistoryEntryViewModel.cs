using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Deployment;

namespace NetDeploy.App.ViewModels;

/// <summary>A deploy to the folder, as the folder view lists it under the folder.</summary>
public sealed partial class HistoryEntryViewModel(DeployRecord record)
{
    public DeployRecord Record => record;
    public DeployResult Result => record.Result;

    public PackIconKind Icon => record.Result switch
    {
        DeployResult.Succeeded => PackIconKind.CheckCircleOutline,
        DeployResult.Failed => PackIconKind.AlertCircleOutline,
        _ => PackIconKind.Cancel,
    };

    /// <summary>Such as "1.1.0 deployed" or "1.1.0 failed, rolled back".</summary>
    public string Label => $"{record.Version} " + record.Result switch
    {
        DeployResult.Succeeded => "deployed",
        DeployResult.Failed => record.RolledBack ? "failed, rolled back" : "failed",
        _ => record.RolledBack ? "stopped, rolled back" : "stopped",
    };

    public string Detail => $"{record.Time.LocalDateTime.ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.CurrentCulture)} by {record.User}";

    public bool HasLog => record.Log is { } log && File.Exists(log);

    /// <summary>Opens the deploy's log in the program Windows uses for .log files.</summary>
    [RelayCommand]
    private void OpenLog()
    {
        if (HasLog)
            Process.Start(new ProcessStartInfo(record.Log!) { UseShellExecute = true });
    }
}
