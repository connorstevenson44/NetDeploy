using System.Text.Json.Serialization;
using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Manifest;

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
