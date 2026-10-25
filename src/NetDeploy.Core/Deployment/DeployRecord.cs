using NetDeploy.Core.Manifest;

namespace NetDeploy.Core.Deployment;

/// <summary>One deploy as the history keeps it: what went where, when, by whom, and how it ended.</summary>
/// <param name="App">The package's app name.</param>
/// <param name="Version">The version deployed.</param>
/// <param name="Install">The install's name, or null for a package without an install block.</param>
/// <param name="Folder">The install's folder, or null for a package without an install block.</param>
/// <param name="Time">When the deploy ended.</param>
/// <param name="User">Who ran it, as DOMAIN\user.</param>
/// <param name="Result">Whether it succeeded, failed or was cancelled.</param>
/// <param name="RolledBack">True when a failed deploy was rolled back to its backup.</param>
/// <param name="Log">The deploy's log file, when it has one.</param>
public sealed record DeployRecord(
    string App, string Version, string? Install, string? Folder, DateTimeOffset Time, string User, DeployResult Result, bool RolledBack, string? Log)
{
    /// <summary>The record of a deploy that has just ended, made by the person running this process.</summary>
    public static DeployRecord For(DeployPackage package, DeployOptions options, DeployOutcome outcome, bool rolledBack, string? log) =>
        new(package.Manifest.App.Name, package.Manifest.App.Version, options.Instance.Length > 0 ? options.Instance : null, outcome.InstallDirectory,
            DateTimeOffset.Now, $@"{Environment.UserDomainName}\{Environment.UserName}", outcome.Result, rolledBack, log);
}
