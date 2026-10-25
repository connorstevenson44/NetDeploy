namespace NetDeploy.Core.Config;

/// <summary>A config file the deploy will write, with the merge to review.</summary>
public sealed record ConfigFileUpdate(string TargetPath, ConfigFileKind Kind, MergePlan Plan, string? PackageText)
{
    public string FileName => Path.GetFileName(TargetPath);

    /// <summary>True when the user should look at this file before it is written.</summary>
    public bool NeedsReview => Kind == ConfigFileKind.New || Plan.Rows.Any(row => row.IsChanged || row.Choice is null);
}
