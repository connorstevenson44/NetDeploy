using System.Diagnostics;
using System.Text.Json.Serialization;

namespace NetDeploy.Core.Steps;

public sealed class RunExeStep : DeployStep
{
    /// <summary>Relative paths are inside the package; absolute paths are on the server.</summary>
    [JsonPropertyName("path")]
    public required string ExePath { get; init; }

    public string? Args { get; init; }

    /// <summary>Defaults to the exe's own folder.</summary>
    public string? WorkingDir { get; init; }

    public List<int> SuccessExitCodes { get; init; } = [0];
    public int? TimeoutMinutes { get; init; }

    public override string Describe() => $"Run {Path.GetFileName(ExePath)}";

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var exe = context.ResolvePath(ExePath);
        var args = context.Resolve(Args ?? "");
        if (context.DryRun)
        {
            context.Log.Info($"Would run {exe} {args}");
            return;
        }

        var startInfo = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = WorkingDir is null ? Path.GetDirectoryName(exe)! : context.ResolvePath(WorkingDir),
        };
        var timeout = TimeoutMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
        var result = await ProcessRunner.RunAsync(startInfo, context.Log, timeout, cancellationToken);
        if (!SuccessExitCodes.Contains(result.ExitCode))
            throw new InvalidOperationException($"{Path.GetFileName(exe)} exited with code {result.ExitCode}.");
    }
}
