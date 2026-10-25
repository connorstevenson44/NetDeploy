using System.Diagnostics;
using System.Text;
using NetDeploy.Core.Deployment;

namespace NetDeploy.Core.Processes;

internal static class PowerShellRunner
{
    private const string CommonArguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass";

    public static ProcessStartInfo ForScript(string script, PowerShellEdition edition)
    {
        // Progress bars come out as CLIXML noise when output is redirected.
        var command = "$ProgressPreference = 'SilentlyContinue'" + Environment.NewLine + script;
        // EncodedCommand avoids every quoting problem with inline scripts.
        return new(Executable(edition), $"{CommonArguments} -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(command))}");
    }

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
