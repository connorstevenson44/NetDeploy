using System.Windows;

namespace NetDeploy.App.ViewModels;

/// <summary>A folder heading in a file list.</summary>
public sealed record FolderViewModel(string Name, int Depth)
{
    public Thickness Indent => new(Depth * 16, 0, 0, 0);
}

public static class FolderHeadings
{
    /// <summary>
    /// Files in path order with a heading wherever a folder starts, so a flat list reads like a tree. A folder's
    /// heading appears once, before its first file, and nested folders get a heading for each level.
    /// </summary>
    public static List<object> Interleave<T>(IEnumerable<T> files, Func<T, string> folderOf)
    {
        var items = new List<object>();
        var open = new List<string>();
        foreach (var file in files)
        {
            var folder = folderOf(file);
            var folders = folder.Length == 0 ? [] : folder.Split('/');
            var common = 0;
            while (common < open.Count && common < folders.Length && open[common].Equals(folders[common], StringComparison.OrdinalIgnoreCase))
                common++;
            open.RemoveRange(common, open.Count - common);
            for (var depth = common; depth < folders.Length; depth++)
            {
                items.Add(new FolderViewModel(folders[depth], depth));
                open.Add(folders[depth]);
            }
            items.Add(file!);
        }
        return items;
    }
}
