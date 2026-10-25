namespace NetDeploy.Core.Manifest;

/// <summary>
/// The install folder. Each install is named and gets its own subfolder. Steps use {InstanceDir} for the app's folder
/// and {Instance} or {InstanceSuffix} in names.
/// </summary>
public sealed record InstallInfo
{
    /// <summary>Usually "{InstallDir}", a parameter.</summary>
    public required string Folder { get; init; }
}
