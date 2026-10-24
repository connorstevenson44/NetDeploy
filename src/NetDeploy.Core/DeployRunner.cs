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
        maskingLog.Info($"{(dryRun ? "Dry run of" : "Deploying")} {app.Name} {app.Version}");
        try
        {
            await RunStepsAsync(package.Manifest.Steps, context, cancellationToken);
            maskingLog.Info(dryRun ? "Dry run finished. Nothing was changed." : "Deploy finished.");
            return new DeployOutcome(DeployResult.Succeeded, context.Rollback);
        }
        catch (StepFailedException ex)
        {
            return new DeployOutcome(DeployResult.Failed, context.Rollback, ex.Step, ex.InnerException);
        }
        catch (OperationCanceledException ex)
        {
            maskingLog.Warn(ex.Message);
            return new DeployOutcome(DeployResult.Cancelled, context.Rollback);
        }
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

internal sealed class StepFailedException(DeployStep step, Exception inner) : Exception(inner.Message, inner)
{
    public DeployStep Step { get; } = step;
}
