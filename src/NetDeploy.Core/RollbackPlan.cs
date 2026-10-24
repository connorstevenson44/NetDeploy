using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

/// <summary>A backup of Target, which left out the Nested folders; restoring it leaves them in place.</summary>
internal sealed record BackupRecord(string Target, string BackupPath, IReadOnlyList<string> Nested);

/// <summary>Restores the backups a deploy made and restarts the services it stopped.</summary>
public sealed class RollbackPlan
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(60);

    internal List<BackupRecord> Backups { get; } = [];
    internal List<string> StoppedServices { get; } = [];
    internal List<string> StartedServices { get; } = [];

    public bool IsAvailable => Backups.Count > 0;

    /// <summary>Blocks while files are copied, so call it off the UI thread.</summary>
    public void Restore(IDeployLog log)
    {
        // A service the deploy started runs the new files and holds them open, so it is stopped too.
        foreach (var service in StoppedServices.Union(StartedServices, StringComparer.OrdinalIgnoreCase).Where(WindowsServices.Exists))
        {
            log.Info($"Stopping service {service}");
            WindowsServices.Stop(service, ServiceTimeout);
        }

        foreach (var backup in Enumerable.Reverse(Backups))
        {
            log.Info($"Restoring {backup.Target} from {backup.BackupPath}");
            FileSystem.ReplaceDirectory(backup.BackupPath, backup.Target, keep: backup.Nested);
        }

        // Only the services that ran before the deploy are started again.
        foreach (var service in StoppedServices.Where(WindowsServices.Exists))
        {
            log.Info($"Starting service {service}");
            WindowsServices.Start(service, ServiceTimeout);
        }

        log.Info("Rollback finished.");
    }
}
