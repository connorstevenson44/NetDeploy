using System.Diagnostics;
using System.Text;

namespace NetDeploy.Core;

internal sealed record ProcessResult(int ExitCode, string? LastLine);

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

public enum PowerShellEdition { Windows, Core }

internal static class PowerShellRunner
{
    private const string CommonArguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass";

    public static ProcessStartInfo ForScript(string script, PowerShellEdition edition) =>
        // EncodedCommand avoids every quoting problem with inline scripts.
        new(Executable(edition), $"{CommonArguments} -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}");

    public static ProcessStartInfo ForFile(string file, string arguments, PowerShellEdition edition) =>
        new(Executable(edition), $"{CommonArguments} -File \"{file}\" {arguments}");

    public static async Task<bool> EvaluateAsync(string script, IDeployLog log, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ForScript(script, PowerShellEdition.Windows), log, TimeSpan.FromMinutes(5), cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"The condition script exited with code {result.ExitCode}.");

        return result.LastLine?.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidOperationException($"A condition script must output True or False, but its last output was '{result.LastLine}'."),
        };
    }

    private static string Executable(PowerShellEdition edition) =>
        edition == PowerShellEdition.Core ? "pwsh.exe" : "powershell.exe";
}
