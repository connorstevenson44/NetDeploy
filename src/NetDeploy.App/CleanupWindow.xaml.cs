using System.Windows;
using NetDeploy.Core;

namespace NetDeploy.App;

/// <summary>Confirms one step of removing an install. Run stays disabled until the box is ticked.</summary>
public partial class CleanupWindow : Window
{
    public CleanupWindow(string title, string explanation, int number, int total)
    {
        InitializeComponent();
        Counter.Text = $"REMOVING THE INSTALL: STEP {number} OF {total}";
        StepTitle.Text = title;
        Explanation.Text = explanation;
    }

    public CleanupDecision Decision { get; private set; } = CleanupDecision.Cancel;

    private void OnUnderstoodChanged(object sender, RoutedEventArgs e) => RunButton.IsEnabled = Understood.IsChecked == true;

    private void OnRun(object sender, RoutedEventArgs e) => Close(CleanupDecision.Run);

    private void OnSkip(object sender, RoutedEventArgs e) => Close(CleanupDecision.Skip);

    private void OnCancel(object sender, RoutedEventArgs e) => Close(CleanupDecision.Cancel);

    private void Close(CleanupDecision decision)
    {
        Decision = decision;
        DialogResult = decision != CleanupDecision.Cancel;
    }
}
