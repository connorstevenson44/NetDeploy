using System.Text.Json.Serialization;

namespace NetDeploy.Core.Steps;

/// <summary>Copies a folder to "&lt;folder&gt;.backups\&lt;timestamp&gt;" so a failed deploy can be rolled back.</summary>
public sealed class BackupStep : DeployStep
{
    [JsonPropertyName("path")]
    public required string Folder { get; init; }

    /// <summary>How many backups to keep; older ones are deleted.</summary>
    public int Keep { get; init; } = 3;

    public override string Describe() => $"Back up {Folder}";

    /// <summary>Removing the install also removes its backups.</summary>
    protected internal override IEnumerable<DeployStep> DefaultCleanup() =>
        [new DeleteFolderStep { Path = Folder.TrimEnd('\\', '/') + ".backups" }];

    public override Task RunAsync(StepContext context, CancellationToken cancellationToken)
    {
        var folder = Path.GetFullPath(context.Resolve(Folder)).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(folder))
        {
            context.Log.Info("Nothing to back up yet, the folder does not exist.");
            return Task.CompletedTask;
        }

        var backupRoot = folder + ".backups";
        var backup = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        if (context.DryRun)
        {
            context.Log.Info($"Would back up to {backup}");
            return Task.CompletedTask;
        }

        FileSystem.CopyDirectory(folder, backup);
        context.Rollback.Backups.Add(new BackupRecord(folder, backup));
        context.Log.Info($"Backed up to {backup}");

        // Timestamps sort by name, so the oldest backups come last.
        foreach (var old in Directory.GetDirectories(backupRoot).OrderDescending().Skip(Keep))
            Directory.Delete(old, recursive: true);

        return Task.CompletedTask;
    }

    protected override IEnumerable<string> ValidateOptions()
    {
        if (Keep < 1)
            yield return "'keep' must be at least 1.";
    }
}
