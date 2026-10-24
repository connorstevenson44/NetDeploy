using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using NetDeploy.Core;

namespace NetDeploy.App.ViewModels;

/// <summary>A row in the install picker: an install found in the folder, or a new one.</summary>
public sealed record InstallTarget(Installation? Install)
{
    public bool IsNew => Install is null;
    public string Label => Install is null ? "New" : Install.IsInstance ? Install.Name : "Single install";

    /// <summary>The label in the "start from a copy of" list, where no install means a fresh one.</summary>
    public string CopyLabel => Install is null ? "Nothing: a fresh install" : Label;

    public string Detail => Install?.Version is { } version ? "v" + version : "";

    public PackIconKind Icon => Install switch
    {
        null => PackIconKind.PlusCircleOutline,
        { IsInstance: true } => PackIconKind.ServerNetwork,
        _ => PackIconKind.Server,
    };
}

/// <summary>What this deploy does to the installs in the folder.</summary>
public enum InstallPlan { NewSingle, NewInstance, Update, Rename, Convert }

/// <summary>
/// Picks where the deploy goes when the manifest has an install block: update an install that is there, add a new
/// one, rename an instance, or turn a single install into an instance.
/// </summary>
public sealed partial class InstallViewModel(InstallInfo install, string version, IReadOnlyCollection<string> appFiles) : ObservableObject
{
    private string _folder = "";
    private bool _refreshing;

    public bool AllowsInstances => install.Instances;

    public ObservableCollection<InstallTarget> Targets { get; } = [];

    /// <summary>What a new instance can start from: nothing, or a copy of an install.</summary>
    public ObservableCollection<InstallTarget> CopySources { get; } = [];

    public IEnumerable<Installation> Installs => Targets.Select(target => target.Install).OfType<Installation>();

    [ObservableProperty]
    public partial InstallTarget? Selected { get; set; }

    /// <summary>The instance name; empty for a single install.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>For a new instance: an install to copy, so its config edits and data come along.</summary>
    [ObservableProperty]
    public partial InstallTarget? CopyFrom { get; set; }

    public bool CanCopy => Selected?.IsNew == true && Installs.Any();

    public InstallPlan Plan => Selected?.Install switch
    {
        null => Name.Length == 0 ? InstallPlan.NewSingle : InstallPlan.NewInstance,
        { } existing when existing.Name.Equals(Name, StringComparison.OrdinalIgnoreCase) => InstallPlan.Update,
        { IsInstance: false } => InstallPlan.Convert,
        _ => InstallPlan.Rename,
    };

    public PackIconKind PlanIcon => Plan switch
    {
        InstallPlan.Update => PackIconKind.Sync,
        InstallPlan.Rename => PackIconKind.RenameOutline,
        InstallPlan.Convert => PackIconKind.CallSplit,
        _ => PackIconKind.PlusCircleOutline,
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
                InstallPlan.NewSingle => $"Installs {version} into {target}.",
                InstallPlan.NewInstance when CopyFrom?.Install is { } source =>
                    $"Adds instance {Name} in {target}, starting from a copy of {Describe(source)}.",
                InstallPlan.NewInstance => $"Adds instance {Name} in {target}.",
                InstallPlan.Update => $"Updates {Describe(old!)} from {old!.Version ?? "an unknown version"} to {version}.",
                InstallPlan.Convert => $"Copies the single install to {target}, deploys {version} there as instance {Name}, then offers to remove the old one.",
                _ => $"Copies {Describe(old!)} to {target}, deploys {version} there as instance {Name}, then offers to remove {old!.Name}.",
            };
        }
    }

    /// <summary>Why the deploy can't go ahead with these choices, or null.</summary>
    public string? Problem
    {
        get
        {
            if (!Path.IsPathFullyQualified(_folder))
                return "Set the install folder first.";
            if (Selected is null)
                return "Pick an install to update, or New.";
            if (Name.Length > 0 && Installations.CheckName(Name) is { } nameProblem)
                return nameProblem;
            if (Name.Length > 0 && Installs.Any(other => other != Selected.Install && other.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)))
                return $"There is already an install called {Name}.";
            if (Name.Length == 0 && Selected.Install is { IsInstance: true })
                return "An instance needs a name.";
            if (Name.Length == 0 && Selected.IsNew && Installs.Any())
                return Installs.Any(other => !other.IsInstance) ? "There is already a single install here. Name the new instance." : "Name the new instance.";
            return null;
        }
    }

    /// <summary>The install this deploy replaces and should remove afterwards, for a rename or a conversion.</summary>
    public Installation? Replaced => Plan is InstallPlan.Rename or InstallPlan.Convert ? Selected?.Install : null;

    public DeployOptions Apply(DeployOptions options) => options with
    {
        Instance = Name,
        SeedFrom = Replaced?.Directory ?? (Plan == InstallPlan.NewInstance ? CopyFrom?.Install?.Directory : null),
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
            if (AllowsInstances || found.Count == 0)
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
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(CanCopy));
    }

    private static string Describe(Installation existing) => existing.IsInstance ? $"instance {existing.Name}" : "the single install";
}
