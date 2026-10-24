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
        var source = context.PackagePath(From);
        var target = Path.GetFullPath(context.Resolve(To));
        var preserve = Matcher(Preserve);
        var configs = Matcher(Configs);

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(file => FileSystem.RelativeUnixPath(source, file))
            .ToList();
        var configFiles = files.Where(file => configs.Match(file).HasMatches).ToList();

        int copied = 0, kept = 0, deleted = 0;
        foreach (var relative in files.Except(configFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, relative);
            if (File.Exists(destination) && preserve.Match(relative).HasMatches)
            {
                kept++;
                continue;
            }

            if (!context.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(source, relative), destination, overwrite: true);
            }
            copied++;
        }

        if (DeleteExtra && Directory.Exists(target))
        {
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                var relative = FileSystem.RelativeUnixPath(target, file);
                if (relative.StartsWith(ConfigUpdates.StateFolder + "/", StringComparison.OrdinalIgnoreCase)
                    || preserve.Match(relative).HasMatches
                    || configs.Match(relative).HasMatches
                    || File.Exists(Path.Combine(source, relative)))
                    continue;

                if (!context.DryRun)
                    File.Delete(file);
                deleted++;
            }
        }

        var verb = context.DryRun ? "Would copy" : "Copied";
        context.Log.Info($"{verb} {copied} files, kept {kept} existing, {(context.DryRun ? "would delete" : "deleted")} {deleted}.");

        if (configFiles.Count > 0)
            await MergeConfigsAsync(context, source, target, configFiles, cancellationToken);
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
