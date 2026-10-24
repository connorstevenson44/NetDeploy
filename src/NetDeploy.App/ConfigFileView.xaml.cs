using System.Windows;
using System.Windows.Controls;

namespace NetDeploy.App;

/// <summary>Side-by-side settings editor for one config file, used by the config review and the folder view.</summary>
public partial class ConfigFileView : UserControl
{
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(ConfigFileView));

    public ConfigFileView() => InitializeComponent();

    /// <summary>Shows the settings without letting anyone change them, as in a dry run.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }
}
