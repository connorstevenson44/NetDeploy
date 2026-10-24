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
    /// Starts a new instance from a copy of an install, so its settings and data come along. With a package open it
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
            // Point the install folder at the one the source lives in.
            if (FolderField is not null)
                FolderField.Value = source.IsInstance ? Path.GetDirectoryName(source.Directory)! : source.Directory;
        }
        if (_package?.Manifest.Install is not { Instances: true } || Install is null)
        {
            MessageBox.Show("This package doesn't support instances, so it can't make a clone.", "Clone", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Install.Selected = Install.Targets.First(choice => choice.IsNew);
        Install.CopyFrom = Install.CopySources.FirstOrDefault(choice => choice.Install?.Directory == source.Directory);
        Status = "Name the new instance in the folder tree, then deploy.";
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

    /// <summary>Writes the staged settings edits and deletes the staged files, after showing exactly what will happen.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void FinalizeFolder()
    {
        var folder = Folder!;
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
