using NetDeploy.Core.Config;

namespace NetDeploy.Core.Steps;

public sealed class MergeConfigStep : DeployStep
{
    /// <summary>Folder beside the target where the tool keeps its own files, such as the last deployed defaults.</summary>
    internal const string StateFolder = ".deploy";

    /// <summary>The new default config, inside the package.</summary>
    public required string Source { get; init; }

    /// <summary>The config file on the server.</summary>
    public required string Target { get; init; }

    /// <summary>Settings paths (and everything under them) where the package's value always wins.</summary>
    public List<string> TakeNew { get; init; } = [];

    /// <summary>Keeps settings the package no longer has, instead of removing them.</summary>
    public bool KeepRemoved { get; init; }

    /// <summary>Opens the merge table. When false, the proposed merge is used and conflicts fail the step.</summary>
    public bool Interactive { get; init; } = true;

    public override string Describe() => $"Merge settings into {Target}";

    public override async Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var source = context.PackagePath(Source);
        var target = Path.GetFullPath(context.Resolve(Target));
        var incoming = await File.ReadAllTextAsync(source, cancellationToken);

        if (!File.Exists(target))
        {
            context.Log.Info("No existing file, so the package's file is used as is.");
            if (!context.DryRun)
                await WriteAsync(target, incoming, incoming, cancellationToken);
            return;
        }

        var basePath = BaseCopyPath(target);
        var plan = ConfigMerger.Plan(
            File.Exists(basePath) ? await File.ReadAllTextAsync(basePath, cancellationToken) : null,
            await File.ReadAllTextAsync(target, cancellationToken),
            incoming,
            ConfigFormats.For(target),
            new MergeOptions(TakeNew, KeepRemoved));
        context.Log.Info(plan.Summary);

        if (Interactive && plan.Rows.Any(row => row.IsChanged))
        {
            if (!await context.User.ReviewMergeAsync(target, plan, context.DryRun, cancellationToken))
                throw new OperationCanceledException("The merge was cancelled.");
        }
        else if (plan.ConflictCount > 0)
        {
            throw new InvalidOperationException($"{plan.ConflictCount} settings changed on both sides, and interactive is off.");
        }

        if (!context.DryRun)
            await WriteAsync(target, plan.BuildResult(), incoming, cancellationToken);
    }

    /// <summary>Writes the merged file and remembers the package's defaults as the base for the next merge.</summary>
    private static async Task WriteAsync(string target, string merged, string incoming, CancellationToken cancellationToken)
    {
        var basePath = BaseCopyPath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        await File.WriteAllTextAsync(target, merged, cancellationToken);
        await File.WriteAllTextAsync(basePath, incoming, cancellationToken);
    }

    internal static string BaseCopyPath(string target) =>
        Path.Combine(Path.GetDirectoryName(target)!, StateFolder, "base", Path.GetFileName(target));
}
