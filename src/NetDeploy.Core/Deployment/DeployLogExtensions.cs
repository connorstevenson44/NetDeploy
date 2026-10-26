namespace NetDeploy.Core.Deployment;

public static class DeployLogExtensions
{
    public static void Info(this IDeployLog log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this IDeployLog log, string message) => log.Write(LogLevel.Warning, message);
    public static void Error(this IDeployLog log, string message) => log.Write(LogLevel.Error, message);
}
