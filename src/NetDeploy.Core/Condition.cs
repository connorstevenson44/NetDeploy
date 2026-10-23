using System.Text.Json.Serialization;

namespace NetDeploy.Core;

/// <summary>
/// A test used by a step's "when" and by the "if" step. Exactly one kind is set:
/// powershell, ask, value (with equals), all, any or not.
/// </summary>
public sealed class Condition
{
    /// <summary>A script whose last output line is True or False.</summary>
    public string? PowerShell { get; init; }

    /// <summary>A Yes/No question asked at run time.</summary>
    public string? Ask { get; init; }

    public string? Value { get; init; }

    [JsonPropertyName("equals")]
    public string? EqualsValue { get; init; }

    public List<Condition>? All { get; init; }
    public List<Condition>? Any { get; init; }
    public Condition? Not { get; init; }

    public async Task<bool> EvaluateAsync(StepContext context, CancellationToken cancellationToken)
    {
        if (PowerShell is not null)
            return await PowerShellRunner.EvaluateAsync(context.Resolve(PowerShell), context.Log, cancellationToken);

        if (Ask is not null)
            return await context.User.AskYesNoAsync(context.Resolve(Ask), cancellationToken);

        if (Value is not null)
            return string.Equals(context.Resolve(Value), context.Resolve(EqualsValue ?? ""), StringComparison.OrdinalIgnoreCase);

        if (All is not null)
        {
            foreach (var condition in All)
                if (!await condition.EvaluateAsync(context, cancellationToken))
                    return false;
            return true;
        }

        if (Any is not null)
        {
            foreach (var condition in Any)
                if (await condition.EvaluateAsync(context, cancellationToken))
                    return true;
            return false;
        }

        if (Not is not null)
            return !await Not.EvaluateAsync(context, cancellationToken);

        throw new InvalidOperationException("The condition is empty.");
    }

    public string Describe() =>
        PowerShell is not null ? $"PowerShell: {PowerShell}"
        : Ask is not null ? $"Ask: {Ask}"
        : Value is not null ? $"{Value} is {EqualsValue}"
        : All is not null ? string.Join(" and ", All.Select(c => c.Describe()))
        : Any is not null ? string.Join(" or ", Any.Select(c => c.Describe()))
        : Not is not null ? $"not ({Not.Describe()})"
        : "(empty)";

    internal IEnumerable<string> Validate()
    {
        object?[] kinds = [PowerShell, Ask, Value, All, Any, Not];
        if (kinds.Count(kind => kind is not null) != 1)
            yield return "A condition needs exactly one of: powershell, ask, value, all, any, not.";

        if (Value is not null && EqualsValue is null)
            yield return "A 'value' condition needs 'equals'.";

        var nested = (All ?? []).Concat(Any ?? []).Append(Not).OfType<Condition>();
        foreach (var error in nested.SelectMany(c => c.Validate()))
            yield return error;
    }
}
