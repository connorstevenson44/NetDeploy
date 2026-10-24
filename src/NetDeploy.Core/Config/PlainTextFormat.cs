namespace NetDeploy.Core.Config;

/// <summary>Any other text file: the whole file is one value, so it can be edited and reviewed like a settings file.</summary>
internal sealed class PlainTextFormat : IConfigFormat
{
    /// <summary>The path of the one value.</summary>
    public const string WholeFile = "";

    public OrderedDictionary<string, string> Read(string text) => new() { [WholeFile] = text };

    public string Write(string text, IEnumerable<KeyValuePair<string, string?>> changes, IReadOnlyList<string>? sources = null) =>
        changes.Aggregate(text, (written, change) => change.Value ?? "");

    public IReadOnlyDictionary<string, (int Start, int End)> Locate(string text) =>
        new Dictionary<string, (int Start, int End)> { [WholeFile] = (0, text.Length) };

    public string Display(string value) => value;

    public string ParseEdit(string original, string text) => text;
}
