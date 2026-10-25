using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Deployment;

internal sealed class StepFailedException(DeployStep step, Exception inner) : Exception(inner.Message, inner)
{
    public DeployStep Step { get; } = step;
}
