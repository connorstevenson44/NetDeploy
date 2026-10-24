using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NetDeploy.Core.Editing;

/// <summary>How a step property is edited.</summary>
public enum FieldKind { Text, Number, YesNo, Choice, Lines, Condition, Group, Inputs, Json }

/// <summary>One property of a step, as deploy.schema.json describes it. A Group has its own fields, such as a check.</summary>
public sealed record SchemaField(
    string Name, FieldKind Kind, string? Description, bool Required, IReadOnlyList<string> Options, IReadOnlyList<SchemaField> Fields)
{
    public string Label => ManifestSchema.Label(Name);
}

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

/// <summary>The step types and their properties, read from deploy.schema.json so the editor matches the schema.</summary>
public static partial class ManifestSchema
{
    // Properties the editor shows in its own way: the type as the heading, and if branches as steps in the list.
    private static readonly HashSet<string> Structural = ["type", "then", "else"];

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["args"] = "Arguments",
        ["arguments"] = "Arguments",
        ["checkPrerequisite"] = "Check a prerequisite",
        ["continueOnError"] = "Carry on if it fails",
        ["copyFiles"] = "Copy files",
        ["deleteExtra"] = "Delete extra files",
        ["deleteFolder"] = "Delete a folder",
        ["if"] = "If / else",
        ["powershell"] = "PowerShell",
        ["runAs"] = "Run as",
        ["runExe"] = "Run an exe",
        ["scheduledTask"] = "Scheduled task",
        ["takeNew"] = "Always take the package's value",
        ["valueName"] = "Value name",
        ["when"] = "Run only when",
        ["workingDir"] = "Working folder",
    };

    public static IReadOnlyList<StepSchema> Steps { get; } = Load();

    public static StepSchema? For(string? type) => Steps.FirstOrDefault(step => step.Type == type);

    /// <summary>A plain label for a property or type name: "timeoutMinutes" is "Timeout minutes".</summary>
    public static string Label(string name)
    {
        if (Labels.TryGetValue(name, out var label))
            return label;
        var words = CamelHump().Replace(name, " $1").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex CamelHump();

    private static List<StepSchema> Load()
    {
        using var stream = typeof(ManifestSchema).Assembly.GetManifestResourceStream("deploy.schema.json")!;
        var schema = JsonNode.Parse(stream)!.AsObject();
        var definitions = schema["definitions"]!.AsObject();

        // The step definition picks each type's own definition: { "if": type is X, "then": { "$ref": ... } }.
        return definitions["step"]!["allOf"]!.AsArray()
            .Select(rule => (Type: rule!["if"]!["properties"]!["type"]!["const"]!.GetValue<string>(), Ref: rule["then"]!["$ref"]!.GetValue<string>()))
            .Select(pair =>
            {
                var definition = Resolve(definitions, pair.Ref);
                var fields = Fields(definitions, definition).Where(field => !Structural.Contains(field.Name)).OrderBy(field => field.Name != "name").ToList();
                return new StepSchema(pair.Type, definition["description"]?.GetValue<string>(), fields);
            })
            .ToList();
    }

    private static List<SchemaField> Fields(JsonObject definitions, JsonObject definition)
    {
        var required = definition["required"]?.AsArray().Select(name => name!.GetValue<string>()).ToHashSet() ?? [];
        return (definition["properties"]?.AsObject() ?? [])
            .Select(property => Field(definitions, property.Key, property.Value!.AsObject(), required.Contains(property.Key)))
            .ToList();
    }

    private static SchemaField Field(JsonObject definitions, string name, JsonObject property, bool required)
    {
        var description = property["description"]?.GetValue<string>();
        if (property["$ref"]?.GetValue<string>() is { } reference)
        {
            var target = Resolve(definitions, reference);
            description ??= target["description"]?.GetValue<string>();
            return reference switch
            {
                "#/definitions/condition" => new(name, FieldKind.Condition, description, required, [], []),
                "#/definitions/inputs" => new(name, FieldKind.Inputs, description, required, [], []),
                _ => new(name, FieldKind.Group, description, required, [], Fields(definitions, target)),
            };
        }

        if (property["enum"] is JsonArray options)
            return new(name, FieldKind.Choice, description, required, options.Select(option => option!.GetValue<string>()).ToList(), []);

        var kind = property["type"]?.GetValue<string>() switch
        {
            "string" => FieldKind.Text,
            "integer" => FieldKind.Number,
            "boolean" => FieldKind.YesNo,
            "array" when property["items"]?["type"]?.GetValue<string>() == "string" => FieldKind.Lines,
            _ => FieldKind.Json,
        };
        return new(name, kind, description, required, [], []);
    }

    private static JsonObject Resolve(JsonObject definitions, string reference) =>
        definitions[reference["#/definitions/".Length..]]!.AsObject();
}
