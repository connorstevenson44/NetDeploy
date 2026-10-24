using System.IO.Compression;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

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

/// <summary>
/// What is in each zip of logs, kept so the logs can be listed again every few seconds without opening every zip
/// again. A zip that has changed since is read again.
/// </summary>
public sealed class ZipContents
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Written, long Length, IReadOnlyList<AppLog> Logs)> _zips =
        new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<AppLog> Get(FileInfo zip, Func<IReadOnlyList<AppLog>> read)
    {
        if (_zips.TryGetValue(zip.FullName, out var known) && known.Written == zip.LastWriteTimeUtc && known.Length == zip.Length)
            return known.Logs;
        var logs = read();
        _zips[zip.FullName] = (zip.LastWriteTimeUtc, zip.Length, logs);
        return logs;
    }
}

/// <summary>Finds the logs an app's NLog file targets wrote, from the paths in its nlog.config.</summary>
public static class AppLogs
{
    /// <summary>
    /// Each target's logs: the newest file its file name matches is the live log, and any others, such as yesterday's
    /// for a name with the date in it, are older logs, as are the files its archive names match. Zips are listed by
    /// the files inside them. Live logs come first, then the rest newest first. Files that can't be read are left out.
    /// </summary>
    public static IReadOnlyList<AppLog> Find(IEnumerable<NLogFileTarget> targets, ZipContents? zips = null, CancellationToken cancellation = default)
    {
        var live = new List<AppLog>();
        var older = new List<AppLog>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            cancellation.ThrowIfCancellationRequested();
            var files = Expand(target.FilePattern).Where(seen.Add).Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            for (var i = 0; i < files.Count; i++)
                (i == 0 ? live : older).AddRange(Logs(files[i], target.Name, isLive: i == 0, zips));
            foreach (var path in target.ArchivePatterns.SelectMany(Expand).Where(seen.Add))
            {
                cancellation.ThrowIfCancellationRequested();
                older.AddRange(Logs(new FileInfo(path), target.Name, isLive: false, zips));
            }
        }
        return [.. live, .. older.OrderByDescending(log => log.Modified)];
    }

    private static IEnumerable<AppLog> Logs(FileInfo file, string target, bool isLive, ZipContents? zips)
    {
        try
        {
            if (!file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return [new AppLog(file.FullName, null, target, isLive, file.LastWriteTime, file.Length)];
            return zips is null ? Unzip(file, target) : zips.Get(file, () => Unzip(file, target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return [];
        }
    }

    private static List<AppLog> Unzip(FileInfo file, string target)
    {
        using var zip = ZipFile.OpenRead(file.FullName);
        return zip.Entries.Where(entry => entry.Name.Length > 0)
            .Select(entry => new AppLog(file.FullName, entry.FullName, target, false, entry.LastWriteTime.LocalDateTime, entry.Length))
            .ToList();
    }

    /// <summary>The files a path pattern from NLogConfig matches, where * stands for any part of a name.</summary>
    public static IReadOnlyList<string> Expand(string pattern)
    {
        var parts = pattern.Split(['/', '\\']);
        var wild = Array.FindIndex(parts, part => part.Contains('*'));
        try
        {
            if (wild < 0)
                return File.Exists(pattern) ? [pattern] : [];
            // Everything before the first * is a folder to look in; the rest is matched below it.
            var root = string.Join(Path.DirectorySeparatorChar, parts[..wild]);
            // C: alone is the current folder on drive C, not its top.
            if (root.EndsWith(':'))
                root += Path.DirectorySeparatorChar;
            if (root.Length == 0 || !Directory.Exists(root))
                return [];
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(string.Join('/', parts[wild..]));
            return matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(root))).Files
                .Select(match => Path.GetFullPath(Path.Combine(root, match.Path)))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
