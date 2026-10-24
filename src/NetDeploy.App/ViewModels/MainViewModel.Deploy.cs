using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetDeploy.App.Services;
using NetDeploy.Core;
using NetDeploy.Core.Config;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.ViewModels;

/// <summary>
/// The deploy: the files are checked first in the right-hand pane, the config is checked there when the copy step
/// gets to it, and the rest runs with its progress on the left and its log on the right.
/// </summary>
public sealed partial class MainViewModel
{
    private Dictionary<DeployStep, StepViewModel> _stepsByDefinition = [];
    private CancellationTokenSource? _cancellation;
    private RollbackPlan? _rollback;

    /// <summary>A review the deploy is waiting on, shown in place of the tabs; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewing))]
    public partial ReviewStageViewModel? Stage { get; private set; }

    public bool IsReviewing => Stage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(OpenPackageCommand), nameof(OpenFolderCommand), nameof(DryRunCommand), nameof(DeployCommand),
        nameof(CancelCommand), nameof(RollBackCommand), nameof(RemoveInstallCommand), nameof(CloneInstallCommand), nameof(RunFileCommand),
        nameof(FinalizeFolderCommand), nameof(NewConfigCommand), nameof(OpenConfigCommand), nameof(OpenRecentCommand),
        nameof(StartServicesCommand), nameof(StopServicesCommand), nameof(RestartServicesCommand), nameof(RestoreBackupCommand))]
    public partial bool IsRunning { get; private set; }

    public bool IsIdle => !IsRunning;

    /// <summary>The step running now, for the progress on the left.</summary>
    [ObservableProperty]
    public partial string? RunningStep { get; private set; }

    /// <summary>How far the run is, from 0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>What the last dry run found the deploy would change, above the log; null otherwise.</summary>
    [ObservableProperty]
    public partial RunSummaryViewModel? Summary { get; private set; }

    [RelayCommand]
    private void CloseSummary() => Summary = null;

    private bool CanRun => HasPackage && !IsRunning;
    private bool CanRollBack => _rollback is { IsAvailable: true } && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRunAsync() => RunAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DeployAsync() => RunAsync(dryRun: false);

    /// <summary>Stops the run, or drops the review it is waiting on.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        Stage?.CancelCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRollBack))]
    private async Task RollBackAsync()
    {
        if (!Confirm("Restore the backup from the last deploy and restart the services it stopped?", "Roll back"))
            return;

        IsRunning = true;
        SelectedTab = LogTab;
        try
        {
            using var log = UiLog.Create(Log, _package!.Manifest.App.Name, "rollback");
            await RestoreAsync(log);
        }
        finally
        {
            IsRunning = false;
            RefreshFolder();
        }
    }

    /// <summary>
    /// Shows a config review in the right-hand pane and waits for it. The window's IUserInteraction calls this, so
    /// the review is part of the workspace rather than another window.
    /// </summary>
    public Task<bool> ReviewConfigsInPaneAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly) =>
        Application.Current.Dispatcher.InvokeAsync(() => ShowStageAsync(new ConfigStageViewModel(new MergeViewModel(files, readOnly)))).Task.Unwrap();

    private async Task<bool> ShowStageAsync(ReviewStageViewModel stage)
    {
        Stage = stage;
        try
        {
            return await stage.Decision;
        }
        finally
        {
            Stage = null;
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        var errors = Parameters.Append(FolderField).OfType<InputFieldViewModel>().Select(parameter => parameter.Validate()).OfType<string>().ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Check the settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (Install?.Problem is { } installProblem)
        {
            MessageBox.Show(installProblem, "Check where to install", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!dryRun && NeedsElevation)
        {
            MessageBox.Show("This package needs administrator rights. Use Restart as administrator first.", "Administrator rights needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RememberValues();
        Summary = null;
        IsRunning = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            // Step 1: the files. Continue is the go-ahead, so there is no separate "are you sure".
            var plans = PlanFiles();
            if (plans.Count > 0 && !await ShowStageAsync(new FilesStageViewModel(plans, Install?.Summary, dryRun)))
            {
                Status = "Cancelled before anything ran.";
                return;
            }
            await RunStepsAsync(dryRun, plans);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            RunningStep = null;
            IsRunning = false;
            RefreshFolder();
        }
    }

    private async Task RunStepsAsync(bool dryRun, IReadOnlyList<object> plans)
    {
        foreach (var step in Steps)
            step.State = StepState.Pending;
        Log.Clear();
        _rollback = null;
        Progress = 0;
        SelectedTab = LogTab;
        Status = dryRun ? "Running a dry run…" : "Deploying…";

        var package = _package!;
        using var log = UiLog.Create(Log, package.Manifest.App.Name, dryRun ? "dryrun" : "deploy");
        var runner = new DeployRunner(_user, log);
        var finished = 0;
        runner.StepChanged += (step, state) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _stepsByDefinition[step].State = state;
            if (state == StepState.Running)
                RunningStep = _stepsByDefinition[step].Title;
            else
                Progress = 100.0 * ++finished / Steps.Count;
        });

        var values = ParameterValues(includeSecrets: true);
        var token = _cancellation!.Token;
        var options = new DeployOptions
        {
            FileActions = plans.OfType<FilePlanViewModel>().ToDictionary(plan => (DeployStep)plan.Step, plan => plan.ChosenActions),
        };
        if (Install is not null)
            options = Install.Apply(options);
        var replaced = Install?.Replaced;
        var outcome = await Task.Run(() => runner.RunAsync(package, values, dryRun, options, token));

        Progress = 100;
        if (dryRun && outcome.Result == DeployResult.Succeeded)
            Summary = RunSummaryViewModel.For(plans.OfType<FilePlanViewModel>(), Steps);
        Status = outcome.Result switch
        {
            DeployResult.Succeeded => dryRun ? "Dry run finished. Nothing was changed." : "Deploy finished.",
            // A failure before the first step, such as a new install's folder already having files, has no step.
            DeployResult.Failed when outcome.FailedStep is { } failed => $"Failed at \"{failed.Title}\": {outcome.Error?.Message}",
            DeployResult.Failed => $"Failed: {outcome.Error?.Message}",
            _ => "Cancelled.",
        } + $" Log: {log.FilePath}";

        if (!dryRun && outcome.Result != DeployResult.Succeeded && outcome.Rollback.IsAvailable)
        {
            _rollback = outcome.Rollback;
            if (Confirm($"{Status}{Environment.NewLine}{Environment.NewLine}Restore the backup and restart the services this deploy stopped?", "Roll back"))
                await RestoreAsync(log);
        }

        // A rename or a conversion leaves the old install behind until the new one works.
        if (!dryRun && outcome.Result == DeployResult.Succeeded && replaced is not null
            && Confirm($"The new install is in place. Remove the old one in {replaced.Directory} now? You'll OK each step.", "Remove the old install"))
            await RemoveAsync(replaced, log);
        RollBackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Lists every copy step's files against the server, for the files review. It reads the disk, so it only runs
    /// when a deploy starts, never while settings are typed: a half-typed folder such as C:\ would list a whole drive.
    /// </summary>
    private List<object> PlanFiles()
    {
        var values = ParameterValues(includeSecrets: true);
        var plans = new List<object>();
        foreach (var step in Steps.Where(step => step.Step is CopyFilesStep))
        {
            var copy = (CopyFilesStep)step.Step;
            var title = step.Branch + step.Title;
            try
            {
                plans.Add(new FilePlanViewModel(copy, title, copy.PlanFiles(_package!.RootPath, values)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                plans.Add(new FilePlanError(title, ex.Message));
            }
        }
        return plans;
    }

    private async Task RemoveAsync(Installation install, IDeployLog log)
    {
        var package = _package?.Manifest.Install is not null ? _package : null;
        var values = package is null ? new Dictionary<string, string>() : ParameterValues(includeSecrets: true);
        var result = await Task.Run(() => new InstallRemover(_user, log).RemoveAsync(install, package, values, CancellationToken.None));
        Status = result switch
        {
            DeployResult.Succeeded => $"Removed {install.Directory}.",
            DeployResult.Failed => $"Removing {install.Directory} had errors. See the log.",
            _ => "Removal stopped. Steps already run are not undone.",
        };
    }

    private async Task RestoreAsync(IDeployLog log)
    {
        var rollback = _rollback!;
        try
        {
            await Task.Run(() => rollback.Restore(log));
            _rollback = null;
            Status = "Rolled back to the backup.";
        }
        catch (Exception ex)
        {
            log.Error($"Rollback failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Rollback failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
