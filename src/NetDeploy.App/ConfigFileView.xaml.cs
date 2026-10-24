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

    /// <summary>
    /// Keeps the two sides level: scrolling one up or down scrolls the other to the same line. Sideways each side
    /// scrolls on its own, and both show a scroll bar when either needs one, so their lines stay the same height apart.
    /// </summary>
    private void OnLinesScrolled(object sender, ScrollChangedEventArgs e)
    {
        // The event also bubbles up from the text boxes in the lines, which scroll on their own.
        if (e.OriginalSource is not ScrollViewer scrolled || scrolled.TemplatedParent is not ItemsControl lines
            || Scroller(LeftLines) is not { } left || Scroller(RightLines) is not { } right)
            return;

        var other = lines == LeftLines ? right : left;
        if (e.VerticalChange != 0 && Math.Abs(other.VerticalOffset - scrolled.VerticalOffset) > 0.5)
            other.ScrollToVerticalOffset(scrolled.VerticalOffset);

        var overflows = left.ExtentWidth > left.ViewportWidth + 0.5 || right.ExtentWidth > right.ViewportWidth + 0.5;
        var bar = overflows ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        left.HorizontalScrollBarVisibility = bar;
        right.HorizontalScrollBarVisibility = bar;
    }

    private static ScrollViewer? Scroller(ItemsControl lines) => lines.Template?.FindName("Scroll", lines) as ScrollViewer;
}
