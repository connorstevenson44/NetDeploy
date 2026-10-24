using System.Text;
using System.Text.RegularExpressions;

namespace NetDeploy.Core.Logs;

/// <summary>
/// Turns an NLog layout, such as "${basedir}/logs/${shortdate}.log", into a path pattern, such as
/// "C:\Apps\Orders\logs\*.log". What is fixed for the app is filled in: its folder, variables, special folders,
/// environment variables and the machine's name. What changes as it runs, such as the date, the level or the logger,
/// becomes *, as does the {#} of an archive's number.
/// </summary>
internal sealed partial class NLogLayout(string baseDir)
{
    // A variable can use another; this stops a loop.
    private const int MaxDepth = 10;

    /// <summary>The config's &lt;variable&gt;s, by name, used as ${var:name} or ${name}.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{#+\}")]
    private static partial Regex ArchiveNumber();

    [GeneratedRegex(@"\*{2,}")]
    private static partial Regex Stars();

    public string ToPattern(string layout)
    {
        var text = Stars().Replace(ArchiveNumber().Replace(Render(layout, 0), "*"), "*");
        // NLog writes a relative path under the app's folder.
        var path = Path.IsPathFullyQualified(text) ? text : Path.Combine(baseDir, text);
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private string Render(string layout, int depth)
    {
        var text = new StringBuilder();
        var i = 0;
        while (i < layout.Length)
        {
            if (layout[i] == '$' && i + 1 < layout.Length && layout[i + 1] == '{' && Close(layout, i + 2) is var end and >= 0)
            {
                text.Append(depth < MaxDepth ? Renderer(layout[(i + 2)..end], depth) : "*");
                i = end + 1;
            }
            else
                text.Append(layout[i++]);
        }
        return text.ToString();
    }

    /// <summary>One ${...}, such as "var:logDir" or "date:format=yyyy-MM-dd".</summary>
    private string Renderer(string spec, int depth)
    {
        var parts = Split(spec);
        var name = parts[0].Trim().ToLowerInvariant();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Skip(1))
        {
            var equals = part.IndexOf('=');
            // The first option without a name is the renderer's default one, as in ${var:logDir}.
            options.TryAdd(equals < 0 ? "" : part[..equals].Trim(), equals < 0 ? part : part[(equals + 1)..]);
        }
        string? Option(string key) => options.GetValueOrDefault(key) ?? options.GetValueOrDefault("");

        return name switch
        {
            "basedir" or "processdir" or "currentdir" => Folder(baseDir, options),
            "tempdir" => Folder(Path.GetTempPath(), options),
            "specialfolder" => Enum.TryParse<Environment.SpecialFolder>(Option("folder"), ignoreCase: true, out var folder)
                && Environment.GetFolderPath(folder) is { Length: > 0 } special ? Folder(special, options) : "*",
            "var" => Variable(Option("name"), options.GetValueOrDefault("default"), depth),
            "environment" => Environment.GetEnvironmentVariable(Option("variable") ?? "") is { Length: > 0 } value ? value : "*",
            "machinename" or "hostname" => Environment.MachineName,
            "literal" => Option("text") ?? "",
            "dir-separator" => Path.DirectorySeparatorChar.ToString(),
            // NLog also reads a variable as ${name}.
            _ when options.Count == 0 && Variables.ContainsKey(name) => Variable(name, null, depth),
            _ => "*",
        };
    }

    private string Variable(string? name, string? fallback, int depth) =>
        name is not null && Variables.TryGetValue(name.Trim(), out var value) ? Render(value, depth + 1) : fallback ?? "*";

    // ${basedir:dir=logs} and ${basedir:file=app.log} name something inside the folder.
    private static string Folder(string folder, Dictionary<string, string> options)
    {
        var path = Path.TrimEndingDirectorySeparator(folder);
        if (options.GetValueOrDefault("dir") is { Length: > 0 } dir)
            path = Path.Combine(path, dir);
        if (options.GetValueOrDefault("file") is { Length: > 0 } file)
            path = Path.Combine(path, file);
        return path;
    }

    /// <summary>Where the ${ that starts before start ends, allowing for ${ inside it; -1 when it doesn't.</summary>
    private static int Close(string layout, int start)
    {
        var depth = 0;
        for (var i = start; i < layout.Length; i++)
        {
            if (layout[i] == '\\')
                i++;
            else if (layout[i] == '$' && i + 1 < layout.Length && layout[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (layout[i] == '}' && depth-- == 0)
                return i;
        }
        return -1;
    }

    /// <summary>A renderer's name and options, split at the colons that aren't inside a nested ${}.</summary>
    private static List<string> Split(string spec)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < spec.Length; i++)
        {
            if (spec[i] == '\\')
                i++;
            else if (spec[i] == '$' && i + 1 < spec.Length && spec[i + 1] == '{')
                depth++;
            else if (spec[i] == '}')
                depth--;
            else if (spec[i] == ':' && depth == 0)
            {
                parts.Add(spec[start..i]);
                start = i + 1;
            }
        }
        parts.Add(spec[start..]);
        return parts;
    }
}
