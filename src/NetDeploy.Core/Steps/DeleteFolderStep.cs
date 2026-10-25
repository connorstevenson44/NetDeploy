namespace NetDeploy.Core.Steps;

/// <summary>Deletes a folder and everything in it, except the subfolders listed in keep.</summary>
public sealed class DeleteFolderStep : DeployStep
{
    public required string Path { get; init; }

    /// <summary>Subfolders, by name, that are left in place; the folder itself then stays too.</summary>
    public List<string> Keep { get; init; } = [];

    public override string Describe() => $"Delete {Path}";

    public override string Explain()
    {
        var except = Keep.Count == 0 ? "" : $", except {string.Join(", ", Keep)}";
        if (!Directory.Exists(Path))
            return $"Deletes the folder {Path}{except}. It does not exist now, so nothing will happen.";
        var files = FilesToDelete(Path).Count();
        return $"Deletes the folder {Path} and everything in it{except}: {files} files.";
    }

    public override Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var folder = context.Resolve(Path);
        if (!Directory.Exists(folder))
        {
            context.Log.Info($"{folder} does not exist.");
            return Task.CompletedTask;
        }

        var kept = Keep.Select(name => System.IO.Path.Combine(folder, context.Resolve(name))).ToList();
        if (context.DryRun)
        {
            context.Log.Info($"Would delete {FilesToDelete(folder, kept).Count()} files in {folder}");
            return Task.CompletedTask;
        }

        if (kept.Count == 0)
            FileSystem.WhenFree(() => Directory.Delete(folder, recursive: true));
        else
        {
            foreach (var file in Directory.EnumerateFiles(folder))
                FileSystem.WhenFree(() => File.Delete(file));
            foreach (var subfolder in Directory.EnumerateDirectories(folder).Where(sub => !IsKept(sub, kept)))
                FileSystem.WhenFree(() => Directory.Delete(subfolder, recursive: true));
        }
        context.Log.Info($"Deleted {folder}{(kept.Count == 0 ? "" : " except " + string.Join(", ", Keep))}");
        return Task.CompletedTask;
    }

    private IEnumerable<string> FilesToDelete(string folder, List<string>? kept = null)
    {
        kept ??= Keep.Select(name => System.IO.Path.Combine(folder, name)).ToList();
        return Directory.EnumerateFiles(folder)
            .Concat(Directory.EnumerateDirectories(folder).Where(sub => !IsKept(sub, kept))
                .SelectMany(sub => Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories)));
    }

    private static bool IsKept(string subfolder, List<string> kept) =>
        kept.Any(keep => System.IO.Path.GetFullPath(keep).Equals(System.IO.Path.GetFullPath(subfolder), StringComparison.OrdinalIgnoreCase));
}
