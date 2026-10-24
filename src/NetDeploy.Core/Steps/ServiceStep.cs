using System.Diagnostics;

namespace NetDeploy.Core.Steps;

public enum ServiceAction { Install, Start, Stop, Uninstall }

public enum ServiceStartup { Automatic, DelayedAutomatic, Manual, Disabled }

/// <summary>Manages a Windows service. "install" creates the service, or updates it if it already exists.</summary>
public sealed class ServiceStep : DeployStep
{
    public required ServiceAction Action { get; init; }
    public required string Service { get; init; }

    public string? Binary { get; init; }
    public string? DisplayName { get; init; }
    public ServiceStartup Startup { get; init; } = ServiceStartup.Automatic;

    /// <summary>Account to run as, such as "NT AUTHORITY\NetworkService" or ".\svc-orders". Defaults to LocalSystem.</summary>
    public string? Account { get; init; }

    public string? Password { get; init; }
    public int TimeoutSeconds { get; init; } = 60;

    public override string Describe() => $"{Action} service {Service}";

    public override string Explain() => Action switch
    {
        ServiceAction.Uninstall => $"Stops the Windows service \"{Service}\" and deletes it: sc.exe stop {Service}, then sc.exe delete {Service}.",
        ServiceAction.Stop => $"Stops the Windows service \"{Service}\".",
        ServiceAction.Start => $"Starts the Windows service \"{Service}\".",
        _ => $"Creates or updates the Windows service \"{Service}\" to run {Binary}.",
    };

    protected internal override IEnumerable<DeployStep> DefaultCleanup() =>
        Action == ServiceAction.Install ? [new ServiceStep { Action = ServiceAction.Uninstall, Service = Service, TimeoutSeconds = TimeoutSeconds }] : [];

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var name = context.Resolve(Service);
        var exists = WindowsServices.Exists(name);
        var timeout = TimeSpan.FromSeconds(TimeoutSeconds);

        if (!exists && Action is ServiceAction.Stop or ServiceAction.Uninstall)
        {
            context.Log.Info($"Service {name} is not installed.");
            return;
        }

        if (context.DryRun)
        {
            var verb = Action == ServiceAction.Install ? (exists ? "update" : "create") : Action.ToString().ToLowerInvariant();
            context.Log.Info($"Would {verb} service {name}");
            return;
        }

        switch (Action)
        {
            case ServiceAction.Stop:
                if (WindowsServices.Stop(name, timeout))
                    context.Rollback.StoppedServices.Add(name);
                break;

            case ServiceAction.Start:
                // A service that was already running isn't the deploy's to stop on a rollback.
                if (WindowsServices.Start(name, timeout))
                    context.Rollback.StartedServices.Add(name);
                break;

            case ServiceAction.Install:
                await RunScAsync([exists ? "config" : "create", name, .. InstallArguments(context)], context, cancellationToken);
                break;

            case ServiceAction.Uninstall:
                WindowsServices.Stop(name, timeout);
                await RunScAsync(["delete", name], context, cancellationToken);
                break;
        }

        context.Log.Info($"Service {name}: {Action.ToString().ToLowerInvariant()} done.");
    }

    private IEnumerable<string> InstallArguments(StepContext context)
    {
        // sc.exe wants "option=" and its value as separate arguments; the quotes keep paths with spaces intact.
        yield return "binPath=";
        yield return $"\"{context.Resolve(Binary!)}\"";
        yield return "start=";
        yield return Startup switch
        {
            ServiceStartup.DelayedAutomatic => "delayed-auto",
            ServiceStartup.Manual => "demand",
            ServiceStartup.Disabled => "disabled",
            _ => "auto",
        };

        if (DisplayName is not null)
        {
            yield return "DisplayName=";
            yield return context.Resolve(DisplayName);
        }

        if (Account is not null)
        {
            yield return "obj=";
            yield return context.Resolve(Account);
            if (Password is not null)
            {
                yield return "password=";
                yield return context.Resolve(Password);
            }
        }
    }

    private static async Task RunScAsync(IEnumerable<string> arguments, StepContext context, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(new ProcessStartInfo("sc.exe", arguments), context.Log, TimeSpan.FromMinutes(1), cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"sc.exe exited with code {result.ExitCode}.");
    }

    protected override IEnumerable<string> ValidateOptions()
    {
        if (Action == ServiceAction.Install && Binary is null)
            yield return "Installing a service needs 'binary'.";
    }
}
