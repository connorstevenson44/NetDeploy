using System.Windows;
using NetDeploy.App.ViewModels;
using NetDeploy.Core.Manifest;

namespace NetDeploy.App.Views;

/// <summary>Asks for a step's inputs just before the step runs.</summary>
public partial class InputsWindow : Window
{
    public InputsWindow(string stepTitle, IReadOnlyDictionary<string, InputField> fields)
    {
        InitializeComponent();
        Title = stepTitle;
        Fields = fields.Select(input => new InputFieldViewModel(input.Key, input.Value)).ToList();
        DataContext = this;
    }

    public IReadOnlyList<InputFieldViewModel> Fields { get; }

    public IReadOnlyDictionary<string, string> Answers => Fields.ToDictionary(input => input.Name, input => input.Value);

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        var errors = Fields.Select(input => input.Validate()).OfType<string>().ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Check the values", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
