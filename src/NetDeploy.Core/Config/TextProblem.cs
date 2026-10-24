using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace NetDeploy.Core.Config;

/// <summary>
/// Why a file's text can't be read, short enough for one line, and where in the text it goes wrong when the reader
/// says. Offset is a character offset into the text, or null when the reader didn't say where.
/// </summary>
public sealed partial record TextProblem(string Message, int? Offset)
{
    private const int MaxLength = 160;
    private const int MaxQuoted = 20;

    /// <summary>
    /// The problem a reader threw for the text. Its message is cut to one line: a JSON reader that meets a bad word
    /// quotes everything from it to the end of the file.
    /// </summary>
    public static TextProblem From(Exception ex, string text)
    {
        var (message, line, column) = ex switch
        {
            JsonException { LineNumber: { } jsonLine, BytePositionInLine: { } bytes } json =>
                (JsonPosition().Replace(ShortenQuoted(json.Message), ""), (int)jsonLine, ColumnOf(text, (int)jsonLine, bytes)),
            XmlException { LineNumber: > 0 } xml => (XmlPosition().Replace(xml.Message, ""), xml.LineNumber - 1, xml.LinePosition - 1),
            _ => (ex.Message, -1, -1),
        };
        message = OneLine(message);
        if (line < 0)
            return new TextProblem(message, null);
        var offset = Math.Min(LineStart(text, line) + Math.Max(column, 0), text.Length);
        return new TextProblem($"Line {line + 1}, column {Math.Max(column, 0) + 1}: {message}", offset);
    }

    // "'tru\n  "Next": 1 ...' is an invalid JSON literal" becomes "'tru' is an invalid JSON literal".
    private static string ShortenQuoted(string message)
    {
        var quoted = Quoted().Match(message);
        if (!quoted.Success || quoted.Value.Length <= MaxQuoted && !quoted.Value.Contains('\n'))
            return message;
        var word = quoted.Value.Split([' ', '\t', '\r', '\n', ',', '}', ']'], 2)[0];
        if (word.Length > MaxQuoted)
            word = word[..MaxQuoted] + "…";
        return message[..quoted.Index] + word + message[(quoted.Index + quoted.Length)..];
    }

    private static string OneLine(string message)
    {
        var line = message.Trim().Split('\n', 2)[0].TrimEnd('\r');
        return line.Length <= MaxLength ? line : line[..(MaxLength - 1)] + "…";
    }

    private static int LineStart(string text, int line)
    {
        var start = 0;
        for (var i = 0; i < line && start < text.Length; i++)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                return text.Length;
            start = end + 1;
        }
        return start;
    }

    // The JSON reader counts bytes of UTF-8 along the line; the editor counts characters.
    private static int ColumnOf(string text, int line, long bytes)
    {
        var (column, counted) = (0, 0L);
        foreach (var rune in text.AsSpan(LineStart(text, line)).EnumerateRunes())
        {
            if (counted >= bytes || rune.Value == '\n')
                break;
            counted += rune.Utf8SequenceLength;
            column += rune.Utf16SequenceLength;
        }
        return column;
    }

    // The quoted text at the start of a reader's message, up to the last "' is ", which ends the quote.
    [GeneratedRegex(@"(?<=^').*(?=' is )", RegexOptions.Singleline)]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\s*(Path: .* \| )?LineNumber: \d+ \| BytePositionInLine: \d+\.\s*$", RegexOptions.Singleline)]
    private static partial Regex JsonPosition();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.\s*$")]
    private static partial Regex XmlPosition();
}
