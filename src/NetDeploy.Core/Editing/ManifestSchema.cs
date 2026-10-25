using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NetDeploy.Core.Editing;

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
        ["core"] = "PowerShell 7",
        ["createOrUpdate"] = "Create or update",
        ["delayedAutomatic"] = "Automatic (delayed start)",
        ["deleteExtra"] = "Delete extra files",
        ["deleteFolder"] = "Delete a folder",
        ["dotnetRuntime"] = ".NET runtime",
        ["everyMinutes"] = "Every few minutes",
        ["if"] = "If / else",
        ["onStart"] = "When Windows starts",
        ["powershell"] = "PowerShell",
        ["runAs"] = "Run as",
        ["runExe"] = "Run an exe",
        ["scheduledTask"] = "Scheduled task",
        ["takeNew"] = "Always take the package's value",
        ["valueName"] = "Value name",
        ["when"] = "Run only when",
        ["windows"] = "Windows PowerShell 5.1",
        ["workingDir"] = "Working folder",
        ["yesNo"] = "Yes / no",
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
        var fields = (definition["properties"]?.AsObject() ?? [])
            .Select(property => Field(definitions, property.Key, property.Value!.AsObject(), required.Contains(property.Key)))
            .ToList();
        var kinds = fields.FirstOrDefault(field => field.Name == "kind")?.Options ?? [];
        return kinds.Count == 0 ? fields : fields.Select(field => ForKinds(field, kinds)).ToList();
    }

    // "dotnetRuntime, installedProgram: the lowest version." applies to those two kinds, and the help drops the prefix.
    private static SchemaField ForKinds(SchemaField field, IReadOnlyList<string> kinds)
    {
        var match = KindsPrefix().Match(field.Description ?? "");
        var named = match.Groups["kinds"].Value.Split(", ");
        if (!match.Success || !named.All(kinds.Contains))
            return field;
        var help = match.Groups["help"].Value;
        return field with { Description = char.ToUpperInvariant(help[0]) + help[1..], Kinds = named };
    }

    [GeneratedRegex(@"^(?<kinds>[a-zA-Z]+(, [a-zA-Z]+)*): (?<help>.+)$", RegexOptions.Singleline)]
    private static partial Regex KindsPrefix();

    private static SchemaField Field(JsonObject definitions, string name, JsonObject property, bool required)
    {
        var description = property["description"]?.GetValue<string>();
        if (name == "cleanup")
            return new(name, FieldKind.Cleanup, description, required, [], []);
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
