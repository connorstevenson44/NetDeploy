using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// A step of the deploy that waits for the user in the workspace's right-hand pane: first the files, then the config.
/// The deploy awaits Decision; Continue or Cancel completes it.
/// </summary>
public abstract partial class ReviewStageViewModel(int number, bool isDryRun) : ObservableObject
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>1 for the files, 2 for the config; the deploy itself is 3.</summary>
    public int Number { get; } = number;

    public bool IsDryRun { get; } = isDryRun;
    public abstract string Title { get; }
    public abstract string Description { get; }
    public virtual string ContinueText => IsDryRun ? "Continue the dry run" : "Continue";
    public virtual bool CanContinue => true;

    /// <summary>True once the user continues, false if they cancel.</summary>
    public Task<bool> Decision => _decision.Task;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => _decision.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _decision.TrySetResult(false);
}

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
                items.AddRange(files.HasItems ? files.Items : [new FilesMatch()]);
        }
        return items;
    }
}

/// <summary>Stands for a copy step's files when the server already matches the package.</summary>
public sealed record FilesMatch;

/// <summary>Step 2: the config files the copy step will write, as side-by-side diffs.</summary>
public sealed partial class ConfigStageViewModel : ReviewStageViewModel
{
    public ConfigStageViewModel(MergeViewModel review) : base(2, review.IsReadOnly)
    {
        Review = review;
        review.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MergeViewModel.CanApply))
            {
                OnPropertyChanged(nameof(CanContinue));
                ContinueCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public MergeViewModel Review { get; }
    public override string Title => "Check the config";

    public override string Description => IsDryRun
        ? "Dry run: this is how the config files would be merged. Nothing is written."
        : "Each file is the server's copy merged with the package's. Change any value on the right, then write the files.";

    public override string ContinueText => IsDryRun ? "Continue the dry run" : "Write files and continue";
    public override bool CanContinue => Review.CanApply;
}
