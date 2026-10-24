using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App;

/// <summary>Shows an element when a count is zero, or with ShowWhenZero false, when it is not.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool ShowWhenZero { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 == ShowWhenZero ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The icon for a step type, as the editor's outline shows it.</summary>
public sealed class StepIconConverter : IValueConverter
{
    public static StepIconConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ViewModels.EditorRowViewModel.IconFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A grid size from a flag: Star when true, nothing when false, for a column that only shows sometimes.</summary>
public sealed class BoolToStarConverter : IValueConverter
{
    public double Star { get; set; } = 1;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? new GridLength(Star, GridUnitType.Star) : new GridLength(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
