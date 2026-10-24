using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using NetDeploy.App.ViewModels;

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
        // and it then throws "ItemsControl is inconsistent with its items source".
        viewModel.Log.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { Count: > 0 } added)
                Dispatcher.InvokeAsync(() =>
                {
                    LogList.ScrollIntoView(added[0]!);
                    FolderLogList.ScrollIntoView(added[0]!);
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
        if (paths[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            _viewModel.EditConfig(paths[0]);
        else
            _viewModel.Load(paths[0]);
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
