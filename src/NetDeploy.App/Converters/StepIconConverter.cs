using System.Globalization;
using System.Windows.Data;

namespace NetDeploy.App.Converters;

/// <summary>The icon for a step type, as the editor's outline shows it.</summary>
public sealed class StepIconConverter : IValueConverter
{
    public static StepIconConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ViewModels.EditorRowViewModel.IconFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
