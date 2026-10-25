using System.Text;

namespace NetDeploy.Core.Logs;

/// <summary>Reads logs to show, without stopping the app writing them.</summary>
public static class LogReader
{
    /// <summary>How much of a log is shown: its last 8 MB. Searching every log reads them whole.</summary>
    public const long MaxBytes = 8 * 1024 * 1024;

    public static LogText Read(AppLog log, long maxBytes = MaxBytes, CancellationToken cancellation = default)
    {
        using var stream = log.Open();
        var size = log.IsZipped ? log.Size : stream.Length;
        var head = new byte[4];
        var headLength = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var (encoding, bom) = Detect(head.AsSpan(0, headLength));
        var unit = encoding is UnicodeEncoding ? 2 : 1;

        // A long log is read from near its end, at the start of a character.
        var skip = Math.Max(bom, size - maxBytes);
        skip += (skip - bom) % unit;
        var isCut = skip > bom;
        if (stream.CanSeek)
            stream.Position = skip;
        else
            Discard(stream, skip - headLength, cancellation);

        var bytes = new MemoryStream();
        if (!stream.CanSeek && skip < headLength)
            bytes.Write(head, (int)skip, headLength - (int)skip);
        stream.CopyTo(bytes);
        cancellation.ThrowIfCancellationRequested();

        // A character the app is half way through writing is finished by the tail's next read.
        var decoder = encoding.GetDecoder();
        var chars = new char[encoding.GetMaxCharCount((int)bytes.Length)];
        var count = decoder.GetChars(bytes.GetBuffer(), 0, (int)bytes.Length, chars, 0, flush: log.IsZipped);
        var text = new string(chars, 0, count);
        // The first line of a cut log is only part of one.
        if (isCut && text.IndexOf('\n') is var newline and >= 0)
            text = text[(newline + 1)..];

        var tail = log.IsZipped ? null : new LogTail(log.Path, skip + bytes.Length, decoder);
        return new LogText(text, isCut, size, tail);
    }

    /// <summary>The encoding a log's first bytes say it is in, and how long that mark is; UTF-8 without one.</summary>
    internal static (Encoding Encoding, int Bom) Detect(ReadOnlySpan<byte> head) => head switch
    {
        [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false), 3),
        [0xFF, 0xFE, ..] => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2),
        [0xFE, 0xFF, ..] => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2),
        _ => (new UTF8Encoding(false), 0),
    };

    private static void Discard(Stream stream, long count, CancellationToken cancellation)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
                return;
            count -= read;
        }
    }
}
