using System.IO.Compression;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace NetDeploy.Core.Logs;

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
                return File.Exists(pattern) ? [Path.GetFullPath(pattern)] : [];
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
