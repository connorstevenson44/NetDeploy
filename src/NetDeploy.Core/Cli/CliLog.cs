using NetDeploy.Core.Deployment;

namespace NetDeploy.Core.Cli;

/// <summary>Writes each line of the log to the console and to the log file.</summary>
internal sealed class CliLog(TextWriter output, StreamWriter file) : IDeployLog, IDisposable
{
    private readonly Lock _gate = new();

    public string FilePath => LogFiles.PathOf(file);

    public void Write(LogLevel level, string message)
    {
        var line = LogFiles.Line(DateTime.Now, level, message);
        lock (_gate)
        {
            file.WriteLine(line);
            output.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
            file.Dispose();
    }
}
