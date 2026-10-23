using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>A row in the plan. Steps inside an "if" are indented under it.</summary>
public sealed partial class StepViewModel : ObservableObject
{
    private StepViewModel(DeployStep step, int depth, string branch)
    {
        Step = step;
        Indent = new Thickness(depth * 20, 3, 0, 3);
        Branch = branch;
        Title = step.Title;
        Detail = "";
    }

    public DeployStep Step { get; }
    public Thickness Indent { get; }

    /// <summary>"Then" or "Else" for steps inside an if, otherwise empty.</summary>
    public string Branch { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial StepState State { get; set; }

    public string Glyph => State switch
    {
        StepState.Running => "▶",
        StepState.Succeeded => "✓",
        StepState.Skipped => "–",
        StepState.Failed => "✗",
        _ => "○",
    };

    /// <summary>Shows the step with the parameter values typed so far.</summary>
    public void Refresh(IReadOnlyDictionary<string, string> values)
    {
        Title = Placeholders.Resolve(Step.Title, values);
        Detail = Step.When is { } when ? "When " + Placeholders.Resolve(when.Describe(), values) : "";
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
