using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App;

/// <summary>The config review: each file as it will be written, with changes marked like a GitHub diff.</summary>
public partial class MergeWindow : Window
{
    public MergeWindow(MergeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        // Pressing Enter doesn't move focus, so keep a value that is still being typed.
        (Keyboard.FocusedElement as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (((MergeViewModel)DataContext).CanApply)
            DialogResult = true;
    }
}
