using System.Xml.Linq;

namespace NetDeploy.Core.Config;

/// <summary>
/// app.config and .exe.config. Only appSettings (by key) and connectionStrings (by name) are merged;
/// every other section comes from the new file, because those usually change with the code.
/// </summary>
internal sealed class XmlConfigFormat : IConfigFormat
{
    private sealed record Section(string Name, string KeyAttribute, string ValueAttribute);

    private static readonly Section[] Sections =
    [
        new("appSettings", "key", "value"),
        new("connectionStrings", "name", "connectionString"),
    ];

    public OrderedDictionary<string, string> Read(string text)
    {
        var root = XDocument.Parse(text).Root;
        var values = new OrderedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in Sections)
            foreach (var add in root?.Element(section.Name)?.Elements("add") ?? [])
                if (add.Attribute(section.KeyAttribute)?.Value is { } key)
                    values[$"{section.Name}:{key}"] = add.ToString(SaveOptions.DisableFormatting);
        return values;
    }

    public string Write(string text, IEnumerable<KeyValuePair<string, string?>> changes, IReadOnlyList<string>? sources = null)
    {
        var written = (sources ?? []).Select(source => (Text: source, Values: Read(source), Spans: Locate(source))).ToList();
        foreach (var (path, value) in changes)
        {
            var separator = path.IndexOf(':');
            var section = Sections.Single(s => s.Name == path[..separator]);
            var key = path[(separator + 1)..];

            // An element a source has keeps that source's layout, such as its quotes and spacing.
            var source = value is null ? default : written.FirstOrDefault(source => source.Values.GetValueOrDefault(path) == value);
            var raw = source.Text is null ? value : source.Text[source.Spans[path].Start..source.Spans[path].End];
            text = XmlText.Set(text, section.Name,
                element => string.Equals(element.Attribute(section.KeyAttribute)?.Value, key, StringComparison.OrdinalIgnoreCase), raw);
        }
        return text;
    }

    public IReadOnlyDictionary<string, (int Start, int End)> Locate(string text)
    {
        var root = XmlText.Parse(text).Root;
        var spans = new Dictionary<string, (int Start, int End)>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in Sections)
            foreach (var add in root?.Element(section.Name)?.Elements("add") ?? [])
                if (add.Attribute(section.KeyAttribute)?.Value is { } key)
                    spans[$"{section.Name}:{key}"] = XmlText.Span(text, add);
        return spans;
    }

    public string Display(string value) => ValueAttribute(XElement.Parse(value))?.Value ?? "";

    public string ParseEdit(string original, string text)
    {
        var element = XElement.Parse(original);
        if (ValueAttribute(element) is { } attribute)
            attribute.Value = text;
        else
            element.SetAttributeValue("value", text);
        return element.ToString(SaveOptions.DisableFormatting);
    }

    private static XAttribute? ValueAttribute(XElement element) =>
        element.Attribute("connectionString") ?? element.Attribute("value");
}
