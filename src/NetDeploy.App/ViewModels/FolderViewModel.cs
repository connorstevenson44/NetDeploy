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
