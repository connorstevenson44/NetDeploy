using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetDeploy.Core.Config;

/// <summary>
/// appsettings.json. Objects are flattened to "Parent:Child" paths like .NET configuration does.
/// Arrays are one value, never merged item by item.
/// </summary>
internal sealed class JsonConfigFormat : IConfigFormat
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // Relaxed escaping keeps connection strings readable instead of turning ' into '.
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Indented = new(Compact) { WriteIndented = true };

    public OrderedDictionary<string, string> Read(string text)
    {
        // .NET configuration keys are case-insensitive.
        var values = new OrderedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Flatten(ParseObject(text), "", values);
        return values;
    }

    public string Write(string incomingText, IEnumerable<KeyValuePair<string, string?>> changes)
    {
        var root = ParseObject(incomingText);
        foreach (var (path, value) in changes)
            Apply(root, path.Split(':'), value);
        return root.ToJsonString(Indented);
    }

    public string Display(string value) =>
        value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value)! : value;

    public string ParseEdit(string original, string text)
    {
        if (original.StartsWith('"'))
            return JsonSerializer.Serialize(text, Compact);
        try
        {
            return JsonNode.Parse(text)?.ToJsonString(Compact) ?? "null";
        }
        catch (JsonException)
        {
            // Not a number, bool or JSON value, so it can only be a string.
            return JsonSerializer.Serialize(text, Compact);
        }
    }

    public IReadOnlyList<string> SplitPath(string path) => path.Split(':');

    public string OpenSection(string? name) => name is null ? "{" : $"{Quote(name)}: {{";

    public string CloseSection(string? name) => "}";

    public string Line(string key, string value) => $"{Quote(key)}: {value}";

    private static string Quote(string key) => JsonSerializer.Serialize(key, Compact);

    private static JsonObject ParseObject(string text) =>
        JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
        ?? throw new InvalidDataException("The JSON file's root must be an object.");

    private static void Flatten(JsonObject node, string path, OrderedDictionary<string, string> values)
    {
        foreach (var (key, child) in node)
        {
            var childPath = path.Length == 0 ? key : $"{path}:{key}";
            if (child is JsonObject { Count: > 0 } childObject)
                Flatten(childObject, childPath, values);
            else
                values[childPath] = child?.ToJsonString(Compact) ?? "null";
        }
    }

    private static void Apply(JsonObject root, string[] keys, string? value)
    {
        var parent = root;
        foreach (var key in keys[..^1])
        {
            var existing = FindKey(parent, key);
            if (existing is not null && parent[existing] is JsonObject child)
            {
                parent = child;
                continue;
            }
            if (value is null)
                return; // Nothing to remove.
            var created = new JsonObject();
            parent[existing ?? key] = created;
            parent = created;
        }

        var leaf = FindKey(parent, keys[^1]) ?? keys[^1];
        if (value is null)
            parent.Remove(leaf);
        else
            parent[leaf] = JsonNode.Parse(value);
    }

    // Keys are case-insensitive, so reuse whatever casing the file already has.
    private static string? FindKey(JsonObject node, string key) =>
        node.Select(property => property.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
}
