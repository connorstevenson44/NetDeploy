using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core;
using NetDeploy.Core.Config;
using NetDeploy.Core.Logs;

namespace NetDeploy.App.ViewModels;

/// <summary>One file in the file list, with what can be done to it.</summary>
public sealed partial class FolderFileViewModel : ObservableObject
{
    public FolderFileViewModel(string root, string path)
    {
        FullPath = path;
        RelativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
        var slash = RelativePath.LastIndexOf('/');
        Folder = slash < 0 ? "" : RelativePath[..slash];
        Name = RelativePath[(slash + 1)..];
        IsConfig = ConfigFormats.IsConfig(Name);
        CanRun = FileRunner.CanRun(Name);
        CanEdit = !BinaryExtensions.Contains(Path.GetExtension(Name));
    }

    // Files that are never text, so they get no Edit button. Anything else is checked when it is opened.
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".msi", ".zip", ".7z", ".gz", ".rar", ".nupkg", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico",
        ".pdf", ".db", ".sqlite", ".mdf", ".ldf", ".bak", ".woff", ".woff2", ".ttf", ".otf", ".eot", ".so", ".bin", ".dat",
    };

    public string FullPath { get; }

    /// <summary>The path from the folder being listed, with forward slashes.</summary>
    public string RelativePath { get; }

    public string Folder { get; }
    public string Name { get; }
    public bool IsConfig { get; }
    public bool CanRun { get; }

    /// <summary>True for a file that may be text, which opens in the editor.</summary>
    public bool CanEdit { get; }
    public Thickness IconMargin => FileTree.IconMargin(Folder.Length == 0 ? 0 : Folder.Count(c => c == '/') + 1);

    public PackIconKind Icon => IsConfig ? PackIconKind.FileCogOutline
        : CanRun ? PackIconKind.ApplicationCogOutline
        : PackIconKind.FileOutline;

    /// <summary>Marked for deletion when the changes are finalized.</summary>
    [ObservableProperty]
    public partial bool IsStagedForDelete { get; set; }

    /// <summary>A file with changes not yet written.</summary>
    [ObservableProperty]
    public partial bool HasEdits { get; set; }
}

/// <summary>A backup of a folder, as the folder view lists it under the folder.</summary>
public sealed record BackupViewModel(InstallBackup Backup, string Folder)
{
    public string Label => Backup.Taken.ToString("d MMM yyyy, HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
    public string Path => Backup.Directory;
}

/// <summary>A deploy to the folder, as the folder view lists it under the folder.</summary>
public sealed partial class HistoryEntryViewModel(DeployRecord record)
{
    public DeployRecord Record => record;
    public DeployResult Result => record.Result;

    public PackIconKind Icon => record.Result switch
    {
        DeployResult.Succeeded => PackIconKind.CheckCircleOutline,
        DeployResult.Failed => PackIconKind.AlertCircleOutline,
        _ => PackIconKind.Cancel,
    };

    /// <summary>Such as "1.1.0 deployed" or "1.1.0 failed, rolled back".</summary>
    public string Label => $"{record.Version} " + record.Result switch
    {
        DeployResult.Succeeded => "deployed",
        DeployResult.Failed => record.RolledBack ? "failed, rolled back" : "failed",
        _ => record.RolledBack ? "stopped, rolled back" : "stopped",
    };

    public string Detail => $"{record.Time.LocalDateTime.ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.CurrentCulture)} by {record.User}";

    public bool HasLog => record.Log is { } log && File.Exists(log);

    /// <summary>Opens the deploy's log in the program Windows uses for .log files.</summary>
    [RelayCommand]
    private void OpenLog()
    {
        if (HasLog)
            Process.Start(new ProcessStartInfo(record.Log!) { UseShellExecute = true });
    }
}

/// <summary>How an install's Windows services are doing, as the dot beside it shows.</summary>
public enum ServiceState
{
    /// <summary>Not looked at yet: nothing shows.</summary>
    Unknown,

    /// <summary>No service runs a program in the install: a question mark shows.</summary>
    NotFound,

    Running,
    Stopped,

    /// <summary>Some running and some not, or starting, stopping or paused.</summary>
    Mixed,
}

/// <summary>A folder in the install folder, with its install marked. The folder view lists those in the folder it is in.</summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    internal FolderNodeViewModel(string path, string name, Installation? install, IReadOnlyList<string>? services = null, string? packageVersion = null)
    {
        FullPath = path;
        Name = name;
        Target = install is null ? null : new InstallTarget(install);
        ServiceNames = services ?? [];
        PackageVersion = packageVersion;
        Change = packageVersion is null ? null : Installations.Compare(install?.Version, packageVersion);
    }

    public string FullPath { get; }
    public string Name { get; }

    /// <summary>The install this folder holds, for the remove and clone buttons; null for a plain folder.</summary>
    public InstallTarget? Target { get; }

    public bool IsInstall => Target is not null;

    /// <summary>The Windows services that run a program in the install.</summary>
    internal IReadOnlyList<string> ServiceNames { get; }

    /// <summary>True when Windows services run from the install, so they can be started and stopped from here.</summary>
    public bool HasServices => ServiceNames.Count > 0;

    /// <summary>The open package's version, or null with no package open.</summary>
    public string? PackageVersion { get; }

    /// <summary>How the open package compares with this install, shown as an arrow beside its version; null when unknown.</summary>
    public VersionChange? Change { get; }

    public string ChangeTip => Change switch
    {
        VersionChange.Upgrade => $"The package ({PackageVersion}) is newer: deploying here updates it.",
        VersionChange.Downgrade => $"The package ({PackageVersion}) is older: deploying here goes back a version.",
        VersionChange.Same => $"The package is the same version ({PackageVersion}).",
        _ => "",
    };

    [ObservableProperty]
    public partial ServiceState ServiceState { get; private set; }

    /// <summary>Each service and its state, for the dot's tooltip.</summary>
    [ObservableProperty]
    public partial string ServiceTip { get; private set; } = "";

    /// <summary>The root of a new install whose folder isn't there yet.</summary>
    public bool IsMissing => !Directory.Exists(FullPath);

    public string Detail => Target?.Install switch
    {
        { Version: { } version } => "v" + version,
        not null => "",
        _ => IsMissing ? "not created yet" : "",
    };

    public string Kind => IsInstall ? "install" : "";

    public PackIconKind Icon =>
        IsInstall ? PackIconKind.Server : IsMissing ? PackIconKind.FolderPlusOutline : PackIconKind.FolderOutline;

    /// <summary>Shows the states just read of the install's services.</summary>
    internal void ShowServices(IReadOnlyDictionary<string, ServiceControllerStatus?> states)
    {
        if (!IsInstall)
            return;
        if (ServiceNames.Count == 0)
        {
            ServiceState = ServiceState.NotFound;
            ServiceTip = "No Windows service runs from this install";
            return;
        }
        var mine = ServiceNames.Select(name => (Name: name, Status: states.GetValueOrDefault(name))).ToList();
        ServiceState = mine.All(service => service.Status == ServiceControllerStatus.Running) ? ServiceState.Running
            : mine.All(service => service.Status is ServiceControllerStatus.Stopped or null) ? ServiceState.Stopped
            : ServiceState.Mixed;
        ServiceTip = string.Join(Environment.NewLine, mine.Select(service => $"{service.Name}: {Words.Status(service.Status)}"));
    }
}

/// <summary>
/// The last row in the install folder itself: a new install, named in place, so the installs already there stay in
/// view while it is set up. It is the deploy's target once it is picked or named.
/// </summary>
public sealed partial class NewInstallRowViewModel : ObservableObject
{
    private readonly InstallViewModel _install;
    private string _draft = "";

    internal NewInstallRowViewModel(InstallViewModel install)
    {
        _install = install;
        install.PropertyChanged += OnInstallChanged;
    }

    public bool IsTarget => _install.Selected?.IsNew == true;

    public string Name
    {
        get => IsTarget ? _install.Name : _draft;
        set
        {
            _draft = value;
            Choose();
            _install.Name = value;
        }
    }

    /// <summary>Makes the new install the deploy's target, with the name typed before.</summary>
    [RelayCommand]
    private void Pick()
    {
        if (IsTarget)
            return;
        Choose();
        _install.Name = _draft;
    }

    private void Choose()
    {
        if (!IsTarget && _install.Targets.FirstOrDefault(target => target.IsNew) is { } target)
            _install.Selected = target;
    }

    private void OnInstallChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallViewModel.Selected) or nameof(InstallViewModel.Name))
        {
            OnPropertyChanged(nameof(IsTarget));
            OnPropertyChanged(nameof(Name));
        }
    }

    internal void Detach() => _install.PropertyChanged -= OnInstallChanged;
}

/// <summary>
/// An install folder, browsed one folder at a time: the folders in the current one with the installs marked, its
/// files, and changes staged on them. Settings edits and deletes are only written when the user finalizes, all
/// together. With a package open, going into an install, or naming a new one, picks where the deploy goes.
/// </summary>
public sealed partial class InstallFolderViewModel : ObservableObject, IDisposable
{
    /// <summary>The most files listed for one folder, so picking the top of a drive by mistake stays quick.</summary>
    internal const int MaxFiles = 5000;

    /// <summary>The most deploys listed under a folder: the latest are the ones that say what is there now.</summary>
    internal const int MaxHistory = 10;

    private readonly Dictionary<string, ConfigFileViewModel> _opened = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deletes = new(StringComparer.OrdinalIgnoreCase);
    private readonly InstallViewModel? _target;
    private readonly IReadOnlyCollection<string> _appFiles;
    private readonly DeployHistory? _history;
    private readonly FileTree<FolderFileViewModel> _tree;
    private Dictionary<string, Installation> _installsByFolder = [];
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _servicesByFolder = new Dictionary<string, IReadOnlyList<string>>();
    private readonly DispatcherTimer _serviceTimer;
    private List<FolderFileViewModel> _files = [];
    private CancellationTokenSource? _listing;
    private NewInstallRowViewModel? _newInstall;
    private bool _reloading;

    /// <param name="path">The install folder.</param>
    /// <param name="target">With a package open, where its deploy goes; the view goes to it.</param>
    /// <param name="appFiles">The package's app files, which tell its installs apart from other folders.</param>
    /// <param name="history">The deploys made on this machine, listed under the folder they went to.</param>
    public InstallFolderViewModel(string path, InstallViewModel? target = null, IReadOnlyCollection<string>? appFiles = null, DeployHistory? history = null)
    {
        Root = Path.GetFullPath(path);
        _target = target;
        _appFiles = appFiles ?? [];
        _history = history;
        _tree = new FileTree<FolderFileViewModel>(file => file.Folder, file => file.RelativePath, ShowFiles);
        // Services start and stop on their own, so their states are read again every few seconds.
        _serviceTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => RefreshServices(), Dispatcher.CurrentDispatcher);
        if (target is not null)
            target.PropertyChanged += OnTargetChanged;
        Reload();
    }

    public string Root { get; }

    /// <summary>The folders in the current one, then in the install folder itself, the row for a new install.</summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>Installs found in the folder from its layout.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InstallTarget> Installs { get; private set; } = [];

    /// <summary>The folder the view is in: its folders are listed on the left and its files on the right.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoUp), nameof(Location))]
    [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
    public partial FolderNodeViewModel? CurrentFolder { get; private set; }

    /// <summary>The view stops at the install folder.</summary>
    public bool CanGoUp => CurrentFolder is { } current && !IsRoot(current.FullPath);

    /// <summary>Where the view is, from the install folder down, such as "Orders › logs › 2026". The full path is above it.</summary>
    public string Location => CurrentFolder is not { } current ? ""
        : IsRoot(current.FullPath) ? current.Name
        : string.Join(" › ", [RootName, .. Path.GetRelativePath(Root, current.FullPath).Split(Path.DirectorySeparatorChar)]);

    /// <summary>The install folder's own name, or the whole path for the top of a drive.</summary>
    private string RootName => Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : Root;

    /// <summary>Folder headings and files, in path order, as a tree whose folders can be folded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    public partial IReadOnlyList<object> Items { get; private set; } = [];

    public bool HasFiles => Items.Count > 0;

    /// <summary>Text the files' paths must have in them to be listed; empty lists them all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFilesText))]
    public partial string FileFilter { get; set; } = "";

    public string NoFilesText => FileFilter.Length > 0 ? "No files match the filter." : "No files in this folder.";

    partial void OnFileFilterChanged(string value)
    {
        _tree.Filter = value;
        ShowFiles();
    }

    private void ShowFiles() => Items = _tree.Build(_files);

    [ObservableProperty]
    public partial bool IsListing { get; private set; }

    /// <summary>True when the folder has more files than are listed.</summary>
    [ObservableProperty]
    public partial bool IsCapped { get; private set; }

    /// <summary>The listing in progress, for tests to wait on.</summary>
    public Task Listing { get; private set; } = Task.CompletedTask;

    /// <summary>The read of the installs' service states in progress, for tests to wait on.</summary>
    public Task CheckServices { get; private set; } = Task.CompletedTask;

    /// <summary>The file open in the editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenFile))]
    public partial ConfigFileViewModel? OpenFile { get; private set; }

    public bool HasOpenFile => OpenFile is not null;

    /// <summary>The backups a deploy made of the folder the view is in, newest first, to put one back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackups))]
    public partial IReadOnlyList<BackupViewModel> Backups { get; private set; } = [];

    public bool HasBackups => Backups.Count > 0;

    /// <summary>The latest deploys to the folder the view is in, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory))]
    public partial IReadOnlyList<HistoryEntryViewModel> History { get; private set; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>
    /// The logs of the app in the folder the view is in, or in the nearest folder above it, when it has an nlog.config;
    /// null otherwise. They show in their own tab.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogs))]
    public partial LogsViewModel? Logs { get; private set; }

    public bool HasLogs => Logs is not null;

    public int EditCount => _opened.Values.Sum(file => file.ChangeCount);
    public int EditedFileCount => _opened.Values.Count(file => file.HasChanges);
    public int DeleteCount => _deletes.Count;
    public bool HasPending => EditCount > 0 || DeleteCount > 0;
    public bool HasConflicts => _opened.Values.Any(file => file.HasConflicts);

    /// <summary>A settings file whose text can't be read as settings, which has to be put right or undone before Finalize.</summary>
    public ConfigFileViewModel? FileWithTextError => _opened.Values.FirstOrDefault(file => file.HasTextError);

    /// <summary>What Finalize will do, in a few words, for the bar beside it.</summary>
    public string PendingText => string.Join(", ", new[]
    {
        EditCount > 0 ? Words.Count(EditCount, "change", "changes") : null,
        DeleteCount > 0 ? Words.Count(DeleteCount, "file to delete", "files to delete") : null,
    }.OfType<string>());

    /// <summary>A plain list of what Finalize will do, for the confirmation.</summary>
    public string PendingSummary
    {
        get
        {
            var lines = _opened.Values.Where(file => file.HasChanges)
                .Select(file => $"Write {Words.Count(file.ChangeCount, "change", "changes")} to {Relative(Path.Combine(file.Folder, file.FileName))}")
                .Concat(_deletes.Order(StringComparer.OrdinalIgnoreCase).Select(path => $"Delete {Relative(path)}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Why the last file picked couldn't be opened, such as it not being text; null when it could.</summary>
    [ObservableProperty]
    public partial string? OpenError { get; private set; }

    [RelayCommand]
    private void Open(FolderFileViewModel file)
    {
        OpenError = null;
        if (!_opened.TryGetValue(file.FullPath, out var opened))
        {
            try
            {
                opened = new ConfigFileViewModel(ConfigUpdates.Open(file.FullPath), () => OnEdited(file.FullPath));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.Xml.XmlException)
            {
                OpenError = ex is InvalidDataException ? ex.Message : $"{file.Name} can't be opened: {ex.Message}";
                return;
            }
            _opened[file.FullPath] = opened;
        }
        OpenFile = opened;
    }

    [RelayCommand]
    private void CloseFile() => OpenFile = null;

    [RelayCommand]
    private void ToggleDelete(FolderFileViewModel file)
    {
        if (!_deletes.Remove(file.FullPath))
            _deletes.Add(file.FullPath);
        file.IsStagedForDelete = _deletes.Contains(file.FullPath);
        NotifyPendingChanged();
    }

    /// <summary>Drops every staged change and reads the folder again.</summary>
    [RelayCommand]
    private void Discard()
    {
        _opened.Clear();
        _deletes.Clear();
        OpenFile = null;
        Reload();
    }

    /// <summary>Writes the edited config files and deletes the staged files. The caller confirms first.</summary>
    public void Apply()
    {
        ConfigUpdates.Write(_opened.Values.Where(file => file.HasChanges).Select(file => file.Update));
        foreach (var path in _deletes)
            File.Delete(path);
        Discard();
    }

    /// <summary>Reads the folder again: its installs, and the folders and files of the one the view is in.</summary>
    [RelayCommand]
    public void Reload()
    {
        var current = CurrentFolder?.FullPath;

        var installs = Installations.Find(Root, _appFiles);
        _installsByFolder = installs.ToDictionary(install => Path.GetFullPath(install.Directory), StringComparer.OrdinalIgnoreCase);
        _servicesByFolder = Installations.Services(_installsByFolder.Keys);
        Installs = installs.Select(install => new InstallTarget(install)).ToList();

        _newInstall?.Detach();
        _newInstall = _target is { } target && target.Targets.Any(choice => choice.IsNew) ? new NewInstallRowViewModel(target) : null;

        // Staying in the same folder lists it again, but must not move the deploy's target, which the user chose.
        _reloading = true;
        try
        {
            Navigate(Node(current is not null && Directory.Exists(current) ? current : Root));
        }
        finally
        {
            _reloading = false;
        }
        NotifyPendingChanged();
    }

    [RelayCommand]
    private void GoInto(FolderNodeViewModel folder) => Navigate(folder);

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp() => Navigate(Node(Path.GetDirectoryName(CurrentFolder!.FullPath)!));

    internal FolderNodeViewModel Node(string path) =>
        new(path, IsRoot(path) ? RootName : Path.GetFileName(path),
            _installsByFolder.GetValueOrDefault(Path.GetFullPath(path)), _servicesByFolder.GetValueOrDefault(Path.GetFullPath(path)), _target?.Version);

    /// <summary>
    /// Goes into a folder: lists its folders and its files, and with a package open, makes an install there the
    /// deploy's target.
    /// </summary>
    private void Navigate(FolderNodeViewModel folder)
    {
        CurrentFolder = folder;
        Rows.Clear();
        foreach (var path in Subfolders(folder.FullPath))
            Rows.Add(Node(path));
        if (_newInstall is not null && IsRoot(folder.FullPath))
            Rows.Add(_newInstall);

        if (!_reloading && _target is not null && folder.Target?.Install is { } install
            && _target.Targets.FirstOrDefault(choice => choice.Install?.Directory == install.Directory) is { } choice)
            _target.Selected = choice;

        OpenFile = null;
        OpenError = null;
        FileFilter = "";
        _tree.Filter = "";
        Backups = ReadBackups(folder.FullPath);
        History = _history?.For(folder.FullPath).Take(MaxHistory).Select(record => new HistoryEntryViewModel(record)).ToList() ?? [];
        FindLogs(folder.FullPath);
        Listing = ListFilesAsync(folder.FullPath);
        RefreshServices();
        _serviceTimer.Start();
    }

    /// <summary>
    /// Looks for an nlog.config in the folder and the folders above it, up to the install folder. Going deeper into the
    /// same app, such as into its logs folder, keeps its logs open; reloading the folder reads them again.
    /// </summary>
    private void FindLogs(string folder)
    {
        string? config = null;
        for (var path = folder; config is null && path is not null; path = IsRoot(path) ? null : Path.GetDirectoryName(path))
            config = NLogConfig.Find(path);
        if (config is not null && Logs is { } logs && logs.ConfigPath.Equals(config, StringComparison.OrdinalIgnoreCase))
        {
            if (_reloading)
                logs.RefreshCommand.Execute(null);
            return;
        }
        Logs?.Dispose();
        Logs = config is null ? null : new LogsViewModel(config);
    }

    /// <summary>Reads the services' states again now, such as after starting or stopping them.</summary>
    internal void RefreshServices() => CheckServices = CheckServicesAsync();

    private static IReadOnlyList<BackupViewModel> ReadBackups(string folder)
    {
        try
        {
            return Installations.Backups(folder).Select(backup => new BackupViewModel(backup, folder)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads the states of the services of the installs in view, on a background thread, and shows them beside each
    /// install.
    /// </summary>
    private async Task CheckServicesAsync()
    {
        var installs = Rows.OfType<FolderNodeViewModel>().Append(CurrentFolder).OfType<FolderNodeViewModel>().Where(node => node.IsInstall).ToList();
        if (installs.Count == 0)
            return;
        var names = installs.SelectMany(node => node.ServiceNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IReadOnlyList<InstallService> states = names.Count == 0 ? [] : await Task.Run(() => Installations.States(names));
        var byName = states.ToDictionary(service => service.Name, service => service.Status, StringComparer.OrdinalIgnoreCase);
        foreach (var node in installs)
            node.ShowServices(byName);
    }

    private bool IsRoot(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>Subfolders in name order, without NetDeploy's own state folder. Folders that can't be read are left out.</summary>
    internal static IEnumerable<string> Subfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Where(folder => !Path.GetFileName(folder).Equals(ConfigUpdates.StateFolder, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads the folder's files on a background thread, so a big folder never freezes the window. Picking another
    /// folder cancels a listing that hasn't finished.
    /// </summary>
    private async Task ListFilesAsync(string folder)
    {
        _listing?.Cancel();
        var cancellation = _listing = new CancellationTokenSource();
        IsListing = true;
        try
        {
            var (files, capped) = await Task.Run(() => ReadFiles(folder, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
                return;
            foreach (var file in files)
            {
                file.IsStagedForDelete = _deletes.Contains(file.FullPath);
                file.HasEdits = _opened.TryGetValue(file.FullPath, out var config) && config.HasChanges;
            }
            _files = files;
            IsCapped = capped;
            ShowFiles();
        }
        catch (OperationCanceledException)
        {
            // Another folder was picked; its listing takes over.
        }
        finally
        {
            if (_listing == cancellation)
            {
                _listing = null;
                IsListing = false;
            }
            cancellation.Dispose();
        }
    }

    private static (List<FolderFileViewModel> Files, bool Capped) ReadFiles(string folder, CancellationToken cancellation)
    {
        if (!Directory.Exists(folder))
            return ([], false);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var state = Path.DirectorySeparatorChar + ConfigUpdates.StateFolder + Path.DirectorySeparatorChar;
        var files = new List<FolderFileViewModel>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", options))
        {
            cancellation.ThrowIfCancellationRequested();
            if (path.Contains(state, StringComparison.OrdinalIgnoreCase))
                continue;
            if (files.Count == MaxFiles)
                return (Sort(files), true);
            files.Add(new FolderFileViewModel(folder, path));
        }
        return (Sort(files), false);

        static List<FolderFileViewModel> Sort(List<FolderFileViewModel> files) => files
            .OrderBy(file => file.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The view follows a target chosen elsewhere: to the install, or for a new one such as after Clone, to the
    // install folder, where its row is.
    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InstallViewModel.Selected) || _reloading || _target?.Selected is not { } choice)
            return;
        var folder = choice.Install?.Directory ?? Root;
        if (CurrentFolder is { } current && Path.GetFullPath(current.FullPath).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            return;
        if (choice.Install is null || Directory.Exists(folder))
            Navigate(Node(folder));
    }

    private void OnEdited(string path)
    {
        if (_files.FirstOrDefault(file => file.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)) is { } file)
            file.HasEdits = _opened[path].HasChanges;
        NotifyPendingChanged();
    }

    private void NotifyPendingChanged()
    {
        OnPropertyChanged(nameof(EditCount));
        OnPropertyChanged(nameof(EditedFileCount));
        OnPropertyChanged(nameof(DeleteCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(PendingText));
    }

    private string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public void Dispose()
    {
        _serviceTimer.Stop();
        Logs?.Dispose();
        _listing?.Cancel();
        _newInstall?.Detach();
        if (_target is not null)
            _target.PropertyChanged -= OnTargetChanged;
    }
}
