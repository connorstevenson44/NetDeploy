using System.Text.RegularExpressions;

namespace NetDeploy.Core.Logs;

/// <summary>Where a match is in a log's text.</summary>
public readonly record struct LogMatch(int Start, int Length);

/// <summary>How far a search of every log has got: the log just searched and its matches, if one was.</summary>
public sealed record LogSearchProgress(double Fraction, AppLog? Searched = null, int Matches = 0);

/// <summary>
/// Searches logs a line at a time, for text as typed or a regular expression. A match never spans lines, the same in
/// the log on screen as in every log.
/// </summary>
public static class LogSearch
{
    /// <summary>A pattern that takes longer than this on one line is given up on, rather than hanging the search.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>How many logs are searched at once.</summary>
    private const int Parallelism = 4;

    /// <summary>What to search for: the text as typed, or with isRegex, a regular expression.</summary>
    /// <exception cref="ArgumentException">The regular expression isn't valid.</exception>
    public static Regex Pattern(string text, bool isRegex, bool matchCase) =>
        new(isRegex ? text : Regex.Escape(text), RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), Timeout);

    /// <summary>
    /// The matches in text from the start of the line that from is in, at most max of them. A pattern that can match
    /// nothing, such as a*, only counts where it matches something.
    /// </summary>
    public static List<LogMatch> Matches(string text, Regex pattern, int from = 0, int max = int.MaxValue, CancellationToken cancellation = default)
    {
        var matches = new List<LogMatch>();
        var start = from <= 0 || text.Length == 0 ? 0 : text.LastIndexOf('\n', Math.Min(from, text.Length) - 1) + 1;
        while (start <= text.Length && matches.Count < max)
        {
            cancellation.ThrowIfCancellationRequested();
            var end = text.IndexOf('\n', start);
            var line = Line(text.AsSpan(start, (end < 0 ? text.Length : end) - start));
            foreach (var match in pattern.EnumerateMatches(line))
                if (match.Length > 0 && matches.Count < max)
                    matches.Add(new LogMatch(start + match.Index, match.Length));
            if (end < 0)
                break;
            start = end + 1;
        }
        return matches;
    }

    /// <summary>
    /// Counts the matches in each log, reading each to its end, several at a time. Progress says how much has been read
    /// and each log's count as it finishes. The counts are by Key.
    /// </summary>
    /// <exception cref="RegexMatchTimeoutException">The pattern took too long on a line.</exception>
    public static async Task<IReadOnlyDictionary<string, int>> SearchAsync(
        IReadOnlyList<AppLog> logs, Regex pattern, IProgress<LogSearchProgress>? progress = null, CancellationToken cancellation = default)
    {
        // Compiled is slower to set up but much faster over a lot of text.
        var compiled = new Regex(pattern.ToString(), pattern.Options | RegexOptions.Compiled, pattern.MatchTimeout);
        var total = Math.Max(1, logs.Sum(log => Math.Max(log.Size, 1)));
        long read = 0;
        var counts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        await Parallel.ForEachAsync(logs, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellation }, (log, token) =>
        {
            long reported = 0;
            void Report(long bytes)
            {
                progress?.Report(new LogSearchProgress(Math.Min(1, (double)Interlocked.Add(ref read, bytes - reported) / total)));
                reported = bytes;
            }
            var count = Count(log, compiled, Report, token);
            counts[log.Key] = count;
            Report(Math.Max(log.Size, 1));
            progress?.Report(new LogSearchProgress(Math.Min(1, (double)Interlocked.Read(ref read) / total), log, count));
            return ValueTask.CompletedTask;
        });
        return counts;
    }

    /// <summary>The matches in one log; a log that can't be read has none.</summary>
    private static int Count(AppLog log, Regex pattern, Action<long> report, CancellationToken cancellation)
    {
        try
        {
            using var reader = new StreamReader(log.Open(), detectEncodingFromByteOrderMarks: true);
            var count = 0;
            long bytes = 0;
            long next = 0;
            while (reader.ReadLine() is { } line)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var match in pattern.EnumerateMatches(line))
                    if (match.Length > 0)
                        count++;
                // Near enough for the progress: the logs are mostly one byte to a character.
                bytes += line.Length + 1;
                if (bytes >= next)
                {
                    report(Math.Min(bytes, log.Size));
                    next = bytes + 1024 * 1024;
                }
            }
            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return 0;
        }
    }

    private static ReadOnlySpan<char> Line(ReadOnlySpan<char> line) => line.EndsWith("\r") ? line[..^1] : line;
}
