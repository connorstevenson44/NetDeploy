namespace NetDeploy.Core.Config;

/// <summary>
/// Turns a config file into flat "Section:Key" settings and back. Values are kept in a canonical text
/// form (JSON text, or an XML element) so they can be compared as strings.
/// </summary>
public interface IConfigFormat
{
    OrderedDictionary<string, string> Read(string text);

    /// <summary>
    /// Applies changes to a file's text, in place, so the rest of the file keeps its layout. A null value removes the
    /// setting. A value that a source file has is written the way that file writes it.
    /// </summary>
    string Write(string text, IEnumerable<KeyValuePair<string, string?>> changes, IReadOnlyList<string>? sources = null);

    /// <summary>Where each setting is written in the text, by its path.</summary>
    IReadOnlyDictionary<string, (int Start, int End)> Locate(string text);

    /// <summary>The value as a person would type it, without JSON quotes or XML markup.</summary>
    string Display(string value);

    /// <summary>Turns text the user typed into a canonical value, keeping the original value's kind.</summary>
    string ParseEdit(string original, string text);
}
