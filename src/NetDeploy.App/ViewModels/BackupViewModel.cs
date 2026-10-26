using NetDeploy.Core.Installs;

namespace NetDeploy.App.ViewModels;

/// <summary>A backup of a folder, as the folder view lists it under the folder.</summary>
public sealed record BackupViewModel(InstallBackup Backup, string Folder)
{
    public string Label => Backup.Taken.ToString("d MMM yyyy, HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
    public string Path => Backup.Directory;
}
