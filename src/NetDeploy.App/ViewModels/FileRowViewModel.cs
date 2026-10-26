using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

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
    public Thickness IconMargin => FileTree.IconMargin(Depth);
    public FileState State => _entry.State;
    public FileAction DefaultAction => _entry.DefaultAction;
    public IReadOnlyList<FileActionOption> Options { get; }

    /// <summary>What happens to the file, in a word, beside the action buttons.</summary>
    public string ActionLabel => (_entry.Action, _entry.State) switch
    {
        (FileAction.Replace or FileAction.Merge, FileState.New) => "Add",
        (FileAction.Replace, _) => "Replace",
        (FileAction.Merge, _) => "Merge",
        (FileAction.Delete, _) => "Delete",
        (FileAction.Keep, FileState.New) => "Skip",
        _ => "Keep",
    };

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
