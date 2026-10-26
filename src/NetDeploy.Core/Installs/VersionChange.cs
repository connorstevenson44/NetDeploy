namespace NetDeploy.Core.Installs;

/// <summary>How a package's version compares with the version of an install it would update.</summary>
public enum VersionChange { Upgrade, Same, Downgrade }
