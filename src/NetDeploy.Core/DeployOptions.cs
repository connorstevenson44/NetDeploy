using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

/// <summary>Choices made in the UI before a deploy starts.</summary>
public sealed record DeployOptions
{
    public static DeployOptions Default { get; } = new();

    /// <summary>File actions picked in the file view, per copy step, by relative path.</summary>
    public IReadOnlyDictionary<DeployStep, IReadOnlyDictionary<string, FileAction>> FileActions { get; init; } =
        new Dictionary<DeployStep, IReadOnlyDictionary<string, FileAction>>();
}
