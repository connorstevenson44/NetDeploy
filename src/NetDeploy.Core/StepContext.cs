namespace NetDeploy.Core;

/// <summary>What a step can see and use while it runs.</summary>
public sealed class StepContext
{
    private readonly DeployRunner _runner;

    internal StepContext(DeployRunner runner) => _runner = runner;

    public required string PackageDirectory { get; init; }
    public required bool DryRun { get; init; }
    public required IDeployLog Log { get; init; }
    public required IUserInteraction User { get; init; }

    /// <summary>Parameter and input values, used to resolve {Name} placeholders.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What to undo if the deploy fails.</summary>
    public RollbackPlan Rollback { get; } = new();

    public string Resolve(string text) => Placeholders.Resolve(text, Variables);

    /// <summary>Resolves a path inside the package. Throws if it points outside the package.</summary>
    public string PackagePath(string relativePath)
    {
        var root = Path.GetFullPath(PackageDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, Resolve(relativePath))).TrimEnd(Path.DirectorySeparatorChar);
        var isInside = full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!isInside)
            throw new InvalidOperationException($"'{relativePath}' points outside the package.");
        return full;
    }

    /// <summary>An absolute path is used as is; a relative path is inside the package.</summary>
    public string ResolvePath(string path)
    {
        var resolved = Resolve(path);
        return Path.IsPathRooted(resolved) ? Path.GetFullPath(resolved) : PackagePath(resolved);
    }

    internal Task RunNestedAsync(IReadOnlyList<DeployStep> steps, CancellationToken cancellationToken) =>
        _runner.RunStepsAsync(steps, this, cancellationToken);

    internal void SkipNested(IReadOnlyList<DeployStep> steps) => _runner.MarkSkipped(steps);
}
