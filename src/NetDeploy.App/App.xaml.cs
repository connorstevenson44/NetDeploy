using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetDeploy.App.Services;
using NetDeploy.App.ViewModels;
using NetDeploy.App.Views;
using NetDeploy.Core.Cli;
using NetDeploy.Core.Deployment;

namespace NetDeploy.App;

public partial class App : Application
{
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (CliOptions.IsCommand(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunCommandAsync(e.Args);
            return;
        }
        DispatcherUnhandledException += OnUnhandledException;

        var user = new WpfUserInteraction();
        var viewModel = new MainViewModel(user, UserSettings.Load(UserSettings.DefaultPath), DeployHistory.Machine);
        user.ReviewConfigs = viewModel.ReviewConfigsInPaneAsync;
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();

        // A package path on the command line, used when relaunching as administrator.
        if (e.Args.Length > 0)
            viewModel.Load(e.Args[0]);
    }

    /// <summary>
    /// Runs a command such as "deploy" without the window, writing to the console it was started from, and exits
    /// with its exit code. A bug is reported there too, rather than in a dialog nobody may be there to close. Ctrl+C
    /// cancels the deploy, which then rolls back, rather than killing it halfway.
    /// </summary>
    private async Task RunCommandAsync(string[] args)
    {
        ParentConsole.Attach();
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        int exitCode;
        try
        {
            exitCode = await Task.Run(() => new CliRunner(Console.Out, DeployHistory.Machine, Elevation.IsAdministrator).RunAsync(args, cancel.Token));
        }
        catch (Exception ex)
        {
            Console.Out.WriteLine($"NetDeploy hit an unexpected error: {ex}");
            exitCode = CliRunner.Failed;
        }
        Console.Out.Flush();
        Shutdown(exitCode);
    }

    /// <summary>
    /// Saves the details and closes the app. Carrying on after a bug can leave the window broken, and an error
    /// thrown during layout repeats on every pass, which filled the screen with dialogs.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;

        string report;
        try
        {
            report = UiLog.WriteCrashReport(e.Exception);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            report = "(the crash report could not be saved)";
        }

        MessageBox.Show(
            $"NetDeploy hit an unexpected error and will close.{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}"
            + $"{Environment.NewLine}{Environment.NewLine}Full details: {report}",
            "NetDeploy error", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
