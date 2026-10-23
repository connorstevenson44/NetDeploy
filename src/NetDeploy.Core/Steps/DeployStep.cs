using System.Text.Json.Serialization;

namespace NetDeploy.Core;

/// <summary>One step of a deploy. Each subclass is both the step's settings in deploy.json and the code that runs it.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Steps.CheckPrerequisiteStep), "checkPrerequisite")]
[JsonDerivedType(typeof(Steps.CopyFilesStep), "copyFiles")]
[JsonDerivedType(typeof(Steps.MergeConfigStep), "mergeConfig")]
[JsonDerivedType(typeof(Steps.RunExeStep), "runExe")]
[JsonDerivedType(typeof(Steps.PowerShellStep), "powershell")]
[JsonDerivedType(typeof(Steps.BackupStep), "backup")]
[JsonDerivedType(typeof(Steps.ServiceStep), "service")]
[JsonDerivedType(typeof(Steps.ScheduledTaskStep), "scheduledTask")]
[JsonDerivedType(typeof(Steps.IfStep), "if")]
public abstract class DeployStep
{
    /// <summary>Shown in the UI instead of the generated description.</summary>
    public string? Name { get; init; }

    /// <summary>The step is skipped when this is false.</summary>
    public Condition? When { get; init; }

    /// <summary>Asked just before the step runs; answers become {Name} values for this and later steps.</summary>
    public Dictionary<string, InputField>? Inputs { get; init; }

    public bool ContinueOnError { get; init; }

    [JsonIgnore]
    public string Title => Name ?? Describe();

    /// <summary>A one-line summary for the plan, with placeholders unresolved.</summary>
    public abstract string Describe();

    public abstract Task RunAsync(StepContext context, CancellationToken cancellationToken);

    public IEnumerable<string> Validate()
    {
        var conditionErrors = When?.Validate() ?? [];
        var inputErrors = (Inputs ?? []).SelectMany(input => input.Value.Validate(input.Key));
        return conditionErrors.Concat(inputErrors).Concat(ValidateOptions());
    }

    /// <summary>Checks rules between options that the JSON shape alone can't express.</summary>
    protected virtual IEnumerable<string> ValidateOptions() => [];
}
