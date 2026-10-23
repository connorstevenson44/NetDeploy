using System.Windows;
using NetDeploy.App.Services;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "NetDeploy error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var viewModel = new MainViewModel(new WpfUserInteraction());
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();

        // A package path on the command line, used when relaunching as administrator.
        if (e.Args.Length > 0)
            viewModel.Load(e.Args[0]);
    }
}
