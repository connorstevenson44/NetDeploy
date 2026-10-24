using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NetDeploy.Core.Config;

/// <summary>
/// Changes settings in JSON text in place: a value is swapped where it is written, a setting is added after the
/// last one in its section with the same indentation, and one taken out goes with its comma. Everything else, such as
/// comments, order and layout, stays as the file had it.
/// </summary>
internal static class JsonText
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>An object in the text: where its braces are and its members in order.</summary>
    internal sealed class Node(int open)
    {
        public int Open { get; } = open;
        public int Close { get; set; }
        public List<Member> Members { get; } = [];

        public Member? Find(string key) => Members.LastOrDefault(member => member.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A member of an object: where its key and value are, and its value as an object when it is one.</summary>
    internal sealed record Member(string Key, int KeyStart, int ValueStart, int ValueEnd, Node? Object);

    public static Node Parse(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var charAt = CharOffsets(text, bytes.Length);
        var reader = new Utf8JsonReader(bytes, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("The JSON file's root must be an object.");
        return ReadObject(ref reader, charAt);
    }

    /// <summary>Where each setting's value is written, by its "Parent:Child" path.</summary>
    public static Dictionary<string, (int Start, int End)> Locate(string text)
    {
        var spans = new Dictionary<string, (int Start, int End)>(StringComparer.OrdinalIgnoreCase);
        void Walk(Node node, string path)
        {
            foreach (var member in node.Members)
            {
                var childPath = path.Length == 0 ? member.Key : $"{path}:{member.Key}";
                if (member.Object is { Members.Count: > 0 } child)
                    Walk(child, childPath);
                else
                    spans[childPath] = (member.ValueStart, member.ValueEnd);
            }
        }
        Walk(Parse(text), "");
        return spans;
    }

    /// <summary>Sets a setting to a JSON value, or with null, takes it out.</summary>
    public static string Set(string text, string path, string? value)
    {
        var keys = path.Split(':');
        var node = Parse(text);
        for (var depth = 0; depth < keys.Length; depth++)
        {
            var member = node.Find(keys[depth]);
            var isLeaf = depth == keys.Length - 1;
            if (member is null)
                return value is null ? text : Add(text, node, keys[depth..], value);
            if (isLeaf || member.Object is null)
            {
                if (value is null)
                    return isLeaf ? Remove(text, member) : text;
                // A value where a section should be is replaced by the section.
                var replacement = isLeaf ? value : NewObject(keys[(depth + 1)..], value, Indent(text, member.KeyStart), Unit(text), NewLine(text));
                return text[..member.ValueStart] + replacement + text[member.ValueEnd..];
            }
            node = member.Object;
        }
        return text;
    }

    private static string Add(string text, Node node, string[] keys, string value)
    {
        var newLine = NewLine(text);
        var unit = Unit(text);
        var outer = Indent(text, node.Open);
        var last = node.Members.LastOrDefault();

        // An object on one line, such as { "a": 1 }, gets the member on that line too.
        if (last is not null && !text[node.Open..last.KeyStart].Contains('\n'))
            return Insert(text, last, ", " + MemberText(keys, value, "", unit, newLine));

        var indent = last is not null ? Indent(text, last.KeyStart) : outer + unit;
        var member = MemberText(keys, value, indent, unit, newLine);
        if (last is null)
            return text[..(node.Open + 1)] + newLine + indent + member + newLine + outer + text[node.Close..];
        return Insert(text, last, "," + newLine + indent + member);

        // After the last member and its trailing comma, if it has one.
        static string Insert(string text, Member last, string addition)
        {
            var at = SkipSpace(text, last.ValueEnd);
            if (at < text.Length && text[at] == ',')
                return text[..(at + 1)] + addition.TrimStart(',') + text[(at + 1)..];
            return text[..last.ValueEnd] + addition + text[last.ValueEnd..];
        }
    }

    private static string Remove(string text, Member member)
    {
        // With a comma after it, the member, its comma and the space before it go; the next member keeps its line.
        var before = member.KeyStart;
        while (before > 0 && char.IsWhiteSpace(text[before - 1]))
            before--;
        var after = SkipSpace(text, member.ValueEnd);
        if (after < text.Length && text[after] == ',')
            return text[..before] + text[(after + 1)..];
        // As the last member, the comma before it goes instead.
        if (before > 0 && text[before - 1] == ',')
            before--;
        return text[..before] + text[member.ValueEnd..];
    }

    private static string MemberText(string[] keys, string value, string indent, string unit, string newLine) =>
        Quote(keys[0]) + ": " + (keys.Length == 1 ? value : NewObject(keys[1..], value, indent, unit, newLine));

    private static string NewObject(string[] keys, string value, string indent, string unit, string newLine) =>
        "{" + newLine + indent + unit + MemberText(keys, value, indent + unit, unit, newLine) + newLine + indent + "}";

    private static string Quote(string key) => JsonSerializer.Serialize(key, Relaxed);

    private static int SkipSpace(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;
        return at;
    }

    // The spaces at the start of the line the position is on.
    private static string Indent(string text, int at)
    {
        var start = text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
        var end = start;
        while (end < at && text[end] is ' ' or '\t')
            end++;
        return text[start..end];
    }

    // One level of indentation: the first indented line's, or two spaces.
    private static string Unit(string text)
    {
        foreach (var line in text.Split('\n').Skip(1))
        {
            var indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            if (indent.Length > 0)
                return indent;
        }
        return "  ";
    }

    private static string NewLine(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    private static Node ReadObject(ref Utf8JsonReader reader, int[] charAt)
    {
        var node = new Node(charAt[(int)reader.TokenStartIndex]);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                node.Close = charAt[(int)reader.TokenStartIndex];
                return node;
            }
            var key = reader.GetString()!;
            var keyStart = charAt[(int)reader.TokenStartIndex];
            reader.Read();
            var valueStart = charAt[(int)reader.TokenStartIndex];
            Node? child = null;
            if (reader.TokenType == JsonTokenType.StartObject)
                child = ReadObject(ref reader, charAt);
            else if (reader.TokenType == JsonTokenType.StartArray)
                reader.Skip();
            node.Members.Add(new Member(key, keyStart, valueStart, charAt[(int)reader.BytesConsumed], child));
        }
        throw new JsonException("The JSON ends before its last object is closed.");
    }

    // The character each byte of the text's UTF-8 belongs to, and one past the end.
    private static int[] CharOffsets(string text, int byteCount)
    {
        var charAt = new int[byteCount + 1];
        var b = 0;
        for (var c = 0; c < text.Length; c++)
        {
            var size = char.IsHighSurrogate(text[c]) && c + 1 < text.Length ? 4 : text[c] < 0x80 ? 1 : text[c] < 0x800 ? 2 : 3;
            for (var i = 0; i < size; i++)
                charAt[b + i] = c;
            b += size;
            if (size == 4)
                c++;
        }
        charAt[byteCount] = text.Length;
        return charAt;
    }
}
