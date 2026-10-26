using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Deployment;

/// <summary>Choices made in the UI before a deploy starts.</summary>
public sealed record DeployOptions
{
    public static DeployOptions Default { get; } = new();

    /// <summary>File actions picked in the file view, per copy step, by relative path.</summary>
    public IReadOnlyDictionary<DeployStep, IReadOnlyDictionary<string, FileAction>> FileActions { get; init; } =
        new Dictionary<DeployStep, IReadOnlyDictionary<string, FileAction>>();

    /// <summary>The name of the install to add or update. Required when the manifest has "install", unused otherwise.</summary>
    public string Instance { get; init; } = "";

    /// <summary>
    /// An existing install whose files are copied into a new install before the steps run, so a clone or rename keeps
    /// its config edits and its merge base.
    /// </summary>
    public string? SeedFrom { get; init; }
}
