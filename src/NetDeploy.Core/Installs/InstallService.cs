using System.ServiceProcess;

namespace NetDeploy.Core.Installs;

/// <summary>A Windows service that runs a program in an install, and its state; null when it can't be read.</summary>
public sealed record InstallService(string Name, ServiceControllerStatus? Status);
