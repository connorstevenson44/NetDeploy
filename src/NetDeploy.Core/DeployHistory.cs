using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetDeploy.Core;

/// <summary>One deploy as the history keeps it: what went where, when, by whom, and how it ended.</summary>
/// <param name="Folder">The install's folder, or null for a package without an install block.</param>
/// <param name="RolledBack">True when a failed deploy was rolled back to its backup.</param>
/// <param name="Log">The deploy's log file, when it has one.</param>
public sealed record DeployRecord(
    string App, string Version, string? Install, string? Folder, DateTimeOffset Time, string User, DeployResult Result, bool RolledBack, string? Log)
{
    /// <summary>The record of a deploy that has just ended, made by the person running this process.</summary>
    public static DeployRecord For(DeployPackage package, DeployOptions options, DeployOutcome outcome, bool rolledBack, string? log) =>
        new(package.Manifest.App.Name, package.Manifest.App.Version, options.Instance.Length > 0 ? options.Instance : null, outcome.InstallDirectory,
            DateTimeOffset.Now, $@"{Environment.UserDomainName}\{Environment.UserName}", outcome.Result, rolledBack, log);
}

/// <summary>
/// Every deploy made on this machine, from the window or the command line, so the folder view can say what is on an
/// install and since when. It lives in %ProgramData%\NetDeploy\history, one file per deploy, so everyone who deploys
/// here sees the same history and nothing is added to the install folders, where a restore or rename would lose it.
/// </summary>
public sealed class DeployHistory(string folder)
{
    /// <summary>The most recent files read, so a history that has grown for years still opens quickly.</summary>
    internal const int MaxRead = 500;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>%ProgramData%\NetDeploy\history.</summary>
    public static DeployHistory Machine { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetDeploy", "history"));

    public string Folder { get; } = folder;

    /// <summary>
    /// Saves a deploy. Returns false when it couldn't be written, which only loses the record: the deploy itself has
    /// already happened.
    /// </summary>
    public bool TryAdd(DeployRecord record)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Named by time, so the newest sort last, and unique, so two people deploying at once don't collide.
            var name = $"{record.Time.UtcDateTime:yyyyMMdd-HHmmss-fff}-{Path.GetRandomFileName()[..8]}.json";
            File.WriteAllText(Path.Combine(Folder, name), JsonSerializer.Serialize(record, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The deploys to an install's folder, newest first. Files that can't be read are left out.</summary>
    public IReadOnlyList<DeployRecord> For(string folder)
    {
        List<string> files;
        try
        {
            files = Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*.json").ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        var key = Normalize(folder);
        return files
            .Order(StringComparer.Ordinal)
            .TakeLast(MaxRead)
            .Select(Read)
            .OfType<DeployRecord>()
            .Where(record => record.Folder is { } recorded && string.Equals(Normalize(recorded), key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.Time)
            .ToList();
    }

    /// <summary>
    /// A folder as Windows tells folders apart, so C:/Apps/Orders/main and C:\Apps\Orders\main\ match; null for a
    /// path that can't be one, such as one from a file edited by hand.
    /// </summary>
    private static string? Normalize(string folder)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static DeployRecord? Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<DeployRecord>(File.ReadAllText(file), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
