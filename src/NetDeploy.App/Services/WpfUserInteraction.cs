using System.Windows;
using NetDeploy.App.Views;
using NetDeploy.Core.Config;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Manifest;

namespace NetDeploy.App.Services;

/// <summary>
/// Shows the deploy's questions as dialogs, and its config review in the workspace. Called from the deploy thread,
/// so each call hops to the UI thread.
/// </summary>
internal sealed class WpfUserInteraction : IUserInteraction
{
    private static Window Owner => Application.Current.MainWindow;

    /// <summary>Shows the config review in the main window; set once the window's view model exists.</summary>
    public Func<IReadOnlyList<ConfigFileUpdate>, bool, Task<bool>> ReviewConfigs { get; set; } = (_, _) => Task.FromResult(false);

    public Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(Owner, question, "NetDeploy", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes).Task;

    public Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
        string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new InputsWindow(title, fields) { Owner = Owner };
            return window.ShowDialog() == true ? window.Answers : null;
        }).Task;

    public Task<bool> ReviewConfigsAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly, CancellationToken cancellationToken) =>
        ReviewConfigs(files, readOnly);

    public Task<CleanupDecision> ConfirmCleanupAsync(string title, string explanation, int number, int total, CancellationToken cancellationToken) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new CleanupWindow(title, explanation, number, total) { Owner = Owner };
            window.ShowDialog();
            return window.Decision;
        }).Task;
}
