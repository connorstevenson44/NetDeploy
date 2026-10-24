using System.Text.Json.Serialization;

namespace NetDeploy.Core;

/// <summary>The contents of a package's deploy.json.</summary>
public sealed class DeployManifest
{
    /// <summary>Points editors at deploy.schema.json for autocomplete; not used by the tool.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public int SchemaVersion { get; init; }

    /// <summary>The NetDeploy whose editor last saved the file, so a file from another version can be spotted.</summary>
    [JsonPropertyName("netdeployVersion")]
    public string? NetDeployVersion { get; init; }
    public required AppInfo App { get; init; }
    public bool RequiresAdmin { get; init; }

    /// <summary>Values asked for once, before the first step runs.</summary>
    public Dictionary<string, InputField> Parameters { get; init; } = [];

    public List<DeployStep> Steps { get; init; } = [];

    /// <summary>Where the app is installed, and whether one server can run several named copies of it.</summary>
    public InstallInfo? Install { get; init; }
}

public sealed record AppInfo(string Name, string Version);

/// <summary>
/// The install folder. A single install goes straight into it; with instances, each named copy gets its own
/// subfolder. Steps use {InstanceDir} for the app's folder and {Instance} or {InstanceSuffix} in names.
/// </summary>
public sealed record InstallInfo
{
    /// <summary>Usually "{InstallDir}", a parameter.</summary>
    public required string Folder { get; init; }

    /// <summary>True when the app can run as several named instances side by side.</summary>
    public bool Instances { get; init; }
}

public enum InputType { Text, Secret, Number, YesNo, Choice, Folder, File }

/// <summary>A value the user is asked for, either as a manifest parameter or as a step input.</summary>
public sealed record InputField
{
    public string? Label { get; init; }
    public InputType Type { get; init; } = InputType.Text;
    public string? Default { get; init; }
    public List<string>? Options { get; init; }
    public bool Optional { get; init; }

    internal IEnumerable<string> Validate(string name)
    {
        if (Type == InputType.Choice && Options is not { Count: > 0 })
            yield return $"Input '{name}' is a choice but has no options.";
    }
}
