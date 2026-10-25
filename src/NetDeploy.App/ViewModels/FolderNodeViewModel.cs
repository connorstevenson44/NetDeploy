using System.IO;
using System.ServiceProcess;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Installs;

namespace NetDeploy.App.ViewModels;

/// <summary>A folder in the install folder, with its install marked. The folder view lists those in the folder it is in.</summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    internal FolderNodeViewModel(string path, string name, Installation? install, IReadOnlyList<string>? services = null, string? packageVersion = null)
    {
        FullPath = path;
        Name = name;
        Target = install is null ? null : new InstallTarget(install);
        ServiceNames = services ?? [];
        PackageVersion = packageVersion;
        Change = packageVersion is null ? null : Installations.Compare(install?.Version, packageVersion);
    }

    public string FullPath { get; }
    public string Name { get; }

    /// <summary>The install this folder holds, for the remove and clone buttons; null for a plain folder.</summary>
    public InstallTarget? Target { get; }

    public bool IsInstall => Target is not null;

    /// <summary>The Windows services that run a program in the install.</summary>
    internal IReadOnlyList<string> ServiceNames { get; }

    /// <summary>True when Windows services run from the install, so they can be started and stopped from here.</summary>
    public bool HasServices => ServiceNames.Count > 0;

    /// <summary>The open package's version, or null with no package open.</summary>
    public string? PackageVersion { get; }

    /// <summary>How the open package compares with this install, shown as an arrow beside its version; null when unknown.</summary>
    public VersionChange? Change { get; }

    public string ChangeTip => Change switch
    {
        VersionChange.Upgrade => $"The package ({PackageVersion}) is newer: deploying here updates it.",
        VersionChange.Downgrade => $"The package ({PackageVersion}) is older: deploying here goes back a version.",
        VersionChange.Same => $"The package is the same version ({PackageVersion}).",
        _ => "",
    };

    [ObservableProperty]
    public partial ServiceState ServiceState { get; private set; }

    /// <summary>Each service and its state, for the dot's tooltip.</summary>
    [ObservableProperty]
    public partial string ServiceTip { get; private set; } = "";

    /// <summary>The root of a new install whose folder isn't there yet.</summary>
    public bool IsMissing => !Directory.Exists(FullPath);

    public string Detail => Target?.Install switch
    {
        { Version: { } version } => "v" + version,
        not null => "",
        _ => IsMissing ? "not created yet" : "",
    };

    public string Kind => IsInstall ? "install" : "";

    public PackIconKind Icon =>
        IsInstall ? PackIconKind.Server : IsMissing ? PackIconKind.FolderPlusOutline : PackIconKind.FolderOutline;

    /// <summary>Shows the states just read of the install's services.</summary>
    internal void ShowServices(IReadOnlyDictionary<string, ServiceControllerStatus?> states)
    {
        if (!IsInstall)
            return;
        if (ServiceNames.Count == 0)
        {
            ServiceState = ServiceState.NotFound;
            ServiceTip = "No Windows service runs from this install";
            return;
        }
        var mine = ServiceNames.Select(name => (Name: name, Status: states.GetValueOrDefault(name))).ToList();
        ServiceState = mine.All(service => service.Status == ServiceControllerStatus.Running) ? ServiceState.Running
            : mine.All(service => service.Status is ServiceControllerStatus.Stopped or null) ? ServiceState.Stopped
            : ServiceState.Mixed;
        ServiceTip = string.Join(Environment.NewLine, mine.Select(service => $"{service.Name}: {Words.Status(service.Status)}"));
    }
}
