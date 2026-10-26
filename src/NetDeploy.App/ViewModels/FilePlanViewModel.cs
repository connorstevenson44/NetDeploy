using CommunityToolkit.Mvvm.ComponentModel;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>The files of one copy step, grouped by folder.</summary>
public sealed partial class FilePlanViewModel : ObservableObject
{
    private readonly List<FileRowViewModel> _rows;
    private readonly FileTree<FileRowViewModel> _tree;
    private bool _showUnchanged;

    public FilePlanViewModel(CopyFilesStep step, string title, FilePlan plan)
    {
        Step = step;
        Title = title;
        Target = plan.Target;
        _tree = new FileTree<FileRowViewModel>(row => row.Folder, row => row.Path, () => Items = BuildItems());
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

    /// <summary>Config files merged with the server's copy, which ReplaceCount includes.</summary>
    public int MergeCount => _rows.Count(row => row.State != FileState.New && row.Action == FileAction.Merge);
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

    /// <summary>Lists only the files whose path has the text in it.</summary>
    public void Filter(string text)
    {
        _tree.Filter = text;
        Items = BuildItems();
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(ReplaceCount));
        OnPropertyChanged(nameof(DeleteCount));
        OnPropertyChanged(nameof(KeepCount));
    }

    private List<object> BuildItems() => _tree.Build(_rows.Where(row => _showUnchanged || row.IsNotable));
}
