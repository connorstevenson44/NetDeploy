using System.Xml;
using System.Xml.Linq;

namespace NetDeploy.Core.Logs;

/// <summary>
/// A file target in an nlog.config: where it writes, and where it moves old logs to, as full paths in which * stands
/// for the parts NLog fills in as it runs, such as the date or the logger's name.
/// </summary>
public sealed record NLogFileTarget(string Name, string FilePattern, IReadOnlyList<string> ArchivePatterns);

/// <summary>
/// Reads the file targets of an app's nlog.config, to find its logs. The layouts in the paths are worked out as far as
/// they can be from outside the app: ${basedir} is the config's folder, variables and special folders are filled in,
/// and anything that changes as the app runs becomes *.
/// </summary>
public static class NLogConfig
{
    public const string FileName = "nlog.config";

    // Includes can include each other; this stops a loop.
    private const int MaxIncludeDepth = 5;

    private static readonly HashSet<string> TargetElements = new(StringComparer.OrdinalIgnoreCase) { "target", "wrapper-target", "compound-target" };
    private static readonly HashSet<string> FileTypes = new(StringComparer.OrdinalIgnoreCase) { "File", "AtomicFile" };

    // Any of these turns archiving on, which moves old logs out of the way.
    private static readonly string[] ArchiveSettings = ["archiveAboveSize", "archiveEvery", "maxArchiveFiles", "maxArchiveDays", "archiveOldFileOnStartup"];

    /// <summary>The nlog.config in a folder, whatever its case, or null.</summary>
    public static string? Find(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, FileName, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The file targets in an nlog.config and the files it includes, in the order they are written.</summary>
    /// <exception cref="InvalidDataException">The file isn't XML NLog can read.</exception>
    public static IReadOnlyList<NLogFileTarget> Read(string path)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var reader = new Reader(baseDir);
        reader.ReadFile(path, depth: 0);
        return reader.Targets;
    }

    private sealed class Reader(string baseDir)
    {
        private readonly NLogLayout _layout = new(baseDir);
        private readonly Dictionary<string, XElement> _defaults = new(StringComparer.OrdinalIgnoreCase);

        public List<NLogFileTarget> Targets { get; } = [];

        public void ReadFile(string path, int depth)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} isn't valid XML: {ex.Message}", ex);
            }
            // nlog.config holds <nlog>, or <configuration> with <nlog> in it, as an app.config does.
            var nlog = document.Root is { Name.LocalName: "nlog" } root ? root
                : document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "nlog");
            if (nlog is null)
                throw new InvalidDataException($"{Path.GetFileName(path)} has no <nlog> element.");
            Read(nlog, depth);
        }

        private void Read(XElement nlog, int depth)
        {
            foreach (var element in nlog.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "variable" when Attribute(element, "name") is { } name:
                        _layout.Variables[name] = Attribute(element, "value") ?? "";
                        break;
                    case "include" when depth < MaxIncludeDepth && Attribute(element, "file") is { } file:
                        foreach (var included in AppLogs.Expand(_layout.ToPattern(file)))
                            ReadInclude(included, depth + 1, IsTrue(Attribute(element, "ignoreErrors")));
                        break;
                    case "targets":
                        foreach (var defaults in element.Elements().Where(child => child.Name.LocalName == "default-target-parameters"))
                            if (TypeOf(defaults) is { } type)
                                _defaults[type] = defaults;
                        foreach (var target in element.Descendants().Where(child => TargetElements.Contains(child.Name.LocalName)))
                            if (TypeOf(target) is { } type && FileTypes.Contains(type) && FileTarget(target, type) is { } found)
                                Targets.Add(found);
                        break;
                }
            }
        }

        private void ReadInclude(string path, int depth, bool ignoreErrors)
        {
            try
            {
                ReadFile(path, depth);
            }
            catch (Exception ex) when (ignoreErrors && ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // NLog skips it too.
            }
        }

        private NLogFileTarget? FileTarget(XElement target, string type)
        {
            var defaults = _defaults.GetValueOrDefault(type);
            string? Setting(string name) => Attribute(target, name) ?? (defaults is null ? null : Attribute(defaults, name));

            if (Setting("fileName") is not { Length: > 0 } fileName)
                return null;
            var file = _layout.ToPattern(fileName);
            var archives = new List<string>();
            if (Setting("archiveFileName") is { Length: > 0 } archiveName)
                archives.Add(_layout.ToPattern(archiveName));
            else if (ArchiveSettings.Any(name => Setting(name) is not null))
            {
                // Without an archive name, NLog numbers old logs beside the log itself: app.log becomes app.1.log in
                // NLog 5 and app_01.log in NLog 6.
                var folder = Path.GetDirectoryName(file)!;
                archives.Add(Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + "*" + Path.GetExtension(file)));
            }
            // Compressed archives are zips, whatever the archive name says.
            if (IsTrue(Setting("enableArchiveFileCompression")))
                archives.AddRange(archives.Where(archive => !archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(archive => Path.ChangeExtension(archive, ".zip")).ToList());
            return new NLogFileTarget(Attribute(target, "name") ?? Path.GetFileName(file), file, archives.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        // xsi:type="File" as written by most configs, or type="File", which NLog reads too.
        private static string? TypeOf(XElement element) =>
            element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "type")?.Value.Trim();

        private static bool IsTrue(string? value) => bool.TryParse(value, out var flag) && flag;

        // NLog reads attribute names in any case.
        private static string? Attribute(XElement element, string name) =>
            element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
