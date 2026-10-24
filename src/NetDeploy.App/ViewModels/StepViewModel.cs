using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>A row in the plan. Steps inside an "if" are indented under it.</summary>
public sealed partial class StepViewModel : ObservableObject
{
    private StepViewModel(DeployStep step, int depth, string branch)
    {
        Step = step;
        Indent = new Thickness(depth * 20, 0, 0, 0);
        Branch = branch;
        Title = step.Title;
        Detail = "";
    }

    public DeployStep Step { get; }
    public Thickness Indent { get; }

    /// <summary>What kind of step this is, at a glance.</summary>
    public PackIconKind Icon => Step switch
    {
        CheckPrerequisiteStep => PackIconKind.ClipboardCheckOutline,
        ServiceStep => PackIconKind.CogOutline,
        BackupStep => PackIconKind.ArchiveOutline,
        CopyFilesStep => PackIconKind.ContentCopy,
        RunExeStep => PackIconKind.Play,
        PowerShellStep => PackIconKind.Console,
        ScheduledTaskStep => PackIconKind.CalendarClock,
        IfStep => PackIconKind.SourceBranch,
        _ => PackIconKind.CircleSmall,
    };

    /// <summary>"Then" or "Else" for steps inside an if, otherwise empty.</summary>
    public string Branch { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>The step's condition, shown on hover.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    public partial string Detail { get; set; }

    public bool HasDetail => Detail.Length > 0;

    [ObservableProperty]
    public partial StepState State { get; set; }

    /// <summary>Shows the step with the parameter values typed so far.</summary>
    public void Refresh(IReadOnlyDictionary<string, string> values)
    {
        Title = Placeholders.Resolve(Step.Title, values);
        Detail = Step.When is { } when ? "Only when " + Placeholders.Resolve(when.Describe(), values) : "";
    }

    public static IEnumerable<StepViewModel> Flatten(IEnumerable<DeployStep> steps, int depth = 0, string branch = "")
    {
        foreach (var step in steps)
        {
            yield return new StepViewModel(step, depth, branch);
            if (step is not IfStep ifStep)
                continue;
            foreach (var nested in Flatten(ifStep.Then, depth + 1, "Then: "))
                yield return nested;
            foreach (var nested in Flatten(ifStep.Else, depth + 1, "Else: "))
                yield return nested;
        }
    }
}
