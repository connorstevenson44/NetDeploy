using Microsoft.Extensions.FileSystemGlobbing;

namespace NetDeploy.Core.Steps;

public sealed class CopyFilesStep : DeployStep
{
    /// <summary>A folder inside the package.</summary>
    public required string From { get; init; }

    public required string To { get; init; }

    /// <summary>Globs for files that are kept when they already exist, such as config files merged later.</summary>
    public List<string> Preserve { get; init; } = [];

    /// <summary>Deletes target files that are not in the package (preserved files and .deploy are kept).</summary>
    public bool DeleteExtra { get; init; }

    public override string Describe() => $"Copy {From} to {To}";

    public override Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var source = context.PackagePath(From);
        var target = Path.GetFullPath(context.Resolve(To));
        var preserve = new Matcher(StringComparison.OrdinalIgnoreCase);
        preserve.AddIncludePatterns(Preserve);

        int copied = 0, kept = 0, deleted = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = FileSystem.RelativeUnixPath(source, file);
            var destination = Path.Combine(target, relative);
            if (File.Exists(destination) && preserve.Match(relative).HasMatches)
            {
                kept++;
                continue;
            }

            if (!context.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }
            copied++;
        }

        if (DeleteExtra && Directory.Exists(target))
        {
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                var relative = FileSystem.RelativeUnixPath(target, file);
                if (relative.StartsWith(MergeConfigStep.StateFolder + "/", StringComparison.OrdinalIgnoreCase)
                    || preserve.Match(relative).HasMatches
                    || File.Exists(Path.Combine(source, relative)))
                    continue;

                if (!context.DryRun)
                    File.Delete(file);
                deleted++;
            }
        }

        var verb = context.DryRun ? "Would copy" : "Copied";
        context.Log.Info($"{verb} {copied} files, kept {kept} existing, {(context.DryRun ? "would delete" : "deleted")} {deleted}.");
        return Task.CompletedTask;
    }
}
