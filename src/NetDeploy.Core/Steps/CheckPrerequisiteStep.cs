using System.Diagnostics;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Processes;

namespace NetDeploy.Core.Steps;

/// <summary>Confirms something is installed, and runs the bundled installer when it is not.</summary>
public sealed class CheckPrerequisiteStep : DeployStep
{
    public required PrerequisiteCheck Check { get; init; }

    /// <summary>Installer inside the package, run when the check fails.</summary>
    public string? Installer { get; init; }

    public string? InstallerArgs { get; init; }

    public override string Describe() => $"Check {Check.Describe()}";

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        if (await Check.IsSatisfiedAsync(context, cancellationToken))
        {
            context.Log.Info("Already installed.");
            return;
        }

        if (Installer is null)
            throw new InvalidOperationException("Not installed, and the package has no installer for it.");

        var installer = context.ResolvePath(Installer);
        if (context.DryRun)
        {
            context.Log.Info($"Not installed. Would run {installer}");
            return;
        }

        context.Log.Info($"Not installed. Running {installer}");
        var startInfo = new ProcessStartInfo(installer, context.Resolve(InstallerArgs ?? ""));
        var result = await ProcessRunner.RunAsync(startInfo, context.Log, timeout: null, cancellationToken);

        const int SuccessRestartRequired = 3010;
        if (result.ExitCode == SuccessRestartRequired)
            context.Log.Warn("The installer asks for a restart.");
        else if (result.ExitCode != 0)
            throw new InvalidOperationException($"The installer exited with code {result.ExitCode}.");

        if (!await Check.IsSatisfiedAsync(context, cancellationToken))
            throw new InvalidOperationException("The installer finished, but the prerequisite is still missing.");
    }

    protected override IEnumerable<string> ValidateOptions() => Check.Validate();
}
