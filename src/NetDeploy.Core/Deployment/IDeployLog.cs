namespace NetDeploy.Core.Deployment;

public interface IDeployLog
{
    void Write(LogLevel level, string message);
}
