using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NetDeploy.App.Converters;

/// <summary>Shows an element when a count is zero, or with ShowWhenZero false, when it is not.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool ShowWhenZero { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 == ShowWhenZero ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
