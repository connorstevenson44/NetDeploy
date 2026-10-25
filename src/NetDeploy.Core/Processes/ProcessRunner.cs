using System.Diagnostics;
using NetDeploy.Core.Deployment;

namespace NetDeploy.Core.Processes;

internal static class ProcessRunner
{
    /// <summary>Runs a process, streaming its output to the log. Kills it if the timeout passes.</summary>
    public static async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo, IDeployLog log, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = new Process { StartInfo = startInfo };
        string? lastLine = null;
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            lastLine = e.Data;
            log.Info("  " + e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                log.Warn("  " + e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit)
            timeoutSource.CancelAfter(limit);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException($"{Path.GetFileName(startInfo.FileName)} did not finish within {timeout}.");
        }

        return new ProcessResult(process.ExitCode, lastLine);
    }
}
