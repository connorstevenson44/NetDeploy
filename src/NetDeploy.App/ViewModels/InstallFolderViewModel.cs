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

/// <summary>A folder in the install folder, with its install marked. The folder view lists those in the folder it is in.</summary>
public sealed class FolderNodeViewModel
{
    internal FolderNodeViewModel(string path, string name, Installation? install)
    {
        FullPath = path;
        Name = name;
        Target = install is null ? null : new InstallTarget(install);
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
        not null => "",
        _ => IsMissing ? "not created yet" : "",
    };

    public string Kind => IsInstall ? "install" : "";

    public PackIconKind Icon =>
        IsInstall ? PackIconKind.Server : IsMissing ? PackIconKind.FolderPlusOutline : PackIconKind.FolderOutline;
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
    /// <param name="target">With a package open, where its deploy goes; the view goes to it.</param>
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

    /// <summary>What Finalize will do, in a few words, for the bar beside it.</summary>
    public string PendingText => string.Join(", ", new[]
    {
        EditCount > 0 ? Words.Count(EditCount, "setting changed", "settings changed") : null,
        DeleteCount > 0 ? Words.Count(DeleteCount, "file to delete", "files to delete") : null,
    }.OfType<string>());

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

    /// <summary>Reads the folder again: its installs, and the folders and files of the one the view is in.</summary>
    [RelayCommand]
    public void Reload()
    {
        var current = CurrentFolder?.FullPath;

        var installs = Installations.Find(Root, _appFiles);
        _installsByFolder = installs.ToDictionary(install => Path.GetFullPath(install.Directory), StringComparer.OrdinalIgnoreCase);
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
            _installsByFolder.GetValueOrDefault(Path.GetFullPath(path)));

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
        Listing = ListFilesAsync(folder.FullPath);
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
        _listing?.Cancel();
        _newInstall?.Detach();
        if (_target is not null)
            _target.PropertyChanged -= OnTargetChanged;
    }
}
