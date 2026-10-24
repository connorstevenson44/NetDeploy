using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;

namespace NetDeploy.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        // Keep the newest log line in view. The scroll waits until the ListBox has handled the change:
        // scrolling inside CollectionChanged can reach the ListBox before it knows about the new item,
        // and it then throws "ItemsControl is inconsistent with its items source". Lines arrive in
        // batches, so one scroll is queued per batch rather than one per line.
        var scrollQueued = false;
        viewModel.Log.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not { Count: > 0 } || scrollQueued)
                return;
            scrollQueued = true;
            Dispatcher.InvokeAsync(() =>
            {
                scrollQueued = false;
                if (viewModel.Log.Count == 0)
                    return;
                LogList.ScrollIntoView(viewModel.Log[^1]);
            }, DispatcherPriority.Background);
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
            return;
        // A .json is a config to edit, a folder without deploy.json an install folder to manage, anything else a package.
        var path = paths[0];
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            _viewModel.EditConfig(path);
        else if (Directory.Exists(path) && !File.Exists(Path.Combine(path, DeployPackage.ManifestFileName)))
            _viewModel.ShowFolder(path);
        else
            _viewModel.Load(path);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var warning = _viewModel.IsRunning ? "A deploy is still running. Close anyway?"
            : _viewModel.Editor.IsDirty ? "The deploy config you are editing has unsaved changes. Close anyway?"
            : null;
        if (warning is not null)
            e.Cancel = MessageBox.Show(this, warning, "NetDeploy", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
    }
}
