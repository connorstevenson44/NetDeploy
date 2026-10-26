using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Config;
using NetDeploy.Core.Processes;

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
        CanEdit = !BinaryExtensions.Contains(Path.GetExtension(Name));
    }

    // Files that are never text, so they get no Edit button. Anything else is checked when it is opened.
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".msi", ".zip", ".7z", ".gz", ".rar", ".nupkg", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico",
        ".pdf", ".db", ".sqlite", ".mdf", ".ldf", ".bak", ".woff", ".woff2", ".ttf", ".otf", ".eot", ".so", ".bin", ".dat",
    };

    public string FullPath { get; }

    /// <summary>The path from the folder being listed, with forward slashes.</summary>
    public string RelativePath { get; }

    public string Folder { get; }
    public string Name { get; }
    public bool IsConfig { get; }
    public bool CanRun { get; }

    /// <summary>True for a file that may be text, which opens in the editor.</summary>
    public bool CanEdit { get; }
    public Thickness IconMargin => FileTree.IconMargin(Folder.Length == 0 ? 0 : Folder.Count(c => c == '/') + 1);

    public PackIconKind Icon => IsConfig ? PackIconKind.FileCogOutline
        : CanRun ? PackIconKind.ApplicationCogOutline
        : PackIconKind.FileOutline;

    /// <summary>Marked for deletion when the changes are finalized.</summary>
    [ObservableProperty]
    public partial bool IsStagedForDelete { get; set; }

    /// <summary>A file with changes not yet written.</summary>
    [ObservableProperty]
    public partial bool HasEdits { get; set; }
}
