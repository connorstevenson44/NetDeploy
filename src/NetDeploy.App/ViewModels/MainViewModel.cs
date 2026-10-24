using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetDeploy.App.Services;
using NetDeploy.Core;
using NetDeploy.Core.Editing;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>What the window shows: the home screen, a package to deploy, an install folder, or a deploy config being edited.</summary>
public enum AppScreen { Home, Deploy, Folder, Edit }

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IUserInteraction _user;
    private DeployPackage? _package;
    private Dictionary<DeployStep, StepViewModel> _stepsByDefinition = [];
    private CancellationTokenSource? _cancellation;
    private RollbackPlan? _rollback;

    public MainViewModel(IUserInteraction user)
    {
        _user = user;
        Title = "No package open";
        Status = "Open a package (.zip), or drop a .zip or a folder with deploy.json onto this window.";
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorViewModel.Title) or nameof(EditorViewModel.FilePath))
            {
                OnPropertyChanged(nameof(HeaderTitle));
                OnPropertyChanged(nameof(HeaderDetail));
            }
        };
    }

    /// <summary>The deploy config on the edit screen. It is kept when you leave the screen, so you can come back to it.</summary>
    public EditorViewModel Editor { get; } = new();

    /// <summary>True once a config has been opened or started, so the home screen offers to go back to it.</summary>
    [ObservableProperty]
    public partial bool HasEditor { get; set; }

    public ObservableCollection<InputFieldViewModel> Parameters { get; } = [];
    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <summary>One FilePlanViewModel per copy step, or a FilePlanError when its files can't be listed yet.</summary>
    public ObservableCollection<object> FilePlans { get; } = [];

    public const int StepsTab = 0;
    public const int FilesTab = 1;

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>True when the settings changed after the files were listed.</summary>
    [ObservableProperty]
    public partial bool FilesAreStale { get; set; }

    [ObservableProperty]
    public partial bool ShowUnchangedFiles { get; set; }

    /// <summary>Where the deploy goes, for a package with an install block; otherwise null.</summary>
    [ObservableProperty]
    public partial InstallViewModel? Install { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail))]
    public partial string? PackagePath { get; set; }

    /// <summary>The header: the package on the deploy screen, the folder on the folder screen.</summary>
    public string HeaderTitle => Screen switch
    {
        AppScreen.Deploy => Title,
        AppScreen.Folder => Path.GetFileName(Folder?.Root.TrimEnd(Path.DirectorySeparatorChar)) ?? "",
        AppScreen.Edit => Editor.Title,
        _ => "Deploy or manage a .NET app",
    };

    public string? HeaderDetail => Screen switch
    {
        AppScreen.Deploy => PackagePath,
        AppScreen.Folder => Folder?.Root,
        AppScreen.Edit => Editor.FilePath ?? "Not saved yet",
        _ => null,
    };

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool NeedsElevation { get; set; }

    [ObservableProperty]
    public partial bool HasPackage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(IsDeploy), nameof(IsFolder), nameof(IsEdit), nameof(HeaderTitle), nameof(HeaderDetail))]
    public partial AppScreen Screen { get; set; }

    public bool IsHome => Screen == AppScreen.Home;
    public bool IsDeploy => Screen == AppScreen.Deploy;
    public bool IsFolder => Screen == AppScreen.Folder;
    public bool IsEdit => Screen == AppScreen.Edit;

    /// <summary>The install folder typed on the home screen.</summary>
    [ObservableProperty]
    public partial string FolderPath { get; set; } = "";

    /// <summary>The install folder being managed, on the folder screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle), nameof(HeaderDetail))]
    public partial InstallFolderViewModel? Folder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(OpenPackageCommand), nameof(DryRunCommand), nameof(DeployCommand), nameof(CancelCommand), nameof(RollBackCommand), nameof(RemoveInstallCommand),
        nameof(RunFileCommand), nameof(RemoveFolderInstallCommand), nameof(CloneInstallCommand), nameof(FinalizeFolderCommand), nameof(HomeCommand), nameof(TryConfigCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsIdle => !IsRunning;

    private bool CanRun => _package is not null && !IsRunning;
    private bool CanRollBack => _rollback is { IsAvailable: true } && !IsRunning;

    public void Load(string path)
    {
        if (IsRunning)
            return;

        DeployPackage package;
        try
        {
            package = DeployPackage.Open(path);
        }
        catch (Exception ex) when (ex is ManifestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't open the package", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _package?.Dispose();
        _package = package;
        HasPackage = true;
        Screen = AppScreen.Deploy;
        _rollback = null;

        var manifest = package.Manifest;
        Title = $"{manifest.App.Name} {manifest.App.Version}";
        PackagePath = path;
        NeedsElevation = manifest.RequiresAdmin && !Elevation.IsAdministrator;

        Parameters.Clear();
        foreach (var (name, field) in manifest.Parameters)
        {
            var parameter = new InputFieldViewModel(name, field);
            parameter.PropertyChanged += (_, _) => OnParameterChanged();
            Parameters.Add(parameter);
        }

        Install = manifest.Install is { } install
            ? new InstallViewModel(install, manifest.App.Version, Installations.AppFiles(package))
            : null;
        if (Install is not null)
            Install.PropertyChanged += (_, e) =>
            {
                // The instance name is part of folder, service and task names.
                if (e.PropertyName == nameof(InstallViewModel.Name))
                    OnInstanceChanged();
            };
        RefreshInstalls();

        Steps.Clear();
        foreach (var step in StepViewModel.Flatten(manifest.Steps))
            Steps.Add(step);
        _stepsByDefinition = Steps.ToDictionary(step => step.Step);
        RefreshSteps();
        FilePlans.Clear();
        FilesAreStale = false;
        if (SelectedTab == FilesTab)
            RefreshFiles();

        Log.Clear();
        Status = $"{manifest.Steps.Count} steps. Try a dry run first to see what will change.";
        DryRunCommand.NotifyCanExecuteChanged();
        DeployCommand.NotifyCanExecuteChanged();
        RollBackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenPackage()
    {
        var dialog = new OpenFileDialog { Title = "Open a deploy package", Filter = "Deploy packages (*.zip)|*.zip" };
        if (dialog.ShowDialog() == true)
            Load(dialog.FileName);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Home()
    {
        Screen = AppScreen.Home;
        Status = "Deploy a package, or open an install folder to manage it.";
    }

    private const string ConfigFilter = "Deploy config (deploy.json)|deploy.json|JSON files (*.json)|*.json";

    [RelayCommand]
    private void NewConfig()
    {
        if (!CanReplaceConfig())
            return;
        Editor.New();
        ShowEditor();
    }

    [RelayCommand]
    private void OpenConfig()
    {
        if (!CanReplaceConfig())
            return;
        var dialog = new OpenFileDialog { Title = "Open a deploy config", Filter = ConfigFilter };
        if (dialog.ShowDialog() == true)
            EditConfig(dialog.FileName, confirmed: true);
    }

    /// <summary>Opens a deploy.json in the editor, after asking whether to drop unsaved changes to the one open now.</summary>
    public void EditConfig(string path, bool confirmed = false)
    {
        if (IsRunning || !confirmed && !CanReplaceConfig())
            return;
        try
        {
            Editor.Open(path);
        }
        catch (Exception ex) when (ex is ManifestException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't open the config", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        ShowEditor();
    }

    [RelayCommand]
    private void ResumeEditing() => ShowEditor();

    [RelayCommand]
    private void SaveConfig() => SaveEditor(Editor.FilePath);

    [RelayCommand]
    private void SaveConfigAs() => SaveEditor(null);

    /// <summary>Saves the config and opens its folder as a package, ready for a dry run.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void TryConfig()
    {
        if (Editor.HasProblems)
        {
            MessageBox.Show("Fix the problems listed under the form first.", "Try the config", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!SaveEditor(Editor.FilePath))
            return;
        var path = Editor.FilePath!;
        if (!Path.GetFileName(path).Equals("deploy.json", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("A package reads its config from deploy.json. Use Save as to save it under that name.", "Try the config", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Load(Path.GetDirectoryName(path)!);
    }

    private void ShowEditor()
    {
        HasEditor = true;
        Screen = AppScreen.Edit;
        Status = "Pick a step on the left to change it. Problems are checked as you type.";
    }

    /// <summary>Asks first when the config being edited has unsaved changes.</summary>
    private bool CanReplaceConfig() =>
        !Editor.IsDirty || Confirm("The config you are editing has unsaved changes. Drop them?", "Unsaved changes");

    /// <summary>Saves to the path, or asks where when there is none. Returns false when it wasn't saved.</summary>
    private bool SaveEditor(string? path)
    {
        if (path is null)
        {
            var dialog = new SaveFileDialog { Title = "Save the deploy config", FileName = "deploy.json", Filter = ConfigFilter };
            if (dialog.ShowDialog() != true)
                return false;
            path = dialog.FileName;
        }
        try
        {
            Editor.Save(path);
            Status = Editor.HasProblems ? $"Saved {path}, but it has problems NetDeploy won't accept." : $"Saved {path}.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't save the config", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open an install folder" };
        if (dialog.ShowDialog() != true)
            return;
        FolderPath = dialog.FolderName;
        OpenFolder();
    }

    /// <summary>Shows an install folder: its installs, its files, and tools to change them.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var path = FolderPath.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            MessageBox.Show($"\"{path}\" isn't a folder on this computer.", "Open an install folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Folder = new InstallFolderViewModel(path);
        Log.Clear();
        Status = "Hover a file for its tools. Edits and deletes are only made when you finalize.";
        Screen = AppScreen.Folder;
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
        _cancellation = new CancellationTokenSource();
        try
        {
            using var log = UiLog.Create(Log, Path.GetFileName(Folder!.Root), "run");
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

    /// <summary>
    /// Removes an install found in the folder. With a package open whose install block matches, its undo steps run
    /// too; otherwise the install's services and its folder are removed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RemoveFolderInstallAsync(InstallTarget target)
    {
        var folder = Folder!;
        if (target.Install is not { } install
            || !Confirm($"Remove {target.Label} in {install.Directory}?{Environment.NewLine}{Environment.NewLine}You'll see and OK each step before it runs.", "Remove install"))
            return;

        IsRunning = true;
        try
        {
            using var log = UiLog.Create(Log, Path.GetFileName(folder.Root), "remove");
            var all = folder.Installs.Select(other => other.Install).OfType<Installation>().ToList();
            var result = await Task.Run(() => new InstallRemover(_user, log).RemoveAsync(install, all, null, new Dictionary<string, string>(), CancellationToken.None));
            Status = result == DeployResult.Succeeded ? $"Removed {install.Directory}." : "Removal stopped or had errors. See the log.";
        }
        finally
        {
            IsRunning = false;
            folder.Reload();
        }
    }

    /// <summary>
    /// Clones an install: opens a package to deploy as a new instance that starts from a copy of this one, so its
    /// settings and data come along. The deploy itself runs from the deploy screen as usual.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void CloneInstall(InstallTarget target)
    {
        if (target.Install is not { } source)
            return;
        var dialog = new OpenFileDialog { Title = "Open the package to deploy for the clone", Filter = "Deploy packages (*.zip)|*.zip" };
        if (dialog.ShowDialog() != true)
            return;
        Load(dialog.FileName);
        if (_package?.Manifest.Install is not { Instances: true } install || Install is null)
        {
            MessageBox.Show("This package doesn't support instances, so it can't make a clone.", "Clone", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Point the install folder at the one the source lives in, then start a new instance from it.
        var installFolder = source.IsInstance ? Path.GetDirectoryName(source.Directory)! : source.Directory;
        if (Placeholders.Unresolved(install.Folder) is [var parameter] && install.Folder == $"{{{parameter}}}")
            if (Parameters.FirstOrDefault(field => field.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase)) is { } field)
                field.Value = installFolder;
        Install.Selected = Install.Targets.First(choice => choice.IsNew);
        Install.CopyFrom = Install.CopySources.FirstOrDefault(choice => choice.Install?.Directory == source.Directory);
        Status = "Name the new instance, then deploy.";
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (Elevation.TryRestartElevated(PackagePath))
            Application.Current.Shutdown();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRunAsync() => RunAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DeployAsync() => RunAsync(dryRun: false);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRollBack))]
    private async Task RollBackAsync()
    {
        if (!Confirm("Restore the backup from the last deploy and restart the services it stopped?", "Roll back"))
            return;

        IsRunning = true;
        try
        {
            using var log = UiLog.Create(Log, _package!.Manifest.App.Name, "rollback");
            await RestoreAsync(log);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        var errors = Parameters.Select(parameter => parameter.Validate()).OfType<string>().ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Check the parameters", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Install?.Problem is { } installProblem)
        {
            MessageBox.Show(installProblem, "Check where to install", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!dryRun && NeedsElevation)
        {
            MessageBox.Show("This package needs administrator rights. Use Restart as administrator first.", "Administrator rights needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!dryRun && !Confirm(Install is null ? $"Deploy {Title}?" : $"Deploy {Title}?{Environment.NewLine}{Environment.NewLine}{Install.Summary}", "Deploy"))
            return;

        foreach (var step in Steps)
            step.State = StepState.Pending;
        Log.Clear();
        _rollback = null;
        Status = dryRun ? "Dry run in progress..." : "Deploying...";
        IsRunning = true;

        var package = _package!;
        using var log = UiLog.Create(Log, package.Manifest.App.Name, dryRun ? "dryrun" : "deploy");
        _cancellation = new CancellationTokenSource();
        try
        {
            var runner = new DeployRunner(_user, log);
            runner.StepChanged += (step, state) =>
                Application.Current.Dispatcher.InvokeAsync(() => _stepsByDefinition[step].State = state);

            var values = Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value);
            var token = _cancellation.Token;
            var options = new DeployOptions
            {
                FileActions = FilePlans.OfType<FilePlanViewModel>().ToDictionary(plan => (DeployStep)plan.Step, plan => plan.ChosenActions),
            };
            if (Install is not null)
                options = Install.Apply(options);
            var replaced = Install?.Replaced;
            var outcome = await Task.Run(() => runner.RunAsync(package, values, dryRun, options, token));

            Status = outcome.Result switch
            {
                DeployResult.Succeeded => dryRun ? "Dry run finished. Nothing was changed." : "Deploy finished.",
                DeployResult.Failed => $"Failed at \"{outcome.FailedStep!.Title}\": {outcome.Error!.Message}",
                _ => "Cancelled.",
            } + $" Log: {log.FilePath}";

            if (!dryRun && outcome.Result != DeployResult.Succeeded && outcome.Rollback.IsAvailable)
            {
                _rollback = outcome.Rollback;
                if (Confirm($"{Status}{Environment.NewLine}{Environment.NewLine}Restore the backup and restart the services this deploy stopped?", "Roll back"))
                    await RestoreAsync(log);
            }

            // A rename or a conversion leaves the old install behind until the new one works.
            if (!dryRun && outcome.Result == DeployResult.Succeeded && replaced is not null
                && Confirm($"The new install is in place. Remove the old one in {replaced.Directory} now? You'll OK each step.", "Remove the old install"))
                await RemoveAsync(replaced, log);
            RefreshInstalls();
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsRunning = false;
        }
    }

    /// <summary>Removes an install found in the folder, confirming each step.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RemoveInstallAsync(InstallTarget target)
    {
        if (target.Install is not { } install
            || !Confirm($"Remove {target.Label} in {install.Directory}?{Environment.NewLine}{Environment.NewLine}You'll see and OK each step before it runs.", "Remove install"))
            return;

        IsRunning = true;
        try
        {
            Log.Clear();
            using var log = UiLog.Create(Log, _package!.Manifest.App.Name, "remove");
            await RemoveAsync(install, log);
            RefreshInstalls();
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RemoveAsync(Installation install, IDeployLog log)
    {
        var package = _package!;
        var all = Install!.Installs.ToList();
        var values = ParameterValues(includeSecrets: true);
        var result = await Task.Run(() => new InstallRemover(_user, log).RemoveAsync(install, all, package, values, CancellationToken.None));
        Status = result switch
        {
            DeployResult.Succeeded => $"Removed {install.Directory}.",
            DeployResult.Failed => $"Removing {install.Directory} had errors. See the log.",
            _ => "Removal stopped. Steps already run are not undone.",
        };
    }

    private async Task RestoreAsync(IDeployLog log)
    {
        var rollback = _rollback!;
        try
        {
            await Task.Run(() => rollback.Restore(log));
            _rollback = null;
            Status = "Rolled back to the backup.";
        }
        catch (Exception ex)
        {
            log.Error($"Rollback failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Rollback failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Lists every copy step's files against the server. It reads the disk, so it runs when the Files tab opens or
    /// on refresh, never on each keystroke: a half-typed folder such as C:\ would list the whole drive.
    /// </summary>
    [RelayCommand]
    private void RefreshFiles()
    {
        if (_package is null)
            return;

        var values = ParameterValues(includeSecrets: true);
        var earlier = FilePlans.OfType<FilePlanViewModel>().ToDictionary(plan => plan.Step);
        FilePlans.Clear();
        foreach (var step in Steps.Where(step => step.Step is CopyFilesStep))
        {
            var copy = (CopyFilesStep)step.Step;
            var title = step.Branch + step.Title;
            try
            {
                var plan = new FilePlanViewModel(copy, title, copy.PlanFiles(_package.RootPath, values));
                plan.CopyChoicesFrom(earlier.GetValueOrDefault(copy));
                plan.ShowUnchanged(ShowUnchangedFiles);
                FilePlans.Add(plan);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                FilePlans.Add(new FilePlanError(title, ex.Message));
            }
        }
        FilesAreStale = false;
    }

    partial void OnSelectedTabChanged(int value)
    {
        if (value == FilesTab && (FilesAreStale || FilePlans.Count == 0))
            RefreshFiles();
    }

    partial void OnShowUnchangedFilesChanged(bool value)
    {
        foreach (var plan in FilePlans.OfType<FilePlanViewModel>())
            plan.ShowUnchanged(value);
    }

    private void OnParameterChanged()
    {
        RefreshInstalls();
        OnInstanceChanged();
    }

    private void OnInstanceChanged()
    {
        RefreshSteps();
        FilesAreStale = FilePlans.Count > 0;
    }

    private void RefreshSteps()
    {
        // Secrets are left as {Name} so they never show in the plan.
        var values = ParameterValues(includeSecrets: false);
        foreach (var step in Steps)
            step.Refresh(values);
    }

    /// <summary>Looks for installs in the install folder. It only lists the folder and its subfolders, so it is cheap.</summary>
    private void RefreshInstalls()
    {
        if (Install is null || _package?.Manifest.Install is not { } install)
            return;
        Install.Refresh(Placeholders.Resolve(install.Folder, ParameterValues(includeSecrets: false)));
    }

    /// <summary>The parameter values, plus {Instance}, {InstanceSuffix} and {InstanceDir} for the chosen install.</summary>
    private Dictionary<string, string> ParameterValues(bool includeSecrets)
    {
        var values = Parameters
            .Where(parameter => includeSecrets || parameter.Type != InputType.Secret)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        if (_package?.Manifest.Install is { } install && Install is not null)
            foreach (var (name, value) in Installations.Variables(Placeholders.Resolve(install.Folder, values), Install.Name))
                values[name] = value;
        return values;
    }

    private static bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        _cancellation?.Cancel();
        _package?.Dispose();
    }
}
