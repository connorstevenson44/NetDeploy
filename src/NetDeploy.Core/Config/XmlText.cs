using System.Xml;
using System.Xml.Linq;

namespace NetDeploy.Core.Config;

/// <summary>
/// Changes the &lt;add /&gt; elements of a config file's sections in its text, in place: an element is swapped where it
/// is, a new one goes after the last in its section with the same indentation, and one taken out goes with its line.
/// The rest of the file stays as it was written.
/// </summary>
internal static class XmlText
{
    /// <summary>Where each element is written, from its &lt; to just after its end.</summary>
    public static (int Start, int End) Span(string text, XElement element)
    {
        var start = Offset(text, (IXmlLineInfo)element) - 1;
        var tagEnd = StartTagEnd(text, start);
        if (text[tagEnd - 2] == '/')
            return (start, tagEnd);
        var close = text.IndexOf("</" + element.Name.LocalName, tagEnd, StringComparison.Ordinal);
        return (start, text.IndexOf('>', close) + 1);
    }

    public static XDocument Parse(string text) => XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);

    /// <summary>Sets the element for a key in a section, or with null, takes it out.</summary>
    public static string Set(string text, string sectionName, Func<XElement, bool> isKey, string? element)
    {
        var root = Parse(text).Root ?? throw new InvalidDataException("The config file has no root element.");
        var section = root.Element(sectionName);
        if (section?.Elements("add").FirstOrDefault(isKey) is { } existing)
        {
            var (start, end) = Span(text, existing);
            return element is null ? RemoveLine(text, start, end) : text[..start] + element + text[end..];
        }
        if (element is null)
            return text;

        var newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        var unit = Unit(text);
        if (section?.Elements("add").LastOrDefault() is { } last)
        {
            var (start, end) = Span(text, last);
            return text[..end] + newLine + Indent(text, start) + element + text[end..];
        }
        if (section is not null)
        {
            var (start, end) = Span(text, section);
            var indent = Indent(text, start);
            var tagEnd = StartTagEnd(text, start);
            var inside = newLine + indent + unit + element + newLine + indent;
            if (tagEnd == end)
                return text[..start] + text[start..(tagEnd - 2)].TrimEnd() + ">" + inside + $"</{sectionName}>" + text[end..];
            var close = text.LastIndexOf("</", end, StringComparison.Ordinal);
            return text[..tagEnd] + text[tagEnd..close].TrimEnd() + inside + text[close..];
        }

        // No such section yet: it goes last in the root.
        var rootClose = text.LastIndexOf("</" + root.Name.LocalName, StringComparison.Ordinal);
        if (rootClose < 0)
            throw new InvalidDataException("The config file's root element has nothing in it to add a section to.");
        var outer = Indent(text, rootClose);
        var block = outer + unit + $"<{sectionName}>" + newLine + outer + unit + unit + element + newLine + outer + unit + $"</{sectionName}>" + newLine;
        var lineStart = LineStart(text, rootClose);
        return text[lineStart..rootClose].Trim().Length == 0
            ? text[..lineStart] + block + text[lineStart..]
            : text[..rootClose] + newLine + block + text[rootClose..];
    }

    // Takes out a span, with its whole line when nothing else is on it.
    private static string RemoveLine(string text, int start, int end)
    {
        var lineStart = LineStart(text, start);
        var lineEnd = text.IndexOf('\n', end) is var newLine and >= 0 ? newLine + 1 : text.Length;
        return text[lineStart..start].Trim().Length == 0 && text[end..lineEnd].Trim().Length == 0
            ? text[..lineStart] + text[lineEnd..]
            : text[..start] + text[end..];
    }

    // Just after the > that ends the tag starting at the position, skipping any > inside quotes.
    private static int StartTagEnd(string text, int start)
    {
        char? quote = null;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
                quote = c == quote ? null : quote;
            else if (c is '"' or '\'')
                quote = c;
            else if (c == '>')
                return i + 1;
        }
        throw new InvalidDataException("A tag in the config file isn't closed.");
    }

    // The character a line and column from the XML reader point at. The reader counts \r\n as one line break.
    private static int Offset(string text, IXmlLineInfo info)
    {
        var offset = 0;
        for (var line = 1; line < info.LineNumber; line++)
        {
            var next = text.IndexOfAny(['\r', '\n'], offset);
            offset = next + (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? 2 : 1);
        }
        return offset + info.LinePosition - 1;
    }

    private static int LineStart(string text, int at) => text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1 is var start && start <= at ? start : 0;

    private static string Indent(string text, int at)
    {
        var start = LineStart(text, at);
        var end = start;
        while (end < at && text[end] is ' ' or '\t')
            end++;
        return text[start..end];
    }

    private static string Unit(string text)
    {
        foreach (var line in text.Split('\n').Skip(1))
        {
            var indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            if (indent.Length > 0 && line.Trim().Length > 0)
                return indent;
        }
        return "  ";
    }
}
