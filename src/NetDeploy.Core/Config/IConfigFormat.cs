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

    // How the file looks, so the merge view can show settings the way they appear in the file.

    /// <summary>Splits a setting path into its sections and, last, its key.</summary>
    IReadOnlyList<string> SplitPath(string path);

    /// <summary>A section's opening line; null is the file's root.</summary>
    string OpenSection(string? name);

    string CloseSection(string? name);

    /// <summary>The line a setting is written as.</summary>
    string Line(string key, string value);
}

public static class ConfigFormats
{
    /// <summary>True for the files NetDeploy treats as settings: appsettings*.json and *.config.</summary>
    public static bool IsConfig(string fileName) =>
        fileName.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static IConfigFormat For(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => new JsonConfigFormat(),
        ".config" or ".xml" => new XmlConfigFormat(),
        var extension => throw new NotSupportedException($"NetDeploy can't merge '{extension}' files."),
    };
}
