using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.App.Services;
using NetDeploy.Core.Installs;
using NetDeploy.Core.Manifest;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// What the person opened lately, on the start screen, and the values they last deployed each app with, filled in
/// again when it opens. Both are kept in their own settings (see UserSettings), never next to the app.
/// </summary>
public sealed partial class MainViewModel
{
    // The install whose remembered settings were last filled in, so picking it again doesn't overwrite what was typed.
    private string? _valuesFrom;

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

    /// <summary>
    /// The app's settings as the person last deployed them, without secrets, for the install folder and for the
    /// install they went to.
    /// </summary>
    private void RememberValues()
    {
        if (_package is null)
            return;
        var values = Parameters.Append(FolderField).OfType<InputFieldViewModel>()
            .Where(parameter => parameter.Type != InputType.Secret)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        var install = Install is not null && _installFolder is not null ? Installations.Variables(_installFolder, Install.Name)["InstanceDir"] : null;
        _settings.SaveValues(_package.Manifest.App.Name, _installFolder ?? "", values, install);
        _valuesFrom = install;
    }

    /// <summary>
    /// Fills the settings an app was last deployed with into this install folder, or install. Secrets are never saved,
    /// a value that no longer fits its setting, such as a choice the package dropped, is left out, and so are the
    /// settings in skip.
    /// </summary>
    private void RestoreValues(string folder, IReadOnlyCollection<string>? skip = null)
    {
        if (_package is null)
            return;
        var saved = _settings.Values(_package.Manifest.App.Name, folder);
        foreach (var parameter in Parameters.Where(parameter => parameter.Type != InputType.Secret && skip?.Contains(parameter.Name) != true))
            if (saved.TryGetValue(parameter.Name, out var value) && parameter.Field.Problem(parameter.Name, value) is null)
                parameter.Value = value;
    }

    /// <summary>
    /// Fills the settings the install picked as the deploy's target was last deployed with, as installs side by side
    /// are often set up differently; for a new install, those of the install it starts from a copy of. Settings that
    /// pick the install folder stay as they are. Each install's are filled once, so what was typed since stays.
    /// </summary>
    private void RestoreInstallValues()
    {
        var source = Install?.Selected?.Install ?? Install?.CopyFrom?.Install;
        if (source is null || _package?.Manifest.Install is not { } install
            || string.Equals(source.Directory, _valuesFrom, StringComparison.OrdinalIgnoreCase))
            return;
        _valuesFrom = source.Directory;
        RestoreValues(source.Directory, Placeholders.Unresolved(install.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The install folder the app was last deployed to, when it is a parameter of its own.</summary>
    private void RestoreFolder()
    {
        if (FolderField is not null && _package is not null && _settings.LastFolder(_package.Manifest.App.Name) is { } last)
            FolderField.Value = last;
    }
}
