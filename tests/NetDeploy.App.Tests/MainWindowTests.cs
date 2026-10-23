using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;
using NetDeploy.Core.Config;

namespace NetDeploy.App.Tests;

/// <summary>
/// Drives the real window on a Windows machine. WPF allows one Application per process,
/// so everything runs in one test.
/// </summary>
public class MainWindowTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "OrdersService");

    [Fact]
    public void A_dry_run_of_the_sample_package_runs_without_ui_errors()
    {
        MainViewModel? viewModel = null;
        RunOnStaThread(() =>
        {
            // A plain Application: App.OnStartup would open a second window and show a message box on errors.
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NetDeploy;component/Styles.xaml") });
            Exception? uiError = null;
            app.DispatcherUnhandledException += (_, e) =>
            {
                // An error during layout repeats on every pass, so stop at the first one.
                uiError ??= e.Exception;
                e.Handled = true;
                Dispatcher.ExitAllFrames();
            };

            viewModel = new MainViewModel(new AutoAnswerUser());
            var window = new MainWindow(viewModel);
            window.Show();
            viewModel.Load(SamplePath);
            viewModel.Parameters.Single(p => p.Name == "InstallDir").Value = Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "missing");
            viewModel.Parameters.Single(p => p.Name == "DbConnection").Value = "asd";

            // Run the dispatcher normally and stop it once the dry run and the layout after it are done.
            Exception? failure = null;
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await viewModel.DryRunCommand.ExecuteAsync(null);
                    // ContextIdle runs after layout and the queued log scrolls. ApplicationIdle never comes on a CI desktop.
                    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    Dispatcher.ExitAllFrames();
                }
            });
            Dispatcher.Run();

            if (uiError is not null)
                Assert.Fail($"The window threw: {uiError}");
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();

            window.Close();
            Assert.Contains(viewModel.Log, entry => entry.Message.Contains("Dry run finished"));
            Assert.DoesNotContain(viewModel.Steps, step => step.State == StepState.Failed);
        }, onTimeout: () => $"Status: {viewModel?.Status}");
    }

    private static void RunOnStaThread(Action action, Func<string> onTimeout)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            Assert.Fail($"The UI test timed out. {onTimeout()}");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class AutoAnswerUser : IUserInteraction
    {
        public Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
            string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(fields.ToDictionary(f => f.Key, f => f.Value.Default ?? "x"));

        public Task<bool> ReviewMergeAsync(string filePath, MergePlan plan, bool readOnly, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
