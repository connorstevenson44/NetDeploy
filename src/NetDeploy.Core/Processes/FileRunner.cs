using System.Diagnostics;
using NetDeploy.Core.Deployment;

namespace NetDeploy.Core.Processes;

/// <summary>Runs an exe, a PowerShell script or a batch file from an install folder, with the output in the log.</summary>
public static class FileRunner
{
    private static readonly string[] Runnable = [".exe", ".ps1", ".bat", ".cmd"];

    public static bool CanRun(string path) => Runnable.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs the file in its own folder and returns its exit code.</summary>
    public static async Task<int> RunAsync(string path, string arguments, IDeployLog log, CancellationToken cancellationToken)
    {
        var startInfo = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ps1" => PowerShellRunner.ForFile(path, arguments, PowerShellEdition.Windows),
            // cmd /c strips the outer quotes, so the whole command line is quoted once more.
            ".bat" or ".cmd" => new ProcessStartInfo("cmd.exe", $"/d /c \"\"{path}\" {arguments}\""),
            _ => new ProcessStartInfo(path, arguments),
        };
        startInfo.WorkingDirectory = Path.GetDirectoryName(path)!;

        log.Info($"> {Path.GetFileName(path)} {arguments}".TrimEnd());
        var result = await ProcessRunner.RunAsync(startInfo, log, timeout: null, cancellationToken);
        log.Info($"{Path.GetFileName(path)} exited with code {result.ExitCode}.");
        return result.ExitCode;
    }
}
