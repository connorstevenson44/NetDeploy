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

    public OrderedDictionary<string, string> Read(string text)
    {
        // .NET configuration keys are case-insensitive.
        var values = new OrderedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Flatten(ParseObject(text), "", values);
        return values;
    }

    public string Write(string text, IEnumerable<KeyValuePair<string, string?>> changes, IReadOnlyList<string>? sources = null)
    {
        var written = (sources ?? []).Select(source => (Text: source, Values: Read(source), Spans: JsonText.Locate(source))).ToList();
        foreach (var (path, value) in changes)
        {
            // A value a source has keeps that source's layout, such as an array over several lines.
            var source = value is null ? default : written.FirstOrDefault(source => source.Values.GetValueOrDefault(path) == value);
            var raw = source.Text is null ? value : source.Text[source.Spans[path].Start..source.Spans[path].End];
            text = JsonText.Set(text, path, raw);
        }
        return text;
    }

    public IReadOnlyDictionary<string, (int Start, int End)> Locate(string text) => JsonText.Locate(text);

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
}
