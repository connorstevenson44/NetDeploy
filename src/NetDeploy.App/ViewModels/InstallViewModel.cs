using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Installs;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// Picks where the deploy goes when the manifest has an install block: update an install that is there, add a new
/// one, or rename one. Every install is named and has its own folder inside the install folder.
/// </summary>
public sealed partial class InstallViewModel(string version, IReadOnlyCollection<string> appFiles) : ObservableObject
{
    private string _folder = "";
    private bool _refreshing;

    /// <summary>The package's version, which the installs are compared with.</summary>
    public string Version => version;

    public ObservableCollection<InstallTarget> Targets { get; } = [];

    /// <summary>What a new install can start from: nothing, or a copy of an install.</summary>
    public ObservableCollection<InstallTarget> CopySources { get; } = [];

    public IEnumerable<Installation> Installs => Targets.Select(target => target.Install).OfType<Installation>();

    [ObservableProperty]
    public partial InstallTarget? Selected { get; set; }

    /// <summary>The install's name, which is also its folder's.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>For a new install: an install to copy, so its config edits and data come along.</summary>
    [ObservableProperty]
    public partial InstallTarget? CopyFrom { get; set; }

    public bool CanCopy => Selected?.IsNew == true && Installs.Any();

    /// <summary>An install picked to update can be given another name, which moves it to a new folder.</summary>
    public bool CanRename => Selected is { IsNew: false };

    public InstallPlan Plan => Selected?.Install switch
    {
        null => InstallPlan.New,
        { } existing when existing.Name.Equals(Name, StringComparison.OrdinalIgnoreCase) => InstallPlan.Update,
        _ => InstallPlan.Rename,
    };

    public PackIconKind PlanIcon => Plan switch
    {
        InstallPlan.Update => PackIconKind.Sync,
        InstallPlan.Rename => PackIconKind.RenameOutline,
        _ => PackIconKind.PlusCircleOutline,
    };

    /// <summary>The deploy button's word for the plan; the summary says the rest.</summary>
    public string DeployLabel => Plan switch
    {
        InstallPlan.Update => "Update",
        InstallPlan.Rename => "Rename",
        _ => "Add",
    };

    /// <summary>One sentence on exactly what the deploy will do to the installs.</summary>
    public string Summary
    {
        get
        {
            var target = Installations.Variables(_folder, Name)["InstanceDir"];
            var old = Selected?.Install;
            return Plan switch
            {
                InstallPlan.New when CopyFrom?.Install is { } source => $"Adds {Name} in {target}, starting from a copy of {source.Name}.",
                InstallPlan.New => $"Adds {Name} in {target}.",
                InstallPlan.Update => $"Updates {old!.Name} from {old.Version ?? "an unknown version"} to {version}.",
                _ => $"Copies {old!.Name} to {target}, deploys {version} there as {Name}, then offers to remove {old.Name}.",
            };
        }
    }

    /// <summary>The plan in a few words, above its detail in the install folder. An older package is called out.</summary>
    public string Headline => Plan switch
    {
        InstallPlan.New => $"New install: {Name}",
        InstallPlan.Update => Installations.Compare(Selected?.Install?.Version, version) switch
        {
            VersionChange.Downgrade => $"Downgrade {Name}",
            VersionChange.Same => $"Reinstall {Name}",
            _ => $"Update {Name}",
        },
        _ => $"Rename {Selected!.Install!.Name} to {Name}",
    };

    /// <summary>The rest of the plan, under its headline: where it goes, or which versions.</summary>
    public string Detail
    {
        get
        {
            var target = Installations.Variables(_folder, Name)["InstanceDir"];
            var old = Selected?.Install;
            return Plan switch
            {
                InstallPlan.New when CopyFrom?.Install is { } source => $"Starts from a copy of {source.Name} in {target}",
                InstallPlan.New => target,
                InstallPlan.Update => $"{old!.Version ?? "Unknown version"} → {version}"
                    + (Installations.Compare(old.Version, version) == VersionChange.Downgrade ? ", an older version than the one installed" : ""),
                _ => $"Deploys {version} to {target}, then offers to remove {old!.Name}",
            };
        }
    }

    /// <summary>A new install with no name yet: a prompt to type one rather than an error.</summary>
    public bool NeedsName => Selected?.IsNew == true && Name.Length == 0;

    /// <summary>Why the deploy can't go ahead with these choices, or null.</summary>
    public string? Problem
    {
        get
        {
            if (!Path.IsPathFullyQualified(_folder))
                return "Set the install folder first.";
            if (Selected is null)
                return "Pick an install to update, or New.";
            if (Name.Length == 0)
                return Selected.IsNew ? "Type a name for the new install." : "An install needs a name.";
            if (Installations.CheckName(Name) is { } nameProblem)
                return nameProblem;
            if (Installs.Any(other => other != Selected.Install && other.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)))
                return $"There is already an install called {Name}.";
            return null;
        }
    }

    /// <summary>The install a rename replaces and should remove afterwards.</summary>
    public Installation? Replaced => Plan == InstallPlan.Rename ? Selected?.Install : null;

    public DeployOptions Apply(DeployOptions options) => options with
    {
        Instance = Name,
        SeedFrom = Replaced?.Directory ?? (Plan == InstallPlan.New ? CopyFrom?.Install?.Directory : null),
    };

    /// <summary>
    /// Looks for installs in the folder. Looking again at the same folder keeps the selection, the name and the copy
    /// source; a different folder starts from its first install.
    /// </summary>
    public void Refresh(string folder)
    {
        var sameFolder = folder.Equals(_folder, StringComparison.OrdinalIgnoreCase);
        _folder = folder;
        var kept = sameFolder ? Selected : null;
        var keptCopy = sameFolder ? CopyFrom?.Install?.Directory : null;
        var found = Installations.Find(folder, appFiles);

        // Rebuilding the lists makes the bound list boxes clear their selection; that is not the user's choice.
        _refreshing = true;
        try
        {
            Targets.Clear();
            foreach (var existing in found)
                Targets.Add(new InstallTarget(existing));
            Targets.Add(new InstallTarget(null));

            CopySources.Clear();
            CopySources.Add(new InstallTarget(null));
            foreach (var existing in found)
                CopySources.Add(new InstallTarget(existing));

            Selected = Targets.FirstOrDefault(target => kept is not null && target.IsNew == kept.IsNew && target.Install?.Directory == kept.Install?.Directory);
            CopyFrom = CopySources.FirstOrDefault(source => source.Install?.Directory == keptCopy);
        }
        finally
        {
            _refreshing = false;
        }

        // A first look, or the kept install has gone: start from the first install, else New.
        if (Selected is null)
            Selected = Targets.FirstOrDefault();
        NotifyPlanChanged();
    }

    partial void OnSelectedChanged(InstallTarget? oldValue, InstallTarget? newValue)
    {
        if (!_refreshing && newValue is not null)
        {
            Name = newValue.Install?.Name ?? "";
            CopyFrom = CopySources.FirstOrDefault();
        }
        NotifyPlanChanged();
    }

    partial void OnNameChanged(string value) => NotifyPlanChanged();

    partial void OnCopyFromChanged(InstallTarget? value) => NotifyPlanChanged();

    private void NotifyPlanChanged()
    {
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(PlanIcon));
        OnPropertyChanged(nameof(DeployLabel));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(NeedsName));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanRename));
    }
}
