using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetDeploy.App.Services;
using NetDeploy.Core;

namespace NetDeploy.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IUserInteraction _user;
    private DeployPackage? _package;
    private Dictionary<DeployStep, StepViewModel> _stepsByDefinition = [];
    private CancellationTokenSource? _cancellation;
    private RollbackPlan? _rollback;

    public MainViewModel(IUserInteraction user)
    {
        _user = user;
        Title = "No package open";
        Status = "Open a package (.zip), or drop a .zip or a folder with deploy.json onto this window.";
    }

    public ObservableCollection<InputFieldViewModel> Parameters { get; } = [];
    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<LogEntry> Log { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string? PackagePath { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool NeedsElevation { get; set; }

    [ObservableProperty]
    public partial bool HasPackage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(OpenPackageCommand), nameof(DryRunCommand), nameof(DeployCommand), nameof(CancelCommand), nameof(RollBackCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsIdle => !IsRunning;

    private bool CanRun => _package is not null && !IsRunning;
    private bool CanRollBack => _rollback is { IsAvailable: true } && !IsRunning;

    public void Load(string path)
    {
        if (IsRunning)
            return;

        DeployPackage package;
        try
        {
            package = DeployPackage.Open(path);
        }
        catch (Exception ex) when (ex is ManifestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Can't open the package", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _package?.Dispose();
        _package = package;
        HasPackage = true;
        _rollback = null;

        var manifest = package.Manifest;
        Title = $"{manifest.App.Name} {manifest.App.Version}";
        PackagePath = path;
        NeedsElevation = manifest.RequiresAdmin && !Elevation.IsAdministrator;

        Parameters.Clear();
        foreach (var (name, field) in manifest.Parameters)
        {
            var parameter = new InputFieldViewModel(name, field);
            parameter.PropertyChanged += (_, _) => RefreshSteps();
            Parameters.Add(parameter);
        }

        Steps.Clear();
        foreach (var step in StepViewModel.Flatten(manifest.Steps))
            Steps.Add(step);
        _stepsByDefinition = Steps.ToDictionary(step => step.Step);
        RefreshSteps();

        Log.Clear();
        Status = $"{manifest.Steps.Count} steps. Try a dry run first to see what will change.";
        DryRunCommand.NotifyCanExecuteChanged();
        DeployCommand.NotifyCanExecuteChanged();
        RollBackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenPackage()
    {
        var dialog = new OpenFileDialog { Title = "Open a deploy package", Filter = "Deploy packages (*.zip)|*.zip" };
        if (dialog.ShowDialog() == true)
            Load(dialog.FileName);
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (Elevation.TryRestartElevated(PackagePath))
            Application.Current.Shutdown();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DryRunAsync() => RunAsync(dryRun: true);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DeployAsync() => RunAsync(dryRun: false);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRollBack))]
    private async Task RollBackAsync()
    {
        if (!Confirm("Restore the backup from the last deploy and restart the services it stopped?", "Roll back"))
            return;

        IsRunning = true;
        try
        {
            using var log = UiLog.Create(Log, _package!.Manifest.App.Name, "rollback");
            await RestoreAsync(log);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunAsync(bool dryRun)
    {
        var errors = Parameters.Select(parameter => parameter.Validate()).OfType<string>().ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Check the parameters", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!dryRun && NeedsElevation)
        {
            MessageBox.Show("This package needs administrator rights. Use Restart as administrator first.", "Administrator rights needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!dryRun && !Confirm($"Deploy {Title}?", "Deploy"))
            return;

        foreach (var step in Steps)
            step.State = StepState.Pending;
        Log.Clear();
        _rollback = null;
        Status = dryRun ? "Dry run in progress..." : "Deploying...";
        IsRunning = true;

        var package = _package!;
        using var log = UiLog.Create(Log, package.Manifest.App.Name, dryRun ? "dryrun" : "deploy");
        _cancellation = new CancellationTokenSource();
        try
        {
            var runner = new DeployRunner(_user, log);
            runner.StepChanged += (step, state) =>
                Application.Current.Dispatcher.InvokeAsync(() => _stepsByDefinition[step].State = state);

            var values = Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value);
            var token = _cancellation.Token;
            var outcome = await Task.Run(() => runner.RunAsync(package, values, dryRun, token));

            Status = outcome.Result switch
            {
                DeployResult.Succeeded => dryRun ? "Dry run finished. Nothing was changed." : "Deploy finished.",
                DeployResult.Failed => $"Failed at \"{outcome.FailedStep!.Title}\": {outcome.Error!.Message}",
                _ => "Cancelled.",
            } + $" Log: {log.FilePath}";

            if (!dryRun && outcome.Result != DeployResult.Succeeded && outcome.Rollback.IsAvailable)
            {
                _rollback = outcome.Rollback;
                if (Confirm($"{Status}{Environment.NewLine}{Environment.NewLine}Restore the backup and restart the services this deploy stopped?", "Roll back"))
                    await RestoreAsync(log);
            }
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsRunning = false;
        }
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

    private void RefreshSteps()
    {
        // Secrets are left as {Name} so they never show in the plan.
        var values = Parameters
            .Where(parameter => parameter.Type != InputType.Secret)
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var step in Steps)
            step.Refresh(values);
    }

    private static bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        _cancellation?.Cancel();
        _package?.Dispose();
    }
}
