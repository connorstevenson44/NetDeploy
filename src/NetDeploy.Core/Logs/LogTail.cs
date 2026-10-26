using System.Text;

namespace NetDeploy.Core.Logs;

/// <summary>Follows a live log as the app adds to it.</summary>
public sealed class LogTail
{
    private readonly string _path;
    private readonly Decoder _decoder;

    internal LogTail(string path, long offset, Decoder decoder)
    {
        _path = path;
        Offset = offset;
        _decoder = decoder;
    }

    /// <summary>How far into the file has been read, in bytes.</summary>
    public long Offset { get; private set; }

    /// <summary>
    /// The text the app has added since the last read, or "" when it hasn't added any. Null when the log has to be read
    /// again: it has gone or got shorter, as when NLog moves it to an archive and starts a new one, or more was added
    /// than is shown at once.
    /// </summary>
    public string? ReadNew(long maxBytes = LogReader.MaxBytes)
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length < Offset || length - Offset > maxBytes)
                return null;
            if (length == Offset)
                return "";
            stream.Position = Offset;
            var bytes = new byte[length - Offset];
            var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            Offset += read;
            var chars = new char[_decoder.GetCharCount(bytes, 0, read, flush: false)];
            return new string(chars, 0, _decoder.GetChars(bytes, 0, read, chars, 0, flush: false));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
