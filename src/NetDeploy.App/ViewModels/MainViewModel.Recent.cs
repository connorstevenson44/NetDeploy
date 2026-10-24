using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using NetDeploy.App.Services;
using NetDeploy.Core;

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

/// <summary>
/// What the person opened lately, on the start screen, and the values they last deployed each app with, filled in
/// again when it opens. Both are kept in their own settings (see UserSettings), never next to the app.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>What was opened lately and is still there, newest first.</summary>
    public ObservableCollection<RecentItemViewModel> Recent { get; } = [];

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenRecent(RecentItemViewModel recent)
    {
        if (!File.Exists(recent.Path) && !Directory.Exists(recent.Path))
        {
            _settings.RemoveRecent(recent.Path);
            RefreshRecent();
            Status = $"{recent.Path} isn't there any more, so it was taken off the list.";
            return;
        }
        switch (recent.Item.Kind)
        {
            case RecentKind.Folder:
                ShowFolder(recent.Path);
                break;
            case RecentKind.Config:
                EditConfig(recent.Path);
                break;
            default:
                Load(recent.Path);
                break;
        }
    }

    private void Remember(string path, RecentKind kind)
    {
        _settings.AddRecent(path, kind);
        RefreshRecent();
    }

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (var item in _settings.Recent.Where(item => File.Exists(item.Path) || Directory.Exists(item.Path)))
            Recent.Add(new RecentItemViewModel(item));
    }

    /// <summary>The app's settings as the person last deployed them, without secrets. The install folder is the key.</summary>
    private void RememberValues()
    {
        if (_package is null)
            return;
        var values = Parameters.Append(FolderField).OfType<InputFieldViewModel>()
            .Where(parameter => parameter.Type != InputType.Secret)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        _settings.SaveValues(_package.Manifest.App.Name, _installFolder ?? "", values);
    }

    /// <summary>
    /// Fills the settings an app was last deployed with into this install folder. Secrets are never saved, and the
    /// install folder itself is set before, from the last one used.
    /// </summary>
    private void RestoreValues(string folder)
    {
        if (_package is null)
            return;
        var saved = _settings.Values(_package.Manifest.App.Name, folder);
        foreach (var parameter in Parameters.Where(parameter => parameter.Type != InputType.Secret))
            if (saved.TryGetValue(parameter.Name, out var value))
                parameter.Value = value;
    }

    /// <summary>The install folder the app was last deployed to, when it is a parameter of its own.</summary>
    private void RestoreFolder()
    {
        if (FolderField is not null && _package is not null && _settings.LastFolder(_package.Manifest.App.Name) is { } last)
            FolderField.Value = last;
    }
}
