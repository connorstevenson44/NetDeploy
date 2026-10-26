namespace NetDeploy.Core.Logs;

/// <summary>
/// A file target in an nlog.config: where it writes, and where it moves old logs to, as full paths in which * stands
/// for the parts NLog fills in as it runs, such as the date or the logger's name.
/// </summary>
public sealed record NLogFileTarget(string Name, string FilePattern, IReadOnlyList<string> ArchivePatterns);
