namespace NetDeploy.Core.Config;

public static class ConfigFormats
{
    /// <summary>True for the files NetDeploy treats as settings: appsettings*.json and *.config.</summary>
    public static bool IsConfig(string fileName) =>
        fileName.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>The format for a file by its extension: JSON, XML, or else plain text, one value for the whole file.</summary>
    public static IConfigFormat For(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => new JsonConfigFormat(),
        ".config" or ".xml" => new XmlConfigFormat(),
        _ => PlainText,
    };

    public static IConfigFormat PlainText { get; } = new PlainTextFormat();
}
