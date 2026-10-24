using System.Windows;
using System.Windows.Controls;

namespace NetDeploy.App;

/// <summary>Track-changes editor for one file, used by the config review and the folder view.</summary>
public partial class ConfigFileView : UserControl
{
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(ConfigFileView));

    public ConfigFileView() => InitializeComponent();

    /// <summary>Shows the file and its changes without letting anyone change them, as in a dry run.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private void OnPreviousChange(object sender, RoutedEventArgs e) => Editor.GoToMark(-1);

    private void OnNextChange(object sender, RoutedEventArgs e) => Editor.GoToMark(1);
}
