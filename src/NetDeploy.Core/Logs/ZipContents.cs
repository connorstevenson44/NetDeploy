namespace NetDeploy.Core.Logs;

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
