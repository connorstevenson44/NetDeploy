using System.Windows;
using System.Windows.Controls;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App;

/// <summary>The merge table: server, package and result side by side, like a git merge tool.</summary>
public partial class MergeWindow : Window
{
    public MergeWindow(MergeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        // Keep a value that is still being typed in the Result column.
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        DialogResult = true;
    }
}
