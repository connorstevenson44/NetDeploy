using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Deployment;

public sealed record DeployOutcome(DeployResult Result, RollbackPlan Rollback, DeployStep? FailedStep = null, Exception? Error = null)
{
    /// <summary>The install's folder, once it is known; null for a package without an install block.</summary>
    public string? InstallDirectory { get; init; }
}
