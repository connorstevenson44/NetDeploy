using System.Windows;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;
using NetDeploy.Core.Config;

namespace NetDeploy.App.Services;

/// <summary>Shows the deploy's questions as dialogs. Called from the deploy thread, so each call hops to the UI thread.</summary>
internal sealed class WpfUserInteraction : IUserInteraction
{
    private static Window Owner => Application.Current.MainWindow;

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

    public Task<bool> ReviewMergeAsync(string filePath, MergePlan plan, bool readOnly, CancellationToken cancellationToken) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
            new MergeWindow(new MergeViewModel(filePath, plan, readOnly)) { Owner = Owner }.ShowDialog() == true).Task;
}
