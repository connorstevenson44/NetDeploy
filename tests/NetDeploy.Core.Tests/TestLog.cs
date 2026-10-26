using NetDeploy.Core.Deployment;

namespace NetDeploy.Core.Tests;

internal sealed class TestLog : IDeployLog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public string Text => string.Join(Environment.NewLine, Entries.Select(e => e.Message));

    public void Write(LogLevel level, string message) => Entries.Add((level, message));
}
