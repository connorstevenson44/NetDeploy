using System.Text.Json.Serialization;

namespace NetDeploy.Core.Steps;

public sealed class PowerShellStep : DeployStep
{
    /// <summary>A .ps1 file, inside the package unless the path is absolute.</summary>
    [JsonPropertyName("file")]
    public string? ScriptFile { get; init; }

    /// <summary>An inline script, used instead of a file.</summary>
    public string? Script { get; init; }

    /// <summary>Arguments for a script file.</summary>
    public string? Args { get; init; }

    public PowerShellEdition Edition { get; init; } = PowerShellEdition.Windows;

    public override string Describe() => ScriptFile is not null ? $"Run {Path.GetFileName(ScriptFile)}" : "Run PowerShell script";

    public override string Explain() => ScriptFile is not null
        ? $"Runs the PowerShell script {ScriptFile} {Args}".TrimEnd() + "."
        : $"Runs this PowerShell:{Environment.NewLine}{Script}";

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        if (context.DryRun)
        {
            context.Log.Info($"Would run {(ScriptFile is not null ? context.ResolvePath(ScriptFile) : "the inline script")}");
            return;
        }

        var startInfo = ScriptFile is not null
            ? PowerShellRunner.ForFile(context.ResolvePath(ScriptFile), context.Resolve(Args ?? ""), Edition)
            : PowerShellRunner.ForScript(context.Resolve(Script!), Edition);
        startInfo.WorkingDirectory = context.PackageDirectory;

        var result = await ProcessRunner.RunAsync(startInfo, context.Log, timeout: null, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"The script exited with code {result.ExitCode}.");
    }

    protected override IEnumerable<string> ValidateOptions()
    {
        if ((ScriptFile is null) == (Script is null))
            yield return "A powershell step needs either 'file' or 'script'.";
    }
}
