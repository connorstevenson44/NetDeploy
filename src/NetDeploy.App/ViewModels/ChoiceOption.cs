using NetDeploy.Core.Editing;

namespace NetDeploy.App.ViewModels;

/// <summary>A choice as the schema names it, and its label on the form: fileExists is "File exists".</summary>
public sealed record ChoiceOption(string Value, string Label)
{
    public static IReadOnlyList<ChoiceOption> For(IEnumerable<string> values) =>
        values.Select(value => new ChoiceOption(value, value.Length == 0 ? "" : ManifestSchema.Label(value))).ToList();
}
