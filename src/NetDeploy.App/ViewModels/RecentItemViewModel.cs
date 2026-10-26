using MaterialDesignThemes.Wpf;
using NetDeploy.App.Services;

namespace NetDeploy.App.ViewModels;

/// <summary>A row in the start screen's recent list.</summary>
public sealed record RecentItemViewModel(RecentItem Item)
{
    public string Path => Item.Path;
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Item.Path)) is { Length: > 0 } name ? name : Item.Path;

    public PackIconKind Icon => Item.Kind switch
    {
        RecentKind.Folder => PackIconKind.FolderCogOutline,
        RecentKind.Config => PackIconKind.CodeJson,
        _ => PackIconKind.PackageVariant,
    };

    public string KindLabel => Item.Kind switch
    {
        RecentKind.Folder => "Install folder",
        RecentKind.Config => "Deploy config",
        _ => "Package",
    };
}
