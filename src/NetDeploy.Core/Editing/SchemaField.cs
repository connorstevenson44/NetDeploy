namespace NetDeploy.Core.Editing;

/// <summary>One property of a step, as deploy.schema.json describes it. A Group has its own fields, such as a check.</summary>
public sealed record SchemaField(
    string Name, FieldKind Kind, string? Description, bool Required, IReadOnlyList<string> Options, IReadOnlyList<SchemaField> Fields)
{
    public string Label => ManifestSchema.Label(Name);

    /// <summary>
    /// The kinds of the group this field applies to, such as fileExists for a check's path; empty when it applies to
    /// every kind. The schema names them at the start of the description: "fileExists: a file or folder."
    /// </summary>
    public IReadOnlyList<string> Kinds { get; init; } = [];

    public bool AppliesTo(string? kind) => Kinds.Count == 0 || Kinds.Contains(kind);
}
