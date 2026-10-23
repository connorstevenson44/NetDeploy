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
                Dispatcher.InvokeAsync(() => LogList.ScrollIntoView(added[0]!), DispatcherPriority.Background);
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
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            _viewModel.Load(paths[0]);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsRunning)
            e.Cancel = MessageBox.Show(this, "A deploy is still running. Close anyway?", "NetDeploy",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
    }
}
