namespace NetDeploy.Core.Steps;

/// <summary>Runs the "then" steps when the condition is true, otherwise the "else" steps.</summary>
public sealed class IfStep : DeployStep
{
    public required Condition Condition { get; init; }
    public List<DeployStep> Then { get; init; } = [];
    public List<DeployStep> Else { get; init; } = [];

    public override string Describe() => $"If {Condition.Describe()}";

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var isTrue = await Condition.EvaluateAsync(context, cancellationToken);
        context.Log.Info(isTrue ? "Condition is true." : "Condition is false.");
        context.SkipNested(isTrue ? Else : Then);
        await context.RunNestedAsync(isTrue ? Then : Else, cancellationToken);
    }

    protected override IEnumerable<string> ValidateOptions() =>
        Condition.Validate()
            .Concat(ManifestLoader.ValidateSteps(Then, "then"))
            .Concat(ManifestLoader.ValidateSteps(Else, "else"));
}
