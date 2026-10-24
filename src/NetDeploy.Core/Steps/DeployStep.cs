using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetDeploy.Core;

/// <summary>One step of a deploy. Each subclass is both the step's settings in deploy.json and the code that runs it.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Steps.CheckPrerequisiteStep), "checkPrerequisite")]
[JsonDerivedType(typeof(Steps.CopyFilesStep), "copyFiles")]
[JsonDerivedType(typeof(Steps.RunExeStep), "runExe")]
[JsonDerivedType(typeof(Steps.PowerShellStep), "powershell")]
[JsonDerivedType(typeof(Steps.BackupStep), "backup")]
[JsonDerivedType(typeof(Steps.ServiceStep), "service")]
[JsonDerivedType(typeof(Steps.ScheduledTaskStep), "scheduledTask")]
[JsonDerivedType(typeof(Steps.IfStep), "if")]
[JsonDerivedType(typeof(Steps.DeleteFolderStep), "deleteFolder")]
public abstract class DeployStep
{
    private static readonly JsonSerializerOptions CopyOptions = new(ManifestLoader.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    /// <summary>Shown in the UI instead of the generated description.</summary>
    public string? Name { get; init; }

    /// <summary>The step is skipped when this is false.</summary>
    public Condition? When { get; init; }

    /// <summary>Asked just before the step runs; answers become {Name} values for this and later steps.</summary>
    public Dictionary<string, InputField>? Inputs { get; init; }

    public bool ContinueOnError { get; init; }

    /// <summary>
    /// Steps that undo this one when the install is removed. Replaces the built-in undo (see DefaultCleanup);
    /// an empty list means nothing to undo.
    /// </summary>
    public List<DeployStep>? Cleanup { get; init; }

    [JsonIgnore]
    public string Title => Name ?? Describe();

    /// <summary>A one-line summary for the plan, with placeholders unresolved.</summary>
    public abstract string Describe();

    /// <summary>Exactly what running the step does, for the confirmation shown before a cleanup step runs.</summary>
    public virtual string Explain() => Title + ".";

    /// <summary>The undo for what this step creates, such as removing a service it installed.</summary>
    protected internal virtual IEnumerable<DeployStep> DefaultCleanup() => [];

    /// <summary>The steps that undo this one: its own cleanup when it has one, otherwise the built-in undo.</summary>
    public IEnumerable<DeployStep> CleanupSteps() => Cleanup ?? DefaultCleanup();

    /// <summary>
    /// A copy with {Name} placeholders filled in. It works on the step's JSON, so every option is covered without each
    /// step type copying itself. Values are JSON-escaped first, so paths with backslashes stay valid.
    /// </summary>
    public DeployStep WithValues(IReadOnlyDictionary<string, string> values)
    {
        var escaped = values.ToDictionary(pair => pair.Key, pair => JsonEncodedText.Encode(pair.Value).ToString(), StringComparer.OrdinalIgnoreCase);
        var json = Placeholders.Resolve(JsonSerializer.Serialize(this, CopyOptions), escaped);
        return JsonSerializer.Deserialize<DeployStep>(json, CopyOptions)!;
    }

    /// <summary>The {Name} placeholders still in the step, such as a secret nobody has entered yet.</summary>
    public IReadOnlyList<string> UnresolvedNames() => Placeholders.Unresolved(JsonSerializer.Serialize(this, CopyOptions));

    public abstract Task RunAsync(StepContext context, CancellationToken cancellationToken);

    public IEnumerable<string> Validate()
    {
        var conditionErrors = When?.Validate() ?? [];
        var inputErrors = (Inputs ?? []).SelectMany(input => input.Value.Validate(input.Key));
        var cleanupErrors = ManifestLoader.ValidateSteps(Cleanup ?? [], "cleanup");
        return conditionErrors.Concat(inputErrors).Concat(ValidateOptions()).Concat(cleanupErrors);
    }

    /// <summary>Checks rules between options that the JSON shape alone can't express.</summary>
    protected virtual IEnumerable<string> ValidateOptions() => [];
}
