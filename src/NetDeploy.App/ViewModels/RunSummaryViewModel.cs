using System.Diagnostics.CodeAnalysis;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>What a dry run found the deploy would change, in a few lines, so nobody has to read the log for it.</summary>
public sealed class RunSummaryViewModel(IReadOnlyList<SummaryLine> lines)
{
    [SuppressMessage("Performance", "CA1822", Justification = "Bound from XAML, which needs an instance property.")]
    public string Title => "Dry run: what the deploy would change";
    public IReadOnlyList<SummaryLine> Lines { get; } = lines;

    /// <summary>The files from the files review, then each step that would change the server, then what was skipped.</summary>
    public static RunSummaryViewModel For(IEnumerable<FilePlanViewModel> plans, IEnumerable<StepViewModel> steps)
    {
        var lines = new List<SummaryLine>();
        var copies = plans.ToList();
        if (copies.Count > 0)
        {
            var added = copies.Sum(plan => plan.NewCount);
            var merged = copies.Sum(plan => plan.MergeCount);
            var replaced = copies.Sum(plan => plan.ReplaceCount) - merged;
            var deleted = copies.Sum(plan => plan.DeleteCount);
            lines.Add(new(PackIconKind.FileMultipleOutline, added + replaced + deleted == 0
                ? "No files would change."
                : $"Files: {added} added, {replaced} replaced, {deleted} deleted."));
            if (merged > 0)
                lines.Add(new(PackIconKind.SourceMerge, $"{Words.Count(merged, "config file", "config files")} merged with the server's copy."));
        }

        var all = steps.ToList();
        foreach (var step in all.Where(step => step.State == StepState.Succeeded && Changes(step.Step)))
            lines.Add(new(step.Icon, step.Branch + step.Title));

        var skipped = all.Count(step => step.State == StepState.Skipped);
        if (skipped > 0)
            lines.Add(new(PackIconKind.MinusCircleOutline, $"{Words.Count(skipped, "step", "steps")} skipped by their conditions."));
        return new RunSummaryViewModel(lines);
    }

    // Steps that change the server beyond the files, which the first line covers.
    private static bool Changes(DeployStep step) =>
        step is ServiceStep or ScheduledTaskStep or RunExeStep or PowerShellStep or BackupStep or DeleteFolderStep;
}
