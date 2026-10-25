using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// Step 1: every file each copy step will add, replace, merge, keep or delete, in one list. The choices made here go
/// to the deploy.
/// </summary>
public sealed partial class FilesStageViewModel : ReviewStageViewModel
{
    public FilesStageViewModel(IReadOnlyList<object> plans, string? installSummary, bool isDryRun) : base(1, isDryRun)
    {
        Plans = plans;
        InstallSummary = installSummary;
        foreach (var plan in plans.OfType<FilePlanViewModel>())
            plan.PropertyChanged += OnPlanChanged;
        Items = BuildItems();
    }

    /// <summary>A FilePlanViewModel per copy step, or a FilePlanError when its files can't be listed.</summary>
    public IReadOnlyList<object> Plans { get; }

    public string? InstallSummary { get; }
    public override string Title => "Check the files";
    public override string Description => InstallSummary ?? "Choose what happens to each file, then continue.";

    /// <summary>Each copy step's heading followed by its folders and files, as one list that only builds the rows on screen.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<object> Items { get; private set; }

    [ObservableProperty]
    public partial bool ShowUnchanged { get; set; }

    partial void OnShowUnchangedChanged(bool value)
    {
        foreach (var plan in Plans.OfType<FilePlanViewModel>())
            plan.ShowUnchanged(value);
    }

    /// <summary>Text the files' paths must have in them to be listed; empty lists them all.</summary>
    [ObservableProperty]
    public partial string FileFilter { get; set; } = "";

    partial void OnFileFilterChanged(string value)
    {
        foreach (var plan in Plans.OfType<FilePlanViewModel>())
            plan.Filter(value);
    }

    private void OnPlanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilePlanViewModel.Items))
            Items = BuildItems();
    }

    private List<object> BuildItems()
    {
        var items = new List<object>();
        foreach (var plan in Plans)
        {
            items.Add(plan);
            if (plan is FilePlanViewModel files)
                items.AddRange(files.HasItems ? files.Items : FileFilter.Length > 0 ? [] : [new FilesMatch()]);
        }
        return items;
    }
}
