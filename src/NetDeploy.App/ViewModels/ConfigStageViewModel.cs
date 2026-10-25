using NetDeploy.Core.Config;

namespace NetDeploy.App.ViewModels;

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

    // A first install has nothing to merge with, so it doesn't talk about merging.
    public override string Description => (IsDryRun, Review.Files.All(file => file.Kind == ConfigFileKind.New)) switch
    {
        (true, true) => "These config files are new on this server and would be written like this. Nothing is written in a dry run.",
        (true, false) => "This is how the server's config files would be merged with the package's. Nothing is written in a dry run.",
        (false, true) => "These config files are new on this server. Check them, and edit anything, before they are written.",
        _ => "Each file is the package's with the server's values kept. Changes to the server's copy are marked: undo any, or edit the text, before it is written.",
    };

    public override string ContinueText => IsDryRun ? "Continue the dry run" : "Write files and continue";
    public override bool CanContinue => Review.CanApply;
}
