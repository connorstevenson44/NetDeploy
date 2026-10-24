using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

/// <summary>Choices made in the UI before a deploy starts.</summary>
public sealed record DeployOptions
{
    public static DeployOptions Default { get; } = new();

    /// <summary>File actions picked in the file view, per copy step, by relative path.</summary>
    public IReadOnlyDictionary<DeployStep, IReadOnlyDictionary<string, FileAction>> FileActions { get; init; } =
        new Dictionary<DeployStep, IReadOnlyDictionary<string, FileAction>>();

    /// <summary>The instance to add or update; empty for a single install. Only used when the manifest has "install".</summary>
    public string Instance { get; init; } = "";

    /// <summary>
    /// An existing install whose files are copied into a new instance before the steps run, so a clone or rename keeps
    /// its config edits and its merge base.
    /// </summary>
    public string? SeedFrom { get; init; }
}
