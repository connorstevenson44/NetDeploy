using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// The last row in the install folder itself: a new install, named in place, so the installs already there stay in
/// view while it is set up. It is the deploy's target once it is picked or named.
/// </summary>
public sealed partial class NewInstallRowViewModel : ObservableObject
{
    private readonly InstallViewModel _install;
    private string _draft = "";

    internal NewInstallRowViewModel(InstallViewModel install)
    {
        _install = install;
        install.PropertyChanged += OnInstallChanged;
    }

    public bool IsTarget => _install.Selected?.IsNew == true;

    public string Name
    {
        get => IsTarget ? _install.Name : _draft;
        set
        {
            _draft = value;
            Choose();
            _install.Name = value;
        }
    }

    /// <summary>Makes the new install the deploy's target, with the name typed before.</summary>
    [RelayCommand]
    private void Pick()
    {
        if (IsTarget)
            return;
        Choose();
        _install.Name = _draft;
    }

    private void Choose()
    {
        if (!IsTarget && _install.Targets.FirstOrDefault(target => target.IsNew) is { } target)
            _install.Selected = target;
    }

    private void OnInstallChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallViewModel.Selected) or nameof(InstallViewModel.Name))
        {
            OnPropertyChanged(nameof(IsTarget));
            OnPropertyChanged(nameof(Name));
        }
    }

    internal void Detach() => _install.PropertyChanged -= OnInstallChanged;
}
