using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetDeploy.Core;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// The deploy config, on the deploy.json tab: the open package's, or a new one. Saving a deploy.json opens its folder
/// as the package, so a config can be written and tried without leaving the window.
/// </summary>
public sealed partial class MainViewModel
{
    private const string ConfigFilter = "Deploy config (deploy.json)|deploy.json|JSON files (*.json)|*.json";

    /// <summary>Starts a new config. It replaces the open package, whose config it would otherwise be mistaken for.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void NewConfig()
    {
        if (!CanReplaceConfig())
            return;
        ClosePackage();
        Editor.New();
        HasEditor = true;
        ShowEditor("A small config to start from. Add steps on the left, then save it as deploy.json next to your app's files.");
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenConfig()
    {
        var dialog = new OpenFileDialog { Title = "Open a deploy config", Filter = ConfigFilter };
        if (dialog.ShowDialog() == true)
            EditConfig(dialog.FileName);
    }

    /// <summary>
    /// Opens a deploy config. A deploy.json NetDeploy can read opens its folder as the package with it; any other
    /// opens in the editor on its own.
    /// </summary>
    public void EditConfig(string path)
    {
        if (IsRunning)
            return;
        if (Path.GetFileName(path).Equals(DeployPackage.ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            Load(Path.GetDirectoryName(path)!);
            SelectedTab = ConfigTab;
            return;
        }
        if (!CanReplaceConfig())
            return;
        ClosePackage();
        if (OpenInEditor(path))
            ShowEditor(Editor.VersionWarning ?? "Pick a step on the left to change it. Problems are checked as you type.");
    }

    [RelayCommand]
    private void SaveConfig() => SaveEditor(Editor.FilePath);

    [RelayCommand]
    private void SaveConfigAs() => SaveEditor(null);

    /// <summary>Opens a file in the editor, or says why it can't be read.</summary>
    private bool OpenInEditor(string path)
    {
        try
        {
            Editor.Open(path);
        }
        catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't open the config", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        HasEditor = true;
        return true;
    }

    /// <summary>Shows a config that has no file of its own, such as the deploy.json inside a zip.</summary>
    private void ShowInEditor(string json, string name)
    {
        try
        {
            Editor.Load(json, name);
            HasEditor = true;
        }
        catch (ManifestException)
        {
            // The package opened, so its deploy.json reads; this can't happen, and the editor is only a view of it.
        }
    }

    private void ShowEditor(string status)
    {
        SelectedTab = ConfigTab;
        Status = status;
        NotifyHeaderChanged();
    }

    private bool IsEditing(string path) => Editor.FilePath is { } open && SamePath(open, path);

    /// <summary>Asks first when the config being edited has unsaved changes.</summary>
    private bool CanReplaceConfig() =>
        !Editor.IsDirty || Confirm("The deploy config you are editing has unsaved changes. Drop them?", "Unsaved changes");

    /// <summary>
    /// Saves to the path, or asks where when there is none. A saved deploy.json opens its folder as the package, or
    /// reloads it when it is the open one, so the steps and the tree follow the change.
    /// </summary>
    private void SaveEditor(string? path)
    {
        if (path is null)
        {
            var dialog = new SaveFileDialog { Title = "Save the deploy config", FileName = DeployPackage.ManifestFileName, Filter = ConfigFilter };
            if (dialog.ShowDialog() != true)
                return;
            path = dialog.FileName;
        }
        try
        {
            Editor.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't save the config", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (Editor.HasProblems)
            Status = $"Saved {path}, but it has problems NetDeploy won't accept. They are listed under the form.";
        else if (Path.GetFileName(path).Equals(DeployPackage.ManifestFileName, StringComparison.OrdinalIgnoreCase) && !IsRunning)
        {
            Load(Path.GetDirectoryName(path)!);
            SelectedTab = ConfigTab;
            Status = $"Saved {path}. Its folder is open as the package: try a dry run.";
        }
        else
            Status = $"Saved {path}.";
    }
}
