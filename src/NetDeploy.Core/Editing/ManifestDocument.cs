using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Editing;

/// <summary>Something that stops a config from being read, and the step it is about, if any.</summary>
public sealed record ManifestProblem(string Message, JsonObject? Step);

/// <summary>
/// A deploy.json being edited. It is kept as JSON rather than as a DeployManifest so that what the editor doesn't show,
/// such as property order and the $schema line, is saved as it was.
/// </summary>
public sealed partial class ManifestDocument(JsonObject root)
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // Keeps quotes and apostrophes readable; JSON still escapes backslashes.
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public JsonObject Root { get; } = root;

    public JsonArray Steps => (Root["steps"] ??= new JsonArray()).AsArray();

    /// <summary>A small working config to start from: it installs one folder from the package.</summary>
    public static ManifestDocument New() => new(new JsonObject
    {
        ["schemaVersion"] = ManifestLoader.SupportedSchemaVersion,
        ["app"] = new JsonObject { ["name"] = "MyApp", ["version"] = "1.0.0" },
        ["install"] = new JsonObject { ["folder"] = "{InstallDir}" },
        ["parameters"] = new JsonObject
        {
            ["InstallDir"] = new JsonObject { ["label"] = "Install folder", ["type"] = "folder", ["default"] = @"C:\Apps\MyApp" },
        },
        ["steps"] = new JsonArray(new JsonObject { ["type"] = "copyFiles", ["from"] = "app", ["to"] = "{InstanceDir}" }),
    });

    /// <summary>
    /// Reads a deploy.json. Throws ManifestException when it isn't a JSON object, or when the parts the editor is built
    /// around, the steps, the app and the install, aren't the shape they must be.
    /// </summary>
    public static ManifestDocument Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject
                ?? throw new ManifestException(["deploy.json must be a JSON object."]);
        }
        catch (JsonException ex)
        {
            throw new ManifestException([ex.Message]);
        }

        string?[] wrongShape =
        [
            root["steps"] is null or JsonArray ? null : "\"steps\" must be a list: [ ... ].",
            root["app"] is null or JsonObject ? null : "\"app\" must be an object: { \"name\": ..., \"version\": ... }.",
            root["install"] is null or JsonObject ? null : "\"install\" must be an object: { \"folder\": ... }.",
        ];
        if (wrongShape.OfType<string>().ToList() is [_, ..] errors)
            throw new ManifestException(errors);
        return new(root);
    }

    public string ToJson() => Root.ToJsonString(WriteOptions) + Environment.NewLine;

    /// <summary>The NetDeploy that last saved the file, or null for one written by hand.</summary>
    public string? SavedWith => Root["netdeployVersion"] is JsonValue version && version.TryGetValue(out string? text) ? text : null;

    /// <summary>Records this NetDeploy as the one that saved the file, next to schemaVersion.</summary>
    public void StampVersion()
    {
        Root.Remove("netdeployVersion");
        Root.Insert(Root.IndexOf("schemaVersion") + 1, "netdeployVersion", ManifestLoader.NetDeployVersion);
    }

    /// <summary>What would stop NetDeploy from reading this config, each with the step it is about when there is one.</summary>
    public IReadOnlyList<ManifestProblem> Problems()
    {
        try
        {
            ManifestLoader.Parse(ToJson());
            return [];
        }
        catch (ManifestException ex)
        {
            return ex.Errors.Select(Locate).ToList();
        }
    }

    // Validation problems start with the step's path, such as "steps[2]: then[0]: ". A step that doesn't read at all
    // stops the whole file with a JSON error and no path, so the step is found by reading each one alone.
    private ManifestProblem Locate(string error)
    {
        JsonNode node = Root;
        var rest = error;
        while (ProblemPath().Match(rest) is { Success: true } part
            && node[part.Groups["list"].Value] is JsonArray list
            && int.Parse(part.Groups["index"].Value) is var index && index < list.Count)
        {
            node = list[index]!;
            rest = rest[part.Length..];
        }
        if (node != Root)
            return new(rest, node as JsonObject);

        var unreadable = Unreadable(Steps);
        return unreadable is null ? new(error, null) : new(Plain(error), unreadable);
    }

    /// <summary>The innermost step that doesn't read as a step on its own.</summary>
    private static JsonObject? Unreadable(JsonArray steps)
    {
        foreach (var step in steps.OfType<JsonObject>())
        {
            var inner = new[] { "then", "else", "cleanup" }.Select(list => step[list] as JsonArray).OfType<JsonArray>().Select(Unreadable).FirstOrDefault(found => found is not null);
            if (inner is not null)
                return inner;
            try
            {
                step.Deserialize<DeployStep>(ManifestLoader.Options);
            }
            catch (JsonException)
            {
                return step;
            }
        }
        return null;
    }

    /// <summary>The JSON reader's two most common complaints, in plain words.</summary>
    private static string Plain(string error)
    {
        if (MissingProperties().Match(error) is { Success: true } missing)
            return $"Fill in {missing.Groups["names"].Value}.";
        if (UnknownProperty().Match(error) is { Success: true } unknown)
            return $"'{unknown.Groups["name"].Value}' isn't a setting of this step.";
        return error;
    }

    [GeneratedRegex(@"^(?<list>steps|then|else|cleanup)\[(?<index>\d+)\]: ")]
    private static partial Regex ProblemPath();

    [GeneratedRegex("missing required properties including: (?<names>.+?)\\.?$")]
    private static partial Regex MissingProperties();

    [GeneratedRegex("The JSON property '(?<name>[^']+)' could not be mapped")]
    private static partial Regex UnknownProperty();

    /// <summary>The title the deploy screen shows for a step: its name, or a description built from its settings.</summary>
    public static string TitleOf(JsonObject step)
    {
        if (step["name"] is JsonValue name && name.TryGetValue(out string? text) && text.Length > 0)
            return text;
        try
        {
            return step.Deserialize<DeployStep>(ManifestLoader.Options)!.Describe();
        }
        catch (Exception)
        {
            // A step still being written may not read as a step yet, or describe itself; its type is enough then.
            return ManifestSchema.For(TypeOf(step))?.Label ?? "Unknown step";
        }
    }

    /// <summary>What removing the install undoes for a step without being told, or nothing while it doesn't read yet.</summary>
    public static IReadOnlyList<string> AutomaticCleanupOf(JsonObject step)
    {
        try
        {
            return step.Deserialize<DeployStep>(ManifestLoader.Options)!.AutomaticCleanup().ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static string? TypeOf(JsonObject step) => step["type"] is JsonValue type && type.TryGetValue(out string? text) ? text : null;
}
