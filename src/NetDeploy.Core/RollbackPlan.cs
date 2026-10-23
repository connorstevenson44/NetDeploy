using NetDeploy.Core.Steps;

namespace NetDeploy.Core;

internal sealed record BackupRecord(string Target, string BackupPath);

/// <summary>Restores the backups a deploy made and restarts the services it stopped.</summary>
public sealed class RollbackPlan
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(60);

    internal List<BackupRecord> Backups { get; } = [];
    internal List<string> StoppedServices { get; } = [];

    public bool IsAvailable => Backups.Count > 0;

    /// <summary>Blocks while files are copied, so call it off the UI thread.</summary>
    public void Restore(IDeployLog log)
    {
        foreach (var service in StoppedServices.Where(WindowsServices.Exists))
        {
            log.Info($"Stopping service {service}");
            WindowsServices.Stop(service, ServiceTimeout);
        }

        foreach (var backup in Enumerable.Reverse(Backups))
        {
            log.Info($"Restoring {backup.Target} from {backup.BackupPath}");
            FileSystem.ReplaceDirectory(backup.BackupPath, backup.Target);
        }

        foreach (var service in StoppedServices.Where(WindowsServices.Exists))
        {
            log.Info($"Starting service {service}");
            WindowsServices.Start(service, ServiceTimeout);
        }

        log.Info("Rollback finished.");
    }
}
