using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>A copy step whose files can't be listed yet, with the reason.</summary>
public sealed record FilePlanError(string Title, string Message);

/// <summary>
/// One of the actions a file can take, shown as an icon button. Every row has all four in the same order, with the
/// ones that don't apply hidden, so each action lines up in its own column.
/// </summary>
public sealed partial class FileActionOption(FileRowViewModel row, FileAction action, bool isAvailable) : ObservableObject
{
    public FileAction Action => action;
    public bool IsAvailable => isAvailable;
    public bool IsSelected => row.Action == action;

    public PackIconKind Icon => action switch
    {
        FileAction.Replace => row.State == FileState.New ? PackIconKind.FilePlusOutline : PackIconKind.FileReplaceOutline,
        // A config the server doesn't have yet is added, not merged; it still goes through the config check.
        FileAction.Merge => row.State == FileState.New ? PackIconKind.FilePlusOutline : PackIconKind.SourceMerge,
        FileAction.Delete => PackIconKind.TrashCanOutline,
        _ => PackIconKind.Lock,
    };

    public string Tip => (action, row.State) switch
    {
        (FileAction.Replace, FileState.New) => "Add the package's file",
        (FileAction.Replace, _) => "Replace with the package's file",
        (FileAction.Merge, FileState.New) => "Add the package's file; you check its settings before it is written",
        (FileAction.Merge, _) => "Merge with the server's copy; you check it before it is written",
        (FileAction.Delete, _) => "Delete from the server",
        (FileAction.Keep, FileState.New) => "Don't add it",
        _ => "Keep the server's file as it is",
    };

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private void Choose() => row.Action = action;

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>One file: how it compares with the server and what the deploy will do with it.</summary>
public sealed partial class FileRowViewModel : ObservableObject
{
    private static readonly FileAction[] ColumnOrder = [FileAction.Merge, FileAction.Replace, FileAction.Keep, FileAction.Delete];

    private readonly FilePlanEntry _entry;
    private readonly Action _changed;

    public FileRowViewModel(FilePlanEntry entry, Action changed)
    {
        _entry = entry;
        _changed = changed;
        var slash = entry.Path.LastIndexOf('/');
        Folder = slash < 0 ? "" : entry.Path[..slash];
        Name = entry.Path[(slash + 1)..];
        Options = ColumnOrder.Select(action => new FileActionOption(this, action, entry.Choices.Contains(action))).ToList();
    }

    public string Path => _entry.Path;
    public string Folder { get; }
    public string Name { get; }
    public int Depth => Folder.Length == 0 ? 0 : Folder.Count(c => c == '/') + 1;
    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public FileState State => _entry.State;
    public FileAction DefaultAction => _entry.DefaultAction;
    public IReadOnlyList<FileActionOption> Options { get; }

    public string StateTip => _entry.State switch
    {
        FileState.New => "Only in the package",
        FileState.Changed => "Different in the package",
        FileState.Same => "Identical in the package and on the server",
        _ => "Only on the server",
    };

    /// <summary>Identical files that stay as they are are hidden unless asked for.</summary>
    public bool IsNotable => _entry.State != FileState.Same || _entry.Action != FileAction.Keep;

    /// <summary>True when the user picked something other than the default.</summary>
    public bool IsChosen => _entry.Action != _entry.DefaultAction;

    public FileAction Action
    {
        get => _entry.Action;
        set
        {
            if (_entry.Action == value)
                return;
            _entry.Action = value;
            OnPropertyChanged(string.Empty);
            foreach (var option in Options)
                option.Refresh();
            _changed();
        }
    }
}

/// <summary>The files of one copy step, grouped by folder.</summary>
public sealed partial class FilePlanViewModel : ObservableObject
{
    private readonly List<FileRowViewModel> _rows;
    private bool _showUnchanged;

    public FilePlanViewModel(CopyFilesStep step, string title, FilePlan plan)
    {
        Step = step;
        Title = title;
        Target = plan.Target;
        _rows = plan.Entries
            .Select(entry => new FileRowViewModel(entry, OnCountsChanged))
            .OrderBy(row => row.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Items = BuildItems();
    }

    public CopyFilesStep Step { get; }
    public string Title { get; }
    public string Target { get; }

    public int NewCount => _rows.Count(row => row.State == FileState.New && row.Action != FileAction.Keep);
    public int ReplaceCount => _rows.Count(row => row.State != FileState.New && row.Action is FileAction.Replace or FileAction.Merge);
    public int DeleteCount => _rows.Count(row => row.Action == FileAction.Delete);
    public int KeepCount => _rows.Count(row => row.Action == FileAction.Keep);

    /// <summary>Folder headings and files, in path order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    public partial IReadOnlyList<object> Items { get; private set; }

    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// The actions the user changed, by relative path. Defaults are left to the deploy, which plans again when it
    /// runs, so a file that changed since the view was built still gets the right default.
    /// </summary>
    public IReadOnlyDictionary<string, FileAction> ChosenActions => _rows
        .Where(row => row.Action != row.DefaultAction)
        .ToDictionary(row => row.Path, row => row.Action, StringComparer.OrdinalIgnoreCase);

    public void ShowUnchanged(bool show)
    {
        _showUnchanged = show;
        Items = BuildItems();
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(ReplaceCount));
        OnPropertyChanged(nameof(DeleteCount));
        OnPropertyChanged(nameof(KeepCount));
    }

    private List<object> BuildItems() =>
        FolderHeadings.Interleave(_rows.Where(row => _showUnchanged || row.IsNotable), row => row.Folder);
}
