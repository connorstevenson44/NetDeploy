using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

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
