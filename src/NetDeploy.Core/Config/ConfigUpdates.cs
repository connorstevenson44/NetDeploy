using System.Text.RegularExpressions;

namespace NetDeploy.Core.Config;

public enum ConfigFileKind
{
    /// <summary>The package's file merged with the server's copy.</summary>
    Update,

    /// <summary>The server has no copy yet, so the package's file is used as is.</summary>
    New,

    /// <summary>A server-only override, such as appsettings.Production.json, checked against the new appsettings.json.</summary>
    Override,

    /// <summary>A file on the server opened for editing, outside any deploy.</summary>
    Edit,
}

/// <summary>A config file the deploy will write, with the merge to review.</summary>
public sealed record ConfigFileUpdate(string TargetPath, ConfigFileKind Kind, MergePlan Plan, string? PackageText)
{
    public string FileName => Path.GetFileName(TargetPath);

    /// <summary>True when the user should look at this file before it is written.</summary>
    public bool NeedsReview => Kind == ConfigFileKind.New || Plan.Rows.Any(row => row.IsChanged || row.Choice is null);
}

/// <summary>
/// Works out how each config file in a package should land on the server, following how .NET layers
/// configuration: appsettings.json ships with the app and decides which settings exist, and
/// appsettings.{Environment}.json on the server holds that server's values on top of it.
/// </summary>
public static partial class ConfigUpdates
{
    /// <summary>Folder beside the config files where the tool keeps the last deployed defaults.</summary>
    public const string StateFolder = ".deploy";

    private const string BaseSettings = "appsettings.json";

    /// <param name="source">The package folder being copied.</param>
    /// <param name="target">The folder it is copied to.</param>
    /// <param name="configFiles">Config files in the package, relative to source.</param>
    /// <param name="variables">The deploy's settings, which fill {{Name}} and {{Name|Default}} values in the package's files.</param>
    public static IReadOnlyList<ConfigFileUpdate> Plan(string source, string target, IEnumerable<string> configFiles, MergeOptions options,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        var updates = new List<ConfigFileUpdate>();
        foreach (var relative in configFiles)
        {
            var targetPath = Path.Combine(target, relative);
            var incoming = File.ReadAllText(Path.Combine(source, relative));
            var format = ConfigFormats.For(targetPath);
            var update = File.Exists(targetPath)
                ? new ConfigFileUpdate(targetPath, ConfigFileKind.Update,
                    ConfigMerger.Plan(ReadBase(targetPath), File.ReadAllText(targetPath), incoming, format, options), incoming)
                : new ConfigFileUpdate(targetPath, ConfigFileKind.New, ConfigMerger.PlanNew(incoming, format), incoming);
            updates.Add(update);

            if (Path.GetFileName(relative).Equals(BaseSettings, StringComparison.OrdinalIgnoreCase))
                updates.AddRange(PlanOverrides(source, targetPath, relative, incoming));
        }

        FillVariables(updates, variables ?? new Dictionary<string, string>());
        RequirePlaceholderValues(updates);
        return updates;
    }

    /// <summary>
    /// Fills each setting the package brings as {{Name}} or {{Name|Default}} with the deploy setting called Name, or
    /// else its default. In a new file the default shows as the value before, so the review shows what the setting
    /// changed to. A token with neither is left for RequirePlaceholderValues to ask about.
    /// </summary>
    private static void FillVariables(List<ConfigFileUpdate> updates, IReadOnlyDictionary<string, string> variables)
    {
        foreach (var update in updates.Where(u => u.Kind is ConfigFileKind.Update or ConfigFileKind.New))
        {
            var format = update.Plan.Format;
            foreach (var row in update.Plan.Rows)
            {
                if (row.Choice != MergeChoice.TakeIncoming || row.Incoming is not { } incoming
                    || VariablePattern().Match(format.Display(incoming)) is not { Success: true } token)
                    continue;

                var name = token.Groups["name"].Value;
                var fallback = token.Groups["default"] is { Success: true } group ? group.Value : null;
                var value = variables.GetValueOrDefault(name) ?? fallback;
                if (value is null)
                    continue;

                var before = update.Kind == ConfigFileKind.New && fallback is not null ? format.ParseEdit(incoming, fallback) : null;
                row.Fill(format.ParseEdit(incoming, value), before,
                    variables.ContainsKey(name) ? $"Filled from the deploy setting {name}" : $"The default for {name}; the deploy has no setting called that");
            }
        }
    }

    /// <summary>Opens a config file on the server for editing. Every setting starts as it is.</summary>
    public static ConfigFileUpdate Open(string path)
    {
        var text = File.ReadAllText(path);
        var plan = ConfigMerger.Plan(null, text, text, ConfigFormats.For(path), new MergeOptions([]));
        return new ConfigFileUpdate(path, ConfigFileKind.Edit, plan, null);
    }

    /// <summary>Writes each file and remembers the package's copy as the base for the next merge.</summary>
    public static void Write(IEnumerable<ConfigFileUpdate> updates)
    {
        foreach (var update in updates)
        {
            // Files already on the server are only rewritten when something in them changes.
            if (update.Kind is ConfigFileKind.Override or ConfigFileKind.Edit)
            {
                if (update.Plan.ChangesStartFile)
                    File.WriteAllText(update.TargetPath, update.Plan.BuildResult());
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(update.TargetPath)!);
            // Untouched package files keep their own formatting.
            File.WriteAllText(update.TargetPath, update.Plan.ChangesStartFile ? update.Plan.BuildResult() : update.PackageText);
            SaveBase(update.TargetPath, update.PackageText!);
        }
    }

    /// <summary>Remembers the package's copy of a config file as the base for the next three-way merge.</summary>
    internal static void SaveBase(string target, string packageText)
    {
        var basePath = BaseCopyPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        File.WriteAllText(basePath, packageText);
    }

    internal static string BaseCopyPath(string target) =>
        Path.Combine(Path.GetDirectoryName(target)!, StateFolder, "base", Path.GetFileName(target));

    private static string? ReadBase(string target) =>
        File.Exists(BaseCopyPath(target)) ? File.ReadAllText(BaseCopyPath(target)) : null;

    /// <summary>Checks the server's appsettings.*.json files that the package doesn't ship.</summary>
    private static IEnumerable<ConfigFileUpdate> PlanOverrides(string source, string targetSettings, string relativeSettings, string newBase)
    {
        var folder = Path.GetDirectoryName(targetSettings)!;
        if (!Directory.Exists(folder))
            yield break;

        // The settings the overrides were written against: the last deployed defaults, or the server's file.
        var oldBase = ReadBase(targetSettings) ?? (File.Exists(targetSettings) ? File.ReadAllText(targetSettings) : null);
        var packageFolder = Path.GetDirectoryName(Path.Combine(source, relativeSettings))!;
        foreach (var path in Directory.EnumerateFiles(folder, "appsettings.*.json"))
        {
            if (File.Exists(Path.Combine(packageFolder, Path.GetFileName(path))))
                continue; // Shipped by the package, so merged like any other file.
            var plan = PlanOverride(File.ReadAllText(path), oldBase, newBase);
            yield return new ConfigFileUpdate(path, ConfigFileKind.Override, plan, null);
        }
    }

    /// <summary>
    /// Finds settings in an override that the new appsettings.json no longer has. .NET silently ignores them,
    /// so a renamed setting would quietly lose its server value.
    /// </summary>
    internal static MergePlan PlanOverride(string overrideText, string? oldBaseText, string newBaseText)
    {
        var format = ConfigFormats.For(BaseSettings);
        var values = format.Read(overrideText);
        var newBase = format.Read(newBaseText);
        var oldBase = oldBaseText is null ? null : format.Read(oldBaseText);
        var added = oldBase is null ? [] : newBase.Keys.Where(key => !oldBase.ContainsKey(key)).ToList();

        var rows = new List<MergeRow>();
        foreach (var (path, value) in values)
        {
            if (newBase.ContainsKey(path))
            {
                rows.Add(new MergeRow(path, null, value, value, MergeChoice.KeepCurrent, "Overrides appsettings.json"));
                continue;
            }

            if (newBase.Keys.Any(key => key.StartsWith(path + ":", StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new MergeRow(path, null, value, null, null, "appsettings.json now has a section here, so the app likely no longer reads this value"));
                continue;
            }

            if (oldBase?.ContainsKey(path) != true)
            {
                rows.Add(new MergeRow(path, null, value, null, MergeChoice.KeepCurrent,
                    "Not in appsettings.json; keep it only if the app reads it"));
                continue;
            }

            // Removed from appsettings.json in this version. One new setting in the same section is likely its new name.
            var section = Section(path);
            var renamedTo = added.Where(key => Section(key) == section && !values.ContainsKey(key)).ToList();
            if (renamedTo is [var newPath])
            {
                rows.Add(new MergeRow(path, null, value, null, MergeChoice.Remove, $"Looks renamed to {newPath} in this version"));
                rows.Add(new MergeRow(newPath, null, null, value, MergeChoice.TakeIncoming, $"Keeps your value from {path}"));
            }
            else
            {
                rows.Add(new MergeRow(path, null, value, null, MergeChoice.Remove, "Removed from appsettings.json in this version"));
            }
        }
        return new MergePlan(format, overrideText, rows, startIsCurrent: true);
    }

    /// <summary>
    /// A setting that arrives with a placeholder, such as "" or "CHANGE_ME", needs a real value for this server,
    /// unless an override already sets it.
    /// </summary>
    private static void RequirePlaceholderValues(List<ConfigFileUpdate> updates)
    {
        foreach (var update in updates.Where(u => u.Kind != ConfigFileKind.Override))
        {
            var folder = Path.GetDirectoryName(update.TargetPath);
            var overrides = updates
                .Where(u => u.Kind == ConfigFileKind.Override && Path.GetDirectoryName(u.TargetPath) == folder)
                .ToList();

            foreach (var row in update.Plan.Rows.Where(row => row.Choice is not null))
            {
                var value = row.Result;
                if (value is null || !IsPlaceholder(update.Plan.Format.Display(value), isNew: row.Current is null || update.Kind == ConfigFileKind.New))
                    continue;
                var setBy = overrides.FirstOrDefault(o => o.Plan.Rows.Any(r =>
                    r.Path.Equals(row.Path, StringComparison.OrdinalIgnoreCase) && r.Result is { } v && !IsPlaceholder(o.Plan.Format.Display(v), isNew: true)));
                if (setBy is null)
                    row.RequireChoice("Needs a value for this server");
            }
        }
    }

    /// <summary>An empty value only counts when the setting is new; a marker like CHANGE_ME always does.</summary>
    internal static bool IsPlaceholder(string value, bool isNew) =>
        value.Length == 0 ? isNew : PlaceholderPattern().IsMatch(value);

    private static string Section(string path) => path.Contains(':') ? path[..path.LastIndexOf(':')] : "";

    [GeneratedRegex(@"^\{\{\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(\|(?<default>[^}]*))?\}\}$")]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"^(change[ _-]?me|replace[ _-]?me|todo|tbd|fixme|<[^<>]+>|__[A-Za-z0-9_]+__|\$\{[^}]+\}|\{\{[^}]+\}\})$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderPattern();
}
