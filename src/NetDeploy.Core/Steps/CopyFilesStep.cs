using Microsoft.Extensions.FileSystemGlobbing;
using NetDeploy.Core.Config;

namespace NetDeploy.Core.Steps;

/// <summary>
/// Copies the app. Config files are never copied over the server's: each one is merged with the server's copy,
/// and the user reviews the merge before anything is written.
/// </summary>
public sealed class CopyFilesStep : DeployStep
{
    /// <summary>A folder inside the package.</summary>
    public required string From { get; init; }

    public required string To { get; init; }

    /// <summary>Globs for files that are kept when they already exist, such as logs or data.</summary>
    public List<string> Preserve { get; init; } = [];

    /// <summary>Deletes target files that are not in the package (preserved files, config files and .deploy are kept).</summary>
    public bool DeleteExtra { get; init; }

    /// <summary>Globs for the config files that are merged instead of copied.</summary>
    public List<string> Configs { get; init; } = ["**/appsettings*.json", "**/*.config"];

    /// <summary>Setting paths (and everything under them) where the package's value always wins.</summary>
    public List<string> TakeNew { get; init; } = [];

    public override string Describe() => $"Copy {From} to {To}";

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var plan = PlanFiles(context.PackageDirectory, context.Variables);
        if (context.Options.FileActions.TryGetValue(this, out var chosen))
            foreach (var entry in plan.Entries)
                if (chosen.TryGetValue(entry.Path, out var action) && entry.Choices.Contains(action))
                    entry.Action = action;

        int copied = 0, kept = 0, deleted = 0;
        var merges = new List<string>();
        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(plan.Target, entry.Path);
            switch (entry.Action)
            {
                case FileAction.Merge:
                    merges.Add(entry.Path);
                    break;
                case FileAction.Keep:
                    kept++;
                    break;
                case FileAction.Delete:
                    if (!context.DryRun)
                        File.Delete(destination);
                    deleted++;
                    break;
                case FileAction.Replace:
                    if (!context.DryRun)
                        Replace(Path.Combine(plan.Source, entry.Path), destination, entry.IsConfig);
                    copied++;
                    break;
            }
        }

        var verb = context.DryRun ? "Would copy" : "Copied";
        context.Log.Info($"{verb} {copied} files, kept {kept} as they are, {(context.DryRun ? "would delete" : "deleted")} {deleted}.");

        if (merges.Count > 0)
            await MergeConfigsAsync(context, plan.Source, plan.Target, merges, cancellationToken);
    }

    /// <summary>
    /// Compares the package's folder with the server's and picks what to do with each file: copy what is new or
    /// changed, keep preserved and identical files, merge config files, and delete extras when DeleteExtra is set.
    /// </summary>
    public FilePlan PlanFiles(string packageDirectory, IReadOnlyDictionary<string, string> variables)
    {
        var source = StepContext.InsidePackage(packageDirectory, Placeholders.Resolve(From, variables));
        var target = Placeholders.Resolve(To, variables);
        if (Placeholders.Unresolved(target) is [_, ..] missing)
            throw new InvalidOperationException($"The target folder uses {{{string.Join("}, {", missing)}}}, which is asked when the step runs.");
        if (!Path.IsPathFullyQualified(target))
            throw new InvalidOperationException(target.Length == 0 ? "Set the target folder first." : $"The target folder \"{target}\" isn't a full path.");
        target = Path.GetFullPath(target);
        var preserve = Matcher(Preserve);
        var configs = Matcher(Configs);
        var entries = new List<FilePlanEntry>();

        var packageFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(file => FileSystem.RelativeUnixPath(source, file))
            .ToList();
        foreach (var relative in packageFiles)
        {
            var isConfig = configs.Match(relative).HasMatches;
            var serverFile = Path.Combine(target, relative);
            var state = !File.Exists(serverFile) ? FileState.New
                : FileSystem.SameContents(Path.Combine(source, relative), serverFile) ? FileState.Same
                : FileState.Changed;
            var action = isConfig ? FileAction.Merge
                : state == FileState.New ? FileAction.Replace
                : state == FileState.Same || preserve.Match(relative).HasMatches ? FileAction.Keep
                : FileAction.Replace;
            entries.Add(new FilePlanEntry(relative, state, isConfig, action));
        }

        if (Directory.Exists(target))
        {
            var inPackage = packageFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                var relative = FileSystem.RelativeUnixPath(target, file);
                if (inPackage.Contains(relative) || relative.StartsWith(ConfigUpdates.StateFolder + "/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var isConfig = configs.Match(relative).HasMatches;
                var delete = DeleteExtra && !isConfig && !preserve.Match(relative).HasMatches;
                entries.Add(new FilePlanEntry(relative, FileState.OnlyOnServer, isConfig, delete ? FileAction.Delete : FileAction.Keep));
            }
        }

        return new FilePlan(source, target, entries);
    }

    /// <summary>Copies a file over the server's. A config copied whole becomes the base for the next merge.</summary>
    private static void Replace(string source, string destination, bool isConfig)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        if (isConfig)
            ConfigUpdates.SaveBase(destination, File.ReadAllText(source));
    }

    private async Task MergeConfigsAsync(StepContext context, string source, string target, List<string> configFiles, CancellationToken cancellationToken)
    {
        var updates = ConfigUpdates.Plan(source, target, configFiles, new MergeOptions(TakeNew));
        foreach (var update in updates)
            context.Log.Info($"{update.FileName}: {update.Plan.Summary}");

        if (updates.Any(update => update.NeedsReview)
            && !await context.User.ReviewConfigsAsync(updates, context.DryRun, cancellationToken))
            throw new OperationCanceledException("The config review was cancelled.");

        if (context.DryRun)
        {
            context.Log.Info($"Would write {updates.Count} config files.");
            return;
        }

        ConfigUpdates.Write(updates);
        context.Log.Info($"Wrote {updates.Count} config files.");
    }

    private static Matcher Matcher(IEnumerable<string> globs)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(globs);
        return matcher;
    }
}
