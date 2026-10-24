namespace NetDeploy.Core;

public enum StepState { Pending, Running, Succeeded, Skipped, Failed }

public enum DeployResult { Succeeded, Failed, Cancelled }

public sealed record DeployOutcome(DeployResult Result, RollbackPlan Rollback, DeployStep? FailedStep = null, Exception? Error = null);

/// <summary>Runs a package's steps in order, stopping at the first failure.</summary>
public sealed class DeployRunner(IUserInteraction user, IDeployLog log)
{
    /// <summary>Raised from the thread running the deploy, so UI handlers must marshal.</summary>
    public event Action<DeployStep, StepState>? StepChanged;

    public async Task<DeployOutcome> RunAsync(
        DeployPackage package, IReadOnlyDictionary<string, string> parameters, bool dryRun, DeployOptions? options, CancellationToken cancellationToken)
    {
        var maskingLog = new MaskingLog(log);
        var context = new StepContext(this)
        {
            PackageDirectory = package.RootPath,
            DryRun = dryRun,
            Log = maskingLog,
            User = user,
            Options = options ?? DeployOptions.Default,
        };

        context.Variables["PackageDir"] = package.RootPath;
        foreach (var (name, value) in parameters)
        {
            context.Variables[name] = value;
            if (package.Manifest.Parameters.GetValueOrDefault(name)?.Type == InputType.Secret)
                maskingLog.AddSecret(value);
        }

        var app = package.Manifest.App;
        maskingLog.Info($"{(dryRun ? "Dry run of" : "Deploying")} {app.Name} {app.Version}"
            + (context.Options.Instance.Length > 0 ? $" as {context.Options.Instance}" : ""));
        try
        {
            var instanceDirectory = SetInstance(package.Manifest.Install, context);
            if (context.Options.SeedFrom is { } seed && instanceDirectory is not null)
                SeedInstance(seed, instanceDirectory, context);
            await RunStepsAsync(package.Manifest.Steps, context, cancellationToken);
            maskingLog.Info(dryRun ? "Dry run finished. Nothing was changed." : "Deploy finished.");
            return new DeployOutcome(DeployResult.Succeeded, context.Rollback);
        }
        catch (StepFailedException ex)
        {
            return new DeployOutcome(DeployResult.Failed, context.Rollback, ex.Step, ex.InnerException);
        }
        // Steps report their own failures; these are from setting up the install before the first step.
        catch (Exception ex) when (ex is InstanceException or IOException or UnauthorizedAccessException)
        {
            maskingLog.Error(ex.Message);
            return new DeployOutcome(DeployResult.Failed, context.Rollback, Error: ex);
        }
        catch (OperationCanceledException ex)
        {
            maskingLog.Warn(ex.Message);
            return new DeployOutcome(DeployResult.Cancelled, context.Rollback);
        }
    }

    /// <summary>Sets {Instance}, {InstanceSuffix} and {InstanceDir}, and returns the install's folder.</summary>
    private static string? SetInstance(InstallInfo? install, StepContext context)
    {
        var instance = context.Options.Instance;
        if (install is null)
        {
            if (instance.Length > 0)
                throw new InstanceException("This package has no \"install\" block, so it can't be deployed as a named install.");
            return null;
        }
        if (instance.Length == 0)
            throw new InstanceException("Name the install: each one goes in its own folder inside the install folder.");
        if (Installations.CheckName(instance) is { } problem)
            throw new InstanceException($"Install name \"{instance}\": {problem}");

        foreach (var (name, value) in Installations.Variables(context.Resolve(install.Folder), instance))
            context.Variables[name] = value;
        return context.Variables["InstanceDir"];
    }

    /// <summary>
    /// Copies an existing install's files into a new install's folder, which must be empty, so a clone or rename keeps
    /// its config edits and data.
    /// </summary>
    private static void SeedInstance(string source, string target, StepContext context)
    {
        if (!Directory.Exists(source))
            throw new InstanceException($"{source} doesn't exist, so there is nothing to copy from.");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new InstanceException($"{target} already has files, so it can't be filled from {source}.");

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(source, file)).ToList();
        if (context.DryRun)
        {
            context.Log.Info($"Would copy {files.Count} files from {source} to {target}");
            return;
        }
        foreach (var file in files)
        {
            var destination = Path.Combine(target, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(source, file), destination);
        }
        context.Log.Info($"Copied {files.Count} files from {source} to {target}");
    }

    internal async Task RunStepsAsync(IReadOnlyList<DeployStep> steps, StepContext context, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
            await RunStepAsync(step, context, cancellationToken);
    }

    internal void MarkSkipped(IReadOnlyList<DeployStep> steps)
    {
        foreach (var step in steps)
        {
            StepChanged?.Invoke(step, StepState.Skipped);
            if (step is Steps.IfStep ifStep)
            {
                MarkSkipped(ifStep.Then);
                MarkSkipped(ifStep.Else);
            }
        }
    }

    private async Task RunStepAsync(DeployStep step, StepContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var title = context.Resolve(step.Title);
        try
        {
            if (step.When is { } when && !await when.EvaluateAsync(context, cancellationToken))
            {
                context.Log.Info($"Skipped: {title}");
                MarkSkipped([step]);
                return;
            }

            StepChanged?.Invoke(step, StepState.Running);
            context.Log.Info($"> {title}");

            if (step.Inputs is { Count: > 0 } inputs)
                await AskInputsAsync(title, inputs, context, cancellationToken);

            await step.RunAsync(context, cancellationToken);
            StepChanged?.Invoke(step, StepState.Succeeded);
        }
        catch (OperationCanceledException)
        {
            StepChanged?.Invoke(step, StepState.Failed);
            throw;
        }
        catch (StepFailedException)
        {
            // A step nested inside this one failed.
            StepChanged?.Invoke(step, StepState.Failed);
            throw;
        }
        catch (Exception ex)
        {
            context.Log.Error($"{title} failed: {ex.Message}");
            StepChanged?.Invoke(step, StepState.Failed);
            if (!step.ContinueOnError)
                throw new StepFailedException(step, ex);
        }
    }

    private static async Task AskInputsAsync(
        string title, Dictionary<string, InputField> inputs, StepContext context, CancellationToken cancellationToken)
    {
        var fields = inputs.ToDictionary(
            input => input.Key,
            input => input.Value with { Default = input.Value.Default is { } value ? context.Resolve(value) : null });

        var answers = await context.User.AskInputsAsync(title, fields, cancellationToken)
            ?? throw new OperationCanceledException("Cancelled while entering inputs.");

        foreach (var (name, value) in answers)
        {
            context.Variables[name] = value;
            if (inputs[name].Type == InputType.Secret && context.Log is MaskingLog maskingLog)
                maskingLog.AddSecret(value);
        }
    }
}

/// <summary>The chosen install can't be deployed, for example because it has no name or a new install's folder already has files.</summary>
internal sealed class InstanceException(string message) : Exception(message);

internal sealed class StepFailedException(DeployStep step, Exception inner) : Exception(inner.Message, inner)
{
    public DeployStep Step { get; } = step;
}
