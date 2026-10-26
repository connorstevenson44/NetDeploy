using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetDeploy.App.Services;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Installs;
using NetDeploy.Core.Manifest;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// The workspace: a package to deploy, the install folder it goes into, and the deploy config. The left side holds
/// the package's settings and the folder tree; the right side the files, steps, config and log, or while a deploy
/// waits for the user, its review. This file opens and closes things; the deploy, the folder tools and the config
/// editor each have their own part.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const int FilesTab = 0;
    public const int StepsTab = 1;
    public const int ConfigTab = 2;
    public const int LogTab = 3;
    public const int AppLogsTab = 4;

    private readonly IUserInteraction _user;
    private readonly UserSettings _settings;
    private readonly DeployHistory? _history;
    private DeployPackage? _package;
    private IReadOnlyCollection<string> _appFiles = [];
    private string? _installFolder;

    /// <param name="user">Asks the person questions and shows them messages.</param>
    /// <param name="settings">What to remember between runs; without it, nothing outlives the window.</param>
    /// <param name="history">Where deploys are recorded and the folder view reads them; without it, they aren't.</param>
    public MainViewModel(IUserInteraction user, UserSettings? settings = null, DeployHistory? history = null)
    {
        _user = user;
        _settings = settings ?? UserSettings.InMemory();
        _history = history;
        RefreshRecent();
        Title = "";
        Status = "Open a package, an install folder or a deploy.json, or drop one onto this window.";
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorViewModel.Title) or nameof(EditorViewModel.FilePath))
                NotifyHeaderChanged();
        };
    }

    /// <summary>The package's parameters, without the install folder, which is edited above the folder tree.</summary>
    public ObservableCollection<InputFieldViewModel> Parameters { get; } = [];

    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <summary>The deploy config: the open package's deploy.json, or one being written.</summary>
    public EditorViewModel Editor { get; } = new();

    /// <summary>The parameter that is the install folder, such as {InstallDir}, shown as the folder tree's path.</summary>
    [ObservableProperty]
    public partial InputFieldViewModel? FolderField { get; private set; }

    /// <summary>Where the deploy goes, for a package with an install block; otherwise null.</summary>
    [ObservableProperty]
    public partial InstallViewModel? Install { get; private set; }

    /// <summary>The install folder: the package's, or one opened on its own to manage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder), nameof(IsEmpty), nameof(IsConfigOnly), nameof(FolderNote), nameof(HeaderTitle), nameof(HeaderDetail))]
    public partial InstallFolderViewModel? Folder { get; private set; }

    public bool HasFolder => Folder is not null;

    /// <summary>Why there is no folder tree with a package open, or null.</summary>
    public string? FolderNote => !HasPackage || Folder is not null ? null
        : Install is null ? "This package doesn't install into a folder."
        : "Choose the install folder to see what is in it.";

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    public partial string Title { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderDetail))]
    public partial string? PackagePath { get; private set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool NeedsElevation { get; private set; }

    /// <summary>What was left out of the package's deploy.json or looks off in it, such as a file from another version.</summary>
    [ObservableProperty]
    public partial string? PackageWarning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsConfigOnly), nameof(FolderNote), nameof(HeaderTitle), nameof(HeaderDetail))]
    [NotifyCanExecuteChangedFor(nameof(DryRunCommand), nameof(DeployCommand))]
    public partial bool HasPackage { get; private set; }

    /// <summary>True once a config is open in the editor, which shows the deploy.json tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsConfigOnly), nameof(HeaderTitle), nameof(HeaderDetail))]
    public partial bool HasEditor { get; private set; }

    /// <summary>Nothing open yet: the workspace shows how to start.</summary>
    public bool IsEmpty => !HasPackage && Folder is null && !HasEditor;

    /// <summary>Only a config is open: there is no package or folder for the left side yet.</summary>
    public bool IsConfigOnly => HasEditor && !HasPackage && Folder is null;

    /// <summary>The header: the package, else the folder, else the config being written.</summary>
    public string HeaderTitle =>
        HasPackage ? Title
        : Folder is not null ? Path.GetFileName(Folder.Root.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : Folder.Root
        : HasEditor ? Editor.Title
        : "NetDeploy";

    public string? HeaderDetail =>
        HasPackage ? PackagePath
        : Folder is not null ? Folder.Root
        : HasEditor ? Editor.FilePath ?? "Not saved yet"
        : "Deploy, update and manage .NET apps";

    /// <summary>Opens a package. A folder whose deploy.json can't be read opens in the editor instead, to fix it.</summary>
    public void Load(string path)
    {
        if (IsRunning)
            return;
        var manifestPath = Path.Combine(path, DeployPackage.ManifestFileName);
        if (!IsEditing(manifestPath) && !CanReplaceConfig())
            return;

        DeployPackage package;
        try
        {
            package = DeployPackage.Open(path);
        }
        catch (ManifestException) when (File.Exists(manifestPath))
        {
            ClosePackage();
            if (OpenInEditor(manifestPath))
                Status = "deploy.json has problems, listed under the form. Fix them and save to open the package.";
            return;
        }
        catch (Exception ex) when (ex is ManifestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't open the package", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ClosePackage();
        _package = package;
        _rollback = null;
        var manifest = package.Manifest;
        Title = $"{manifest.App.Name} {manifest.App.Version}";
        PackagePath = path;
        NeedsElevation = manifest.RequiresAdmin && !Elevation.IsAdministrator;
        PackageWarning = package.Warnings.Count == 0 ? null : string.Join(Environment.NewLine, package.Warnings);
        _appFiles = Installations.AppFiles(package);

        // A folder parameter that is exactly the install folder, such as {InstallDir}, is the folder tree's path.
        var folderParameter = manifest.Install is { } installBlock && Placeholders.Unresolved(installBlock.Folder) is [var only]
            && installBlock.Folder == $"{{{only}}}" ? only : null;
        foreach (var (name, field) in manifest.Parameters)
        {
            var parameter = new InputFieldViewModel(name, field);
            parameter.PropertyChanged += (_, _) => OnParameterChanged();
            if (name.Equals(folderParameter, StringComparison.OrdinalIgnoreCase))
                FolderField = parameter;
            else
                Parameters.Add(parameter);
        }
        // The install folder last used brings that folder's settings with it once it is read (RefreshInstalls), and
        // the install picked in it that install's; a package that doesn't install into a folder has one set of settings.
        RestoreFolder();
        if (manifest.Install is null)
            RestoreValues("");

        Install = manifest.Install is not null ? new InstallViewModel(manifest.App.Version, _appFiles) : null;
        if (Install is not null)
            Install.PropertyChanged += (_, e) =>
            {
                // The install name is part of folder, service and task names.
                if (e.PropertyName == nameof(InstallViewModel.Name))
                    RefreshSteps();
                else if (e.PropertyName is nameof(InstallViewModel.Selected) or nameof(InstallViewModel.CopyFrom))
                    RestoreInstallValues();
            };

        foreach (var step in StepViewModel.Flatten(manifest.Steps))
            Steps.Add(step);
        _stepsByDefinition = Steps.ToDictionary(step => step.Step);
        HasPackage = true;
        RefreshInstalls();
        RefreshSteps();

        if (!IsEditing(manifestPath))
        {
            // A folder's deploy.json is edited in place; a zip's is a copy, saved elsewhere with Save as.
            if (Directory.Exists(path))
                OpenInEditor(manifestPath);
            else
                ShowInEditor(File.ReadAllText(Path.Combine(package.RootPath, DeployPackage.ManifestFileName)), $"deploy.json in {Path.GetFileName(path)}");
        }

        Log.Clear();
        Remember(path, RecentKind.Package);
        SelectedTab = Folder is not null ? FilesTab : StepsTab;
        Status = PackageWarning is null
            ? $"Opened {Words.Count(manifest.Steps.Count, "step", "steps")}. Start with a dry run to see what will change."
            : $"Opened {Words.Count(manifest.Steps.Count, "step", "steps")}, with warnings shown on the left. Start with a dry run to see what will change.";
        NotifyHeaderChanged();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenPackage()
    {
        var dialog = new OpenFileDialog { Title = "Open a deploy package", Filter = "Deploy packages (*.zip)|*.zip" };
        if (dialog.ShowDialog() == true)
            Load(dialog.FileName);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open an install folder" };
        if (dialog.ShowDialog() == true)
            ShowFolder(dialog.FolderName);
    }

    /// <summary>Shows an install folder on its own, to manage: its installs, its files, and tools to change them.</summary>
    public void ShowFolder(string path)
    {
        if (IsRunning || !Directory.Exists(path))
            return;
        ClosePackage();
        SetFolder(path);
        Log.Clear();
        Remember(path, RecentKind.Folder);
        SelectedTab = FilesTab;
        Status = "Go into a folder on the left to see its files. Nothing is changed until you finalize.";
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (Elevation.TryRestartElevated(PackagePath))
            Application.Current.Shutdown();
    }

    /// <summary>Closes the package and everything that came with it, keeping the log and the editor.</summary>
    private void ClosePackage()
    {
        _package?.Dispose();
        _package = null;
        _appFiles = [];
        _installFolder = null;
        _valuesFrom = null;
        _stepsByDefinition = [];
        Parameters.Clear();
        Steps.Clear();
        FolderField = null;
        Install = null;
        SetFolder(null);
        HasPackage = false;
        Title = "";
        PackagePath = null;
        NeedsElevation = false;
        PackageWarning = null;
        Summary = null;
    }

    private void OnParameterChanged()
    {
        RefreshInstalls();
        RefreshSteps();
    }

    private void RefreshSteps()
    {
        // Secrets are left as {Name} so they never show in the plan.
        var values = ParameterValues(includeSecrets: false);
        foreach (var step in Steps)
            step.Refresh(values);
    }

    /// <summary>
    /// Looks for installs in the install folder and shows it in the tree. It only reads the folder's top level here;
    /// files are listed in the background, so typing the folder's path stays smooth.
    /// </summary>
    private void RefreshInstalls()
    {
        if (Install is null || _package?.Manifest.Install is not { } install)
            return;
        var folder = Placeholders.Resolve(install.Folder, ParameterValues(includeSecrets: false));
        // Most changes are to other settings; only a different folder needs the disk read again.
        if (folder == _installFolder)
            return;
        _installFolder = folder;
        _valuesFrom = null;
        RestoreValues(folder);
        // A value just filled in can move the install folder itself, which the refresh that caused has dealt with.
        if (_installFolder != folder)
            return;
        Install.Refresh(folder);
        var usable = Placeholders.Unresolved(folder).Count == 0 && Path.IsPathFullyQualified(folder);
        if (!usable)
            SetFolder(null);
        else if (Folder is null || !SamePath(Folder.Root, folder))
            SetFolder(folder);
    }

    /// <summary>Reads the folder again after something changed it, such as a deploy or a removal.</summary>
    private void RefreshFolder()
    {
        if (Install is not null && _package?.Manifest.Install is { } install)
            Install.Refresh(Placeholders.Resolve(install.Folder, ParameterValues(includeSecrets: false)));
        Folder?.Reload();
    }

    private void SetFolder(string? path)
    {
        if (Folder is not null)
        {
            Folder.PropertyChanged -= OnFolderChanged;
            Folder.Dispose();
        }
        Folder = path is null ? null : new InstallFolderViewModel(path, Install, _appFiles, _history);
        if (Folder is not null)
            Folder.PropertyChanged += OnFolderChanged;
        OnPropertyChanged(nameof(FolderNote));
        // The Files and App logs tabs hide without a folder; don't leave one showing.
        if (Folder is null && SelectedTab is FilesTab or AppLogsTab)
            SelectedTab = HasPackage ? StepsTab : HasEditor ? ConfigTab : LogTab;
        else if (Folder?.HasLogs != true && SelectedTab == AppLogsTab)
            SelectedTab = FilesTab;
    }

    // The App logs tab hides when the folder the view goes into has no logs; don't leave it showing.
    private void OnFolderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallFolderViewModel.HasLogs) && Folder?.HasLogs != true && SelectedTab == AppLogsTab)
            SelectedTab = FilesTab;
    }

    /// <summary>The parameter values, plus {Instance}, {InstanceSuffix} and {InstanceDir} for the chosen install.</summary>
    private Dictionary<string, string> ParameterValues(bool includeSecrets)
    {
        var values = Parameters.Append(FolderField).OfType<InputFieldViewModel>()
            .Where(parameter => includeSecrets || parameter.Type != InputType.Secret)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        if (_package?.Manifest.Install is { } install && Install is not null)
            foreach (var (name, value) in Installations.Variables(Placeholders.Resolve(install.Folder, values), Install.Name))
                values[name] = value;
        return values;
    }

    private void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderDetail));
    }

    private static bool SamePath(string a, string b) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        _cancellation?.Cancel();
        _package?.Dispose();
        Folder?.Dispose();
    }
}
