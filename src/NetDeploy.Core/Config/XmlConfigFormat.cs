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

    public string Write(string incomingText, IEnumerable<KeyValuePair<string, string?>> changes)
    {
        var document = XDocument.Parse(incomingText, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidDataException("The config file has no root element.");

        foreach (var (path, value) in changes)
        {
            var separator = path.IndexOf(':');
            var section = Sections.Single(s => s.Name == path[..separator]);
            var key = path[(separator + 1)..];

            var container = root.Element(section.Name);
            if (container is null)
            {
                container = new XElement(section.Name);
                root.Add(container);
            }

            var existing = container.Elements("add").FirstOrDefault(
                e => string.Equals(e.Attribute(section.KeyAttribute)?.Value, key, StringComparison.OrdinalIgnoreCase));
            if (value is null)
                existing?.Remove();
            else if (existing is null)
                container.Add(XElement.Parse(value));
            else
                existing.ReplaceWith(XElement.Parse(value));
        }

        var declaration = document.Declaration is null ? "" : document.Declaration + Environment.NewLine;
        return declaration + document.ToString(SaveOptions.DisableFormatting);
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
