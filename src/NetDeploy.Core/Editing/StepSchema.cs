using System.Text.Json.Nodes;

namespace NetDeploy.Core.Editing;

/// <summary>A step type: what it does and the properties it takes, name first.</summary>
public sealed record StepSchema(string Type, string? Description, IReadOnlyList<SchemaField> Fields)
{
    public string Label => ManifestSchema.Label(Type);

    /// <summary>A new step with the required properties filled with a first choice or left empty to fill in.</summary>
    public JsonObject NewStep()
    {
        var step = new JsonObject { ["type"] = Type };
        Fill(step, Fields);
        return step;
    }

    private static void Fill(JsonObject owner, IEnumerable<SchemaField> fields)
    {
        foreach (var field in fields.Where(field => field.Required))
            owner[field.Name] = field.Kind switch
            {
                FieldKind.Choice => field.Options[0],
                FieldKind.Group => Group(field),
                FieldKind.Condition => new JsonObject { ["ask"] = "" },
                _ => "",
            };
    }

    private static JsonObject Group(SchemaField field)
    {
        var group = new JsonObject();
        Fill(group, field.Fields);
        return group;
    }
}
