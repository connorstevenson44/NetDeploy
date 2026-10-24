using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NetDeploy.App.ViewModels;

/// <summary>A folder in a file list, which folds away the files and folders in it, as in File Explorer's tree.</summary>
public sealed partial class FolderViewModel(string path, int depth, bool isExpanded, Action<FolderViewModel> toggle) : ObservableObject
{
    /// <summary>The folder's path from the top of the list, with forward slashes.</summary>
    public string Path { get; } = path;

    public string Name { get; } = path[(path.LastIndexOf('/') + 1)..];
    public int Depth { get; } = depth;
    public Thickness Indent => FileTree.Indent(Depth);
    public bool IsExpanded { get; } = isExpanded;

    [RelayCommand]
    private void Toggle() => toggle(this);
}

public static class FileTree
{
    /// <summary>How far a row at this depth is indented, so each level sits under the folder it is in.</summary>
    public static Thickness Indent(int depth) => new(8 + depth * 16, 0, 0, 0);

    /// <summary>Where a file's icon goes: in line with a folder's icon at the same depth, after its fold arrow.</summary>
    public static Thickness IconMargin(int depth) => new(8 + depth * 16 + 18, 0, 8, 0);
}

/// <summary>
/// Lays files out as a tree in a flat list: each folder's heading, then what is in it, so a long list still only builds
/// the rows on screen. Folders can be folded, and a filter keeps the files whose path has the text in it.
/// </summary>
/// <param name="folderOf">A file's folder from the top of the list, with forward slashes; "" at the top.</param>
/// <param name="pathOf">A file's path from the top of the list, which the filter looks in.</param>
/// <param name="changed">Called when a folder is folded or opened, so the owner lays the list out again.</param>
public sealed class FileTree<T>(Func<T, string> folderOf, Func<T, string> pathOf, Action changed) where T : notnull
{
    private readonly HashSet<string> _folded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Text the files' paths must have in them; empty shows every file. A new filter opens every folder.</summary>
    public string Filter
    {
        get;
        set
        {
            field = value;
            _folded.Clear();
        }
    } = "";

    /// <summary>
    /// Files in path order with a heading wherever a folder starts. A folder's heading appears once, before what is in
    /// it, and nested folders get a heading for each level. What is in a folded folder is left out.
    /// </summary>
    public List<object> Build(IEnumerable<T> files)
    {
        var filter = Filter.Trim();
        var items = new List<object>();
        var open = new List<FolderViewModel>();
        foreach (var file in files)
        {
            if (filter.Length > 0 && !pathOf(file).Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var folder = folderOf(file);
            var names = folder.Length == 0 ? [] : folder.Split('/');
            var common = 0;
            while (common < open.Count && common < names.Length && open[common].Name.Equals(names[common], StringComparison.OrdinalIgnoreCase))
                common++;
            open.RemoveRange(common, open.Count - common);
            for (var depth = common; depth < names.Length; depth++)
            {
                var path = string.Join('/', names[..(depth + 1)]);
                var heading = new FolderViewModel(path, depth, !_folded.Contains(path), Toggle);
                if (open.All(parent => parent.IsExpanded))
                    items.Add(heading);
                open.Add(heading);
            }
            if (open.All(parent => parent.IsExpanded))
                items.Add(file);
        }
        return items;
    }

    private void Toggle(FolderViewModel folder)
    {
        if (!_folded.Remove(folder.Path))
            _folded.Add(folder.Path);
        changed();
    }
}
