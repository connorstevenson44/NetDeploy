using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetDeploy.App.Services;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App;

public partial class App : Application
{
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var viewModel = new MainViewModel(new WpfUserInteraction());
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();

        // A package path on the command line, used when relaunching as administrator.
        if (e.Args.Length > 0)
            viewModel.Load(e.Args[0]);
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
