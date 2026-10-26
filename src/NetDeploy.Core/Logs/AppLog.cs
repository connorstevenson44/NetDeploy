using System.IO.Compression;

namespace NetDeploy.Core.Logs;

/// <summary>
/// A log an app wrote: a file, or a file inside a zip that NLog archived it to. Live is the file a target writes to
/// now; the rest are older.
/// </summary>
public sealed record AppLog(string Path, string? Entry, string Target, bool IsLive, DateTime Modified, long Size)
{
    /// <summary>The file's name, or the zipped file's name inside its zip.</summary>
    public string Name => Entry is null ? System.IO.Path.GetFileName(Path) : System.IO.Path.GetFileName(Entry);

    public bool IsZipped => Entry is not null;

    /// <summary>Tells logs apart, as a zip holds several.</summary>
    public string Key => Entry is null ? Path : Path + "|" + Entry;

    /// <summary>
    /// Opens the log to read, leaving the app free to keep writing to it, rename it or delete it. A zipped log is
    /// read straight out of its zip.
    /// </summary>
    public Stream Open()
    {
        var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
        if (Entry is null)
            return file;
        try
        {
            var zip = new ZipArchive(file, ZipArchiveMode.Read);
            var entry = zip.GetEntry(Entry) ?? throw new FileNotFoundException($"{Entry} isn't in {System.IO.Path.GetFileName(Path)} any more.");
            return new EntryStream(zip, entry.Open());
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>A zipped file's text, which closes its zip with it.</summary>
    private sealed class EntryStream(ZipArchive zip, Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                zip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
