using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core;
using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

/// <summary>One file in the folder view, with what can be done to it.</summary>
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

/// <summary>
/// An install folder opened without a package: its installs, its files, and the changes staged on them. Settings
/// edits and deletes are only written when the user finalizes, all together.
/// </summary>
public sealed partial class InstallFolderViewModel : ObservableObject
{
    private readonly Dictionary<string, ConfigFileViewModel> _opened = new(StringComparer.OrdinalIgnoreCase);
    private List<FolderFileViewModel> _files = [];

    public InstallFolderViewModel(string path)
    {
        Root = Path.GetFullPath(path);
        Reload();
    }

    public string Root { get; }

    /// <summary>Installs found in the folder from its layout.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<InstallTarget> Installs { get; private set; } = [];

    /// <summary>Folder headings and files, in path order.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> Items { get; private set; } = [];

    /// <summary>The config file open in the side-by-side editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenFile))]
    public partial ConfigFileViewModel? OpenFile { get; private set; }

    public bool HasOpenFile => OpenFile is not null;

    public int EditCount => _opened.Values.Sum(file => file.ChangeCount);
    public int EditedFileCount => _opened.Values.Count(file => file.HasChanges);
    public int DeleteCount => _files.Count(file => file.IsStagedForDelete);
    public bool HasPending => EditCount > 0 || DeleteCount > 0;
    public bool HasConflicts => _opened.Values.Any(file => file.HasConflicts);

    /// <summary>A plain list of what Finalize will do, for the confirmation.</summary>
    public string PendingSummary
    {
        get
        {
            var lines = _opened.Values.Where(file => file.HasChanges)
                .Select(file => $"Write {file.ChangeCount} changed setting(s) to {Path.GetRelativePath(Root, Path.Combine(file.Folder, file.FileName))}")
                .Concat(_files.Where(file => file.IsStagedForDelete).Select(file => $"Delete {file.RelativePath}"));
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
            config = new ConfigFileViewModel(ConfigUpdates.Open(file.FullPath), () => OnEdited(file));
            config.ShowUnchanged(true);
            _opened[file.FullPath] = config;
        }
        OpenFile = config;
    }

    [RelayCommand]
    private void ToggleDelete(FolderFileViewModel file)
    {
        file.IsStagedForDelete = !file.IsStagedForDelete;
        NotifyPendingChanged();
    }

    /// <summary>Drops every staged change and reads the folder again.</summary>
    [RelayCommand]
    private void Discard()
    {
        _opened.Clear();
        OpenFile = null;
        foreach (var file in _files)
            file.IsStagedForDelete = false;
        Reload();
    }

    /// <summary>Writes the edited config files and deletes the staged files. The caller confirms first.</summary>
    public void Apply()
    {
        ConfigUpdates.Write(_opened.Values.Where(file => file.HasChanges).Select(file => file.Update));
        foreach (var file in _files.Where(file => file.IsStagedForDelete))
            File.Delete(file.FullPath);
        Discard();
    }

    /// <summary>Lists the folder again, keeping staged deletes of files that are still there.</summary>
    [RelayCommand]
    public void Reload()
    {
        var staged = _files.Where(file => file.IsStagedForDelete).Select(file => file.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Installs = Installations.Find(Root, []).Select(install => new InstallTarget(install)).ToList();
        _files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => new FolderFileViewModel(Root, path))
            .Where(file => !file.RelativePath.StartsWith(ConfigUpdates.StateFolder + "/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in _files)
        {
            file.IsStagedForDelete = staged.Contains(file.FullPath);
            file.HasEdits = _opened.TryGetValue(file.FullPath, out var config) && config.HasChanges;
        }
        Items = BuildItems();
        NotifyPendingChanged();
    }

    private void OnEdited(FolderFileViewModel file)
    {
        file.HasEdits = _opened[file.FullPath].HasChanges;
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

    private List<object> BuildItems()
    {
        var items = new List<object>();
        var open = new List<string>();
        foreach (var file in _files)
        {
            var folders = file.Folder.Length == 0 ? [] : file.Folder.Split('/');
            var common = 0;
            while (common < open.Count && common < folders.Length && open[common].Equals(folders[common], StringComparison.OrdinalIgnoreCase))
                common++;
            open.RemoveRange(common, open.Count - common);
            for (var depth = common; depth < folders.Length; depth++)
            {
                items.Add(new FolderViewModel(folders[depth], depth));
                open.Add(folders[depth]);
            }
            items.Add(file);
        }
        return items;
    }
}
