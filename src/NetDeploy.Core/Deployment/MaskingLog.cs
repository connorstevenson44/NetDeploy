namespace NetDeploy.Core.Deployment;

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
