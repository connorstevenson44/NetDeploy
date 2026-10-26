using NetDeploy.Core.Deployment;

namespace NetDeploy.App.Services;

/// <summary>
/// A class rather than a record: two identical lines logged in the same clock tick would be equal records,
/// and a ListBox can't tell equal items apart.
/// </summary>
public sealed class LogEntry(DateTime time, LogLevel level, string message)
{
    public DateTime Time { get; } = time;
    public LogLevel Level { get; } = level;
    public string Message { get; } = message;

    public override string ToString() => LogFiles.Line(Time, Level, Message);
}
