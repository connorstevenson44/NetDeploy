namespace NetDeploy.Core;

public enum LogLevel { Info, Warning, Error }

public interface IDeployLog
{
    void Write(LogLevel level, string message);
}

public static class DeployLogExtensions
{
    public static void Info(this IDeployLog log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this IDeployLog log, string message) => log.Write(LogLevel.Warning, message);
    public static void Error(this IDeployLog log, string message) => log.Write(LogLevel.Error, message);
}

/// <summary>Hides secret values before they reach the log.</summary>
internal sealed class MaskingLog(IDeployLog inner) : IDeployLog
{
    private readonly HashSet<string> _secrets = [];

    public void AddSecret(string value)
    {
        if (value.Length > 0)
            _secrets.Add(value);
    }

    public void Write(LogLevel level, string message)
    {
        foreach (var secret in _secrets)
            message = message.Replace(secret, "****");
        inner.Write(level, message);
    }
}
