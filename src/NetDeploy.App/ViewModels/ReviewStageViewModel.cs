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
