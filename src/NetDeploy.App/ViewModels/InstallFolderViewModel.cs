using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core;
using NetDeploy.Core.Config;

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
    }

    public string FullPath { get; }

    /// <summary>The path from the folder being listed, with forward slashes.</summary>
    public string RelativePath { get; }

    public string Folder { get; }
    public string Name { get; }
    public bool IsConfig { get; }
    public bool CanRun { get; }
    public Thickness Indent => new((Folder.Length == 0 ? 0 : Folder.Count(c => c == '/') + 1) * 16, 0, 0, 0);

    public PackIconKind Icon => IsConfig ? PackIconKind.FileCogOutline
        : CanRun ? PackIconKind.ApplicationCogOutline
        : PackIconKind.FileOutline;

    /// <summary>Marked for deletion when the changes are finalized.</summary>
    [ObservableProperty]
    public partial bool IsStagedForDelete { get; set; }

    /// <summary>A config file with settings changed but not yet written.</summary>
    [ObservableProperty]
    public partial bool HasEdits { get; set; }
}

/// <summary>Shows a folder's expander before its subfolders are read.</summary>
public sealed class LoadingNode
{
    public static readonly LoadingNode Instance = new();

    private LoadingNode()
    {
    }
}

/// <summary>A folder in the install folder's tree. Installs are marked, and subfolders are read when it is first opened.</summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    private readonly InstallFolderViewModel _owner;
    private bool _loaded;

    internal FolderNodeViewModel(InstallFolderViewModel owner, string path, string name, Installation? install)
    {
        _owner = owner;
        FullPath = path;
        Name = name;
        Target = install is null ? null : new InstallTarget(install);
        if (InstallFolderViewModel.HasSubfolders(path))
            Children.Add(LoadingNode.Instance);
    }

    public string FullPath { get; }
    public string Name { get; }

    /// <summary>The install this folder holds, for the remove and clone buttons; null for a plain folder.</summary>
    public InstallTarget? Target { get; }

    public bool IsInstall => Target is not null;

    /// <summary>The root of a new install whose folder isn't there yet.</summary>
    public bool IsMissing => !Directory.Exists(FullPath);

    public string Detail => Target?.Install switch
    {
        { Version: { } version } => "v" + version,
        not null => "unknown version",
        _ => IsMissing ? "new folder" : "",
    };

    public string Kind => Target?.Install switch
    {
        { IsInstance: true } => "instance",
        not null => "single install",
        _ => "",
    };

    public PackIconKind Icon => Target?.Install switch
    {
        { IsInstance: true } => PackIconKind.ServerNetwork,
        not null => PackIconKind.Server,
        _ => IsMissing ? PackIconKind.FolderPlusOutline : PackIconKind.FolderOutline,
    };

    /// <summary>Subfolders, a LoadingNode until they are read, and for the root, the row that adds an install.</summary>
    public ObservableCollection<object> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            Load();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _owner.Select(this);
    }

    internal void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        Children.Remove(LoadingNode.Instance);
        foreach (var folder in InstallFolderViewModel.Subfolders(FullPath))
            Children.Insert(Children.Count(child => child is FolderNodeViewModel), _owner.Node(folder));
    }

    /// <summary>This folder and every subfolder read so far.</summary>
    internal IEnumerable<FolderNodeViewModel> Loaded() =>
        Children.OfType<FolderNodeViewModel>().SelectMany(child => child.Loaded()).Prepend(this);
}

/// <summary>
/// The last row of the tree: a new install, named in place, so the installs already there stay in view while it is
/// set up. It is the deploy's target while it is selected or being named.
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

    /// <summary>A package without instances only installs once, at the top of the folder, so there is no name.</summary>
    public bool CanName => _install.AllowsInstances;

    public string Label => CanName ? "New install" : "Install here";

    public string Hint => _install.Installs.Any() ? "Name the new instance" : "Name, or leave empty for a single install";

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

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !IsTarget)
        {
            Choose();
            _install.Name = _draft;
        }
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
/// An install folder: a tree of its folders with the installs marked, the files of the folder picked in the tree,
/// and changes staged on them. Settings edits and deletes are only written when the user finalizes, all together.
/// With a package open, the tree also picks where the deploy goes: an install in it, or a new one.
/// </summary>
public sealed partial class InstallFolderViewModel : ObservableObject, IDisposable
{
    /// <summary>The most files listed for one folder, so picking the top of a drive by mistake stays quick.</summary>
    internal const int MaxFiles = 5000;

    private readonly Dictionary<string, ConfigFileViewModel> _opened = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deletes = new(StringComparer.OrdinalIgnoreCase);
    private readonly InstallViewModel? _target;
    private readonly IReadOnlyCollection<string> _appFiles;
    private Dictionary<string, Installation> _installsByFolder = [];
    private List<FolderFileViewModel> _files = [];
    private CancellationTokenSource? _listing;
    private NewInstallRowViewModel? _newInstall;
    private bool _reloading;

    /// <param name="path">The install folder.</param>
    /// <param name="target">With a package open, where its deploy goes; the tree selects it.</param>
    /// <param name="appFiles">The package's app files, which tell its installs apart from other folders.</param>
    public InstallFolderViewModel(string path, InstallViewModel? target = null, IReadOnlyCollection<string>? appFiles = null)
    {
        Root = Path.GetFullPath(path);
        _target = target;
        _appFiles = appFiles ?? [];
        if (target is not null)
            target.PropertyChanged += OnTargetChanged;
        Reload();
    }

    public string Root { get; }

    /// <summary>The tree's one top node: the install folder.</summary>
    public ObservableCollection<FolderNodeViewModel> Tree { get; } = [];

    /// <summary>Installs found in the folder from its layout.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InstallTarget> Installs { get; private set; } = [];

    /// <summary>The folder whose files are listed.</summary>
    [ObservableProperty]
    public partial FolderNodeViewModel? SelectedFolder { get; private set; }

    /// <summary>Folder headings and files, in path order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    public partial IReadOnlyList<object> Items { get; private set; } = [];

    public bool HasFiles => Items.Count > 0;

    [ObservableProperty]
    public partial bool IsListing { get; private set; }

    /// <summary>True when the folder has more files than are listed.</summary>
    [ObservableProperty]
    public partial bool IsCapped { get; private set; }

    /// <summary>The listing in progress, for tests to wait on.</summary>
    public Task Listing { get; private set; } = Task.CompletedTask;

    /// <summary>The config file open in the side-by-side editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenFile))]
    public partial ConfigFileViewModel? OpenFile { get; private set; }

    public bool HasOpenFile => OpenFile is not null;

    public int EditCount => _opened.Values.Sum(file => file.ChangeCount);
    public int EditedFileCount => _opened.Values.Count(file => file.HasChanges);
    public int DeleteCount => _deletes.Count;
    public bool HasPending => EditCount > 0 || DeleteCount > 0;
    public bool HasConflicts => _opened.Values.Any(file => file.HasConflicts);

    /// <summary>A plain list of what Finalize will do, for the confirmation.</summary>
    public string PendingSummary
    {
        get
        {
            var lines = _opened.Values.Where(file => file.HasChanges)
                .Select(file => $"Write {file.ChangeCount} changed setting(s) to {Relative(Path.Combine(file.Folder, file.FileName))}")
                .Concat(_deletes.Order(StringComparer.OrdinalIgnoreCase).Select(path => $"Delete {Relative(path)}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    [RelayCommand]
    private void Open(FolderFileViewModel file)
    {
        if (!file.IsConfig)
            return;
        if (!_opened.TryGetValue(file.FullPath, out var config))
        {
            config = new ConfigFileViewModel(ConfigUpdates.Open(file.FullPath), () => OnEdited(file.FullPath));
            config.ShowUnchanged(true);
            _opened[file.FullPath] = config;
        }
        OpenFile = config;
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

    /// <summary>Reads the folder again: its installs, its tree, and the files of the folder picked in it.</summary>
    [RelayCommand]
    public void Reload()
    {
        var expanded = Tree.SelectMany(node => node.Loaded()).Where(node => node.IsExpanded).Select(node => node.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = SelectedFolder?.FullPath;

        var installs = Installations.Find(Root, _appFiles);
        _installsByFolder = installs.ToDictionary(install => Path.GetFullPath(install.Directory), StringComparer.OrdinalIgnoreCase);
        Installs = installs.Select(install => new InstallTarget(install)).ToList();

        _newInstall?.Detach();
        _newInstall = _target is { } target && target.Targets.Any(choice => choice.IsNew) ? new NewInstallRowViewModel(target) : null;
        var root = new FolderNodeViewModel(this, Root, Root.TrimEnd(Path.DirectorySeparatorChar), _installsByFolder.GetValueOrDefault(Root));
        root.IsExpanded = true;
        if (_newInstall is not null)
            root.Children.Add(_newInstall);
        Tree.Clear();
        Tree.Add(root);

        // Open the folders that were open, top down, so their children are there to open in turn.
        foreach (var node in root.Loaded().ToList())
            ReopenUnder(node, expanded);
        // Picking the folder again lists it, but must not move the deploy's target, which the user chose.
        var keep = root.Loaded().FirstOrDefault(node => node.FullPath.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? root;
        SelectedFolder = null;
        _reloading = true;
        try
        {
            keep.IsSelected = true;
        }
        finally
        {
            _reloading = false;
        }
        NotifyPendingChanged();
    }

    private static void ReopenUnder(FolderNodeViewModel node, HashSet<string> expanded)
    {
        foreach (var child in node.Children.OfType<FolderNodeViewModel>().ToList())
        {
            if (!expanded.Contains(child.FullPath))
                continue;
            child.IsExpanded = true;
            ReopenUnder(child, expanded);
        }
    }

    internal FolderNodeViewModel Node(string path) =>
        new(this, path, Path.GetFileName(path), _installsByFolder.GetValueOrDefault(Path.GetFullPath(path)));

    /// <summary>Lists the folder picked in the tree, and with a package open, makes an install picked there the deploy's target.</summary>
    internal void Select(FolderNodeViewModel node)
    {
        foreach (var other in Tree.SelectMany(root => root.Loaded()).Where(other => other != node))
            other.IsSelected = false;
        if (_newInstall is not null)
            _newInstall.IsSelected = false;

        if (!_reloading && _target is not null && node.Target?.Install is { } install
            && _target.Targets.FirstOrDefault(choice => choice.Install?.Directory == install.Directory) is { } choice)
            _target.Selected = choice;

        if (SelectedFolder == node)
            return;
        SelectedFolder = node;
        OpenFile = null;
        Listing = ListFilesAsync(node.FullPath);
    }

    internal static bool HasSubfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Any(folder => !Path.GetFileName(folder).Equals(ConfigUpdates.StateFolder, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

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
            Items = FolderHeadings.Interleave(_files, file => file.Folder);
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

    // The tree's pick follows a target chosen elsewhere, such as the new row after Clone.
    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InstallViewModel.Selected) || _reloading || _target?.Selected is not { } choice)
            return;
        if (choice.Install is not { } install)
        {
            if (_newInstall is not null)
                _newInstall.IsSelected = true;
            return;
        }
        var node = Tree.SelectMany(root => root.Loaded()).FirstOrDefault(node => node.Target?.Install?.Directory == install.Directory);
        if (node is not null)
            node.IsSelected = true;
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
    }

    private string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    public void Dispose()
    {
        _listing?.Cancel();
        _newInstall?.Detach();
        if (_target is not null)
            _target.PropertyChanged -= OnTargetChanged;
    }
}
