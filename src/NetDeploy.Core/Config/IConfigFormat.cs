namespace NetDeploy.Core.Config;

/// <summary>
/// Turns a config file into flat "Section:Key" settings and back. Values are kept in a canonical text
/// form (JSON text, or an XML element) so they can be compared as strings.
/// </summary>
public interface IConfigFormat
{
    OrderedDictionary<string, string> Read(string text);

    /// <summary>Applies changes to the incoming file. A null value removes the setting.</summary>
    string Write(string incomingText, IEnumerable<KeyValuePair<string, string?>> changes);

    /// <summary>The value as a person would type it, without JSON quotes or XML markup.</summary>
    string Display(string value);

    /// <summary>Turns text the user typed into a canonical value, keeping the original value's kind.</summary>
    string ParseEdit(string original, string text);
}

public static class ConfigFormats
{
    public static IConfigFormat For(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => new JsonConfigFormat(),
        ".config" or ".xml" => new XmlConfigFormat(),
        var extension => throw new NotSupportedException($"NetDeploy can't merge '{extension}' files."),
    };
}
