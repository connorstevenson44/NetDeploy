namespace NetDeploy.Core.Installs;

/// <summary>A copy of an install's folder that a backup step made before a deploy, and when it was taken.</summary>
public sealed record InstallBackup(string Directory, DateTime Taken);
