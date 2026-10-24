using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetDeploy.App.Services;
using NetDeploy.Core;

namespace NetDeploy.App.ViewModels;

/// <summary>Tools for the install folder: remove or clone an install, run a file, and finalize staged edits and deletes.</summary>
public sealed partial class MainViewModel
{
    private string LogName => _package?.Manifest.App.Name ?? Path.GetFileName(Folder?.Root.TrimEnd(Path.DirectorySeparatorChar)) ?? "netdeploy";

    /// <summary>
    /// Removes an install, confirming each step. With a package open whose install block matches, its undo steps run;
    /// otherwise the install's services and its folder are removed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RemoveInstallAsync(InstallTarget target)
    {
        if (target.Install is not { } install
            || !Confirm($"Remove {target.Label} in {install.Directory}?{Environment.NewLine}{Environment.NewLine}You'll see and OK each step before it runs.", "Remove install"))
            return;

        IsRunning = true;
        SelectedTab = LogTab;
        try
        {
            Log.Clear();
            using var log = UiLog.Create(Log, LogName, "remove");
            await RemoveAsync(install, log);
        }
        finally
        {
            IsRunning = false;
            RefreshFolder();
        }
    }

    /// <summary>
    /// Starts a new install from a copy of an install, so its settings and data come along. With a package open it
    /// becomes the deploy's target; otherwise a package is opened first. The deploy itself runs as usual.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CloneInstall(InstallTarget target)
    {
        if (target.Install is not { } source)
            return;
        if (!HasPackage)
        {
            var dialog = new OpenFileDialog { Title = "Open the package to deploy for the clone", Filter = "Deploy packages (*.zip)|*.zip" };
            if (dialog.ShowDialog() != true)
                return;
            Load(dialog.FileName);
            if (!HasPackage)
                return;
            // Point the install folder at the one the source lives in.
            if (FolderField is not null)
                FolderField.Value = Path.GetDirectoryName(source.Directory)!;
        }
        if (Install is null)
        {
            MessageBox.Show("This package has no install folder, so it can't make a clone.", "Clone", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Install.Selected = Install.Targets.First(choice => choice.IsNew);
        Install.CopyFrom = Install.CopySources.FirstOrDefault(choice => choice.Install?.Directory == source.Directory);
        Status = "Type a name for the new install on the left, then deploy.";
    }

    /// <summary>Runs an exe, script or batch file from the folder, after asking for its arguments.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RunFileAsync(FolderFileViewModel file)
    {
        var answers = await _user.AskInputsAsync($"Run {file.Name}",
            new Dictionary<string, InputField> { ["Arguments"] = new() { Label = "Arguments", Optional = true } }, CancellationToken.None);
        if (answers is null)
            return;

        IsRunning = true;
        SelectedTab = LogTab;
        _cancellation = new CancellationTokenSource();
        try
        {
            using var log = UiLog.Create(Log, LogName, "run");
            var token = _cancellation.Token;
            var exitCode = await Task.Run(() => FileRunner.RunAsync(file.FullPath, answers["Arguments"], log, token));
            Status = $"{file.Name} exited with code {exitCode}.";
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or IOException)
        {
            Status = $"{file.Name} didn't finish: {ex.Message}";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsRunning = false;
            Folder?.Reload();
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task StartServicesAsync(FolderNodeViewModel install) => ControlServicesAsync(install, ServiceCommand.Start);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task StopServicesAsync(FolderNodeViewModel install) => ControlServicesAsync(install, ServiceCommand.Stop);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RestartServicesAsync(FolderNodeViewModel install) => ControlServicesAsync(install, ServiceCommand.Restart);

    /// <summary>Starts, stops or restarts the Windows services that run from an install. Stopping one is asked first.</summary>
    private async Task ControlServicesAsync(FolderNodeViewModel install, ServiceCommand command)
    {
        var services = install.ServiceNames;
        var names = string.Join(", ", services);
        if (services.Count == 0
            || command != ServiceCommand.Start && !Confirm($"{command} {names}?{Environment.NewLine}{Environment.NewLine}The app stops working until the service is running again.", $"{command} service"))
            return;

        IsRunning = true;
        try
        {
            using var log = UiLog.Create(Log, LogName, "services");
            await Task.Run(() => Installations.Control(services, command, log));
            Status = $"{names}: {command.ToString().ToLowerInvariant()} done.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.ServiceProcess.TimeoutException)
        {
            // Without administrator rights Windows refuses, and says so only as "cannot open the service".
            var hint = Elevation.IsAdministrator ? "" : " Starting and stopping services needs administrator rights.";
            Status = $"{names}: {ex.Message}{hint}";
            MessageBox.Show(ex.Message + hint, $"{command} service", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsRunning = false;
            Folder?.RefreshServices();
        }
    }

    /// <summary>Puts a backup back in place of the folder it was taken of, after asking.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RestoreBackupAsync(BackupViewModel backup)
    {
        if (!Confirm($"Put back the backup from {backup.Label} in {backup.Folder}?{Environment.NewLine}{Environment.NewLine}"
                + "Every file in the folder is replaced with the backup's. Services that run from it are stopped first and started again after.", "Restore backup"))
            return;

        IsRunning = true;
        SelectedTab = LogTab;
        try
        {
            Log.Clear();
            using var log = UiLog.Create(Log, LogName, "restore");
            await Task.Run(() => Installations.Restore(backup.Backup, backup.Folder, log));
            Status = $"Restored {backup.Folder} from the backup of {backup.Label}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            Status = $"The restore didn't finish: {ex.Message}";
            MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
            RefreshFolder();
        }
    }

    /// <summary>Writes the staged settings edits and deletes the staged files, after showing exactly what will happen.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void FinalizeFolder()
    {
        var folder = Folder!;
        if (folder.FileWithTextError is { } unreadable)
        {
            MessageBox.Show($"The text of {unreadable.FileName} can't be read: {unreadable.TextError}{Environment.NewLine}{Environment.NewLine}Put it right, or drop the text changes, then finalize.",
                "Finalize", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!folder.HasPending
            || !Confirm($"Make these changes in {folder.Root}?{Environment.NewLine}{Environment.NewLine}{folder.PendingSummary}", "Finalize"))
            return;
        try
        {
            folder.Apply();
            Status = "Changes written.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Some changes weren't made", MessageBoxButton.OK, MessageBoxImage.Error);
            folder.Reload();
        }
    }
}
