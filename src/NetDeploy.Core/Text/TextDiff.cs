using System.Text.RegularExpressions;

namespace NetDeploy.Core.Text;

/// <summary>
/// Finds what changed between two texts, word by word, the way a word processor tracks changes. Lines are compared
/// first, then the words of the lines that differ, and changes split only by spaces or punctuation are joined into one.
/// </summary>
public static partial class TextDiff
{
    // Past this many differing lines or words a block counts as replaced whole, which keeps big rewrites quick.
    private const int MaxEdits = 2000;

    public static IReadOnlyList<TextChange> Compare(string oldText, string newText)
    {
        var oldLines = Lines(oldText);
        var newLines = Lines(newText);
        var ids = new Dictionary<string, int>();
        var changes = new List<TextChange>();
        foreach (var (a, aLength, b, bLength) in Diff(Ids(oldLines, ids), Ids(newLines, ids)))
        {
            var oldStart = Offset(oldLines, a);
            var newStart = Offset(newLines, b);
            var oldBlock = string.Concat(oldLines.Skip(a).Take(aLength));
            var newBlock = string.Concat(newLines.Skip(b).Take(bLength));
            changes.AddRange(Words(oldBlock, newBlock).Select(change =>
                change with { OldStart = oldStart + change.OldStart, NewStart = newStart + change.NewStart }));
        }
        return changes;
    }

    /// <summary>
    /// Where a position in the old text is in the new one. A position inside a change lands at the change's start, or
    /// with atEnd, its end.
    /// </summary>
    public static int Map(IReadOnlyList<TextChange> changes, int oldPosition, bool atEnd = false)
    {
        var shift = 0;
        foreach (var change in changes)
        {
            // Text added right at the position goes after it when mapping an end.
            if (oldPosition < change.OldStart || oldPosition == change.OldStart && !(atEnd && change.OldLength == 0))
                break;
            if (oldPosition < change.OldEnd)
                return atEnd ? change.NewEnd : change.NewStart;
            shift = change.NewEnd - change.OldEnd;
        }
        return oldPosition + shift;
    }

    // The changes inside a block of lines that differ, word by word.
    private static IEnumerable<TextChange> Words(string oldBlock, string newBlock)
    {
        if (oldBlock.Length == 0 || newBlock.Length == 0)
            return [new TextChange(0, oldBlock.Length, 0, newBlock.Length)];

        var oldTokens = Tokens(oldBlock);
        var newTokens = Tokens(newBlock);
        var ids = new Dictionary<string, int>();
        var edits = Diff(Ids(oldTokens, ids), Ids(newTokens, ids));
        if (edits.Count == 0)
            return [];

        // Text only added or only taken out that holds a line break moves, where it can, to start a line, so a line
        // typed in reads as a whole new line rather than the end of one line and the start of the next.
        for (var i = 0; i < edits.Count; i++)
        {
            var (a, aLength, b, bLength) = edits[i];
            var (tokens, start, length) = aLength == 0 ? (newTokens, b, bLength) : bLength == 0 ? (oldTokens, a, aLength) : (null, 0, 0);
            if (tokens is null || !tokens[start..(start + length)].Any(IsBreak))
                continue;
            var (prevA, prevB) = i > 0 ? (edits[i - 1].A + edits[i - 1].ALength, edits[i - 1].B + edits[i - 1].BLength) : (0, 0);
            var (nextA, nextB) = i + 1 < edits.Count ? (edits[i + 1].A, edits[i + 1].B) : (oldTokens.Length, newTokens.Length);
            int back = 0, forward = 0;
            while (back < Math.Min(a - prevA, b - prevB) && tokens[start - back - 1] == tokens[start + length - back - 1])
                back++;
            while (forward < Math.Min(nextA - a - aLength, nextB - b - bLength) && tokens[start + forward] == tokens[start + length + forward])
                forward++;
            var lineStarts = Enumerable.Range(-back, back + forward + 1)
                .Where(shift => start + shift == 0 || IsBreak(tokens[start + shift - 1]))
                .OrderBy(Math.Abs)
                .ToList();
            if (lineStarts.Count > 0)
                edits[i] = (a + lineStarts[0], aLength, b + lineStarts[0], bLength);
        }

        // Text only added or only taken out slides back while it ends the way the text before it does, so it can
        // join the change before it: a comma added at the end of a line goes with the line added after it.
        for (var i = 1; i < edits.Count; i++)
        {
            var (a, aLength, b, bLength) = edits[i];
            var (prevA, prevALength, prevB, prevBLength) = edits[i - 1];
            var (tokens, start, length) = aLength == 0 ? (newTokens, b, bLength) : bLength == 0 ? (oldTokens, a, aLength) : (null, 0, 0);
            if (tokens is null)
                continue;
            var slide = 0;
            while (a - slide > prevA + prevALength && b - slide > prevB + prevBLength && tokens[start - slide - 1] == tokens[start + length - slide - 1])
                slide++;
            if (a - slide == prevA + prevALength && b - slide == prevB + prevBLength)
                edits[i] = (a - slide, aLength, b - slide, bLength);
        }

        // Changes split only by spaces read as one change, and so do replacements split only by punctuation, so
        // rewritten text doesn't break up around each comma. Both stay on one line.
        var joined = new List<(int A, int AEnd, int B, int BEnd)>();
        foreach (var (a, aLength, b, bLength) in edits)
        {
            var gap = joined.Count > 0 ? oldTokens[joined[^1].AEnd..a] : [];
            var replacements = joined.Count > 0 && aLength > 0 && bLength > 0 && joined[^1].AEnd > joined[^1].A && joined[^1].BEnd > joined[^1].B;
            if (joined.Count > 0 && (gap.All(IsSpace) || replacements && gap.All(IsFiller)))
                joined[^1] = (joined[^1].A, a + aLength, joined[^1].B, b + bLength);
            else
                joined.Add((a, a + aLength, b, b + bLength));
        }

        return joined.Select(edit => new TextChange(
            Offset(oldTokens, edit.A), Offset(oldTokens, edit.AEnd) - Offset(oldTokens, edit.A),
            Offset(newTokens, edit.B), Offset(newTokens, edit.BEnd) - Offset(newTokens, edit.B)));
    }

    private static bool IsSpace(string token) => token[0] is ' ' or '\t';

    private static bool IsBreak(string token) => token[0] is '\r' or '\n';

    // Spaces or punctuation, but not a line break or a word.
    private static bool IsFiller(string token) => !IsBreak(token) && !char.IsLetterOrDigit(token[0]) && token[0] != '_';

    private static string[] Lines(string text) => LinePattern().Matches(text).Select(match => match.Value).ToArray();

    private static string[] Tokens(string text) => TokenPattern().Matches(text).Select(match => match.Value).ToArray();

    private static int Offset(string[] pieces, int count)
    {
        var offset = 0;
        for (var i = 0; i < count; i++)
            offset += pieces[i].Length;
        return offset;
    }

    private static int[] Ids(string[] pieces, Dictionary<string, int> ids)
    {
        var result = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (!ids.TryGetValue(pieces[i], out var id))
                ids[pieces[i]] = id = ids.Count;
            result[i] = id;
        }
        return result;
    }

    /// <summary>
    /// The runs of a and b that differ, in order, by Myers' algorithm, as (start in a, length in a, start in b,
    /// length in b). The ends both sides share are taken off first.
    /// </summary>
    private static List<(int A, int ALength, int B, int BLength)> Diff(int[] a, int[] b)
    {
        int prefix = 0, suffix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
            prefix++;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)])
            suffix++;
        int n = a.Length - prefix - suffix, m = b.Length - prefix - suffix;
        if (n == 0 && m == 0)
            return [];
        if (n == 0 || m == 0)
            return [(prefix, n, prefix, m)];

        // Each step's furthest reach on every diagonal, kept so the path can be walked back.
        var max = Math.Min(n + m, MaxEdits);
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var found = false;
        for (var d = 0; d <= max && !found; d++)
        {
            trace.Add(v[(max - d)..(max + d + 3)]);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || k != d && v[max + k] < v[max + k + 2] ? v[max + k + 2] : v[max + k] + 1;
                var y = x - k;
                while (x < n && y < m && a[prefix + x] == b[prefix + y])
                    (x, y) = (x + 1, y + 1);
                v[max + k + 1] = x;
                if (x >= n && y >= m)
                {
                    found = true;
                    break;
                }
            }
        }
        if (!found)
            return [(prefix, n, prefix, m)];

        // Walk back from the end, noting each step off the diagonal.
        var steps = new List<(int X, int Y, bool Delete)>();
        int px = n, py = m;
        for (var d = trace.Count - 1; d > 0; d--)
        {
            var snapshot = trace[d]; // v before step d, indexed from diagonal -d - 1
            var k = px - py;
            var down = k == -d || k != d && snapshot[k + d] < snapshot[k + d + 2];
            var prevK = down ? k + 1 : k - 1;
            var prevX = snapshot[prevK + d + 1];
            var prevY = prevX - prevK;
            steps.Add(down ? (prevX, prevY, false) : (prevX, prevY, true));
            (px, py) = (prevX, prevY);
        }
        steps.Reverse();

        var runs = new List<(int A, int ALength, int B, int BLength)>();
        foreach (var (x, y, delete) in steps)
        {
            var (aLength, bLength) = delete ? (1, 0) : (0, 1);
            if (runs.Count > 0 && runs[^1].A + runs[^1].ALength == prefix + x && runs[^1].B + runs[^1].BLength == prefix + y)
                runs[^1] = (runs[^1].A, runs[^1].ALength + aLength, runs[^1].B, runs[^1].BLength + bLength);
            else
                runs.Add((prefix + x, aLength, prefix + y, bLength));
        }
        return runs;
    }

    [GeneratedRegex(@"[^\r\n]*(\r\n|\n|\r)|[^\r\n]+$")]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"\r\n|\n|\r|[ \t]+|[\p{L}\p{N}_]+|.")]
    private static partial Regex TokenPattern();
}
