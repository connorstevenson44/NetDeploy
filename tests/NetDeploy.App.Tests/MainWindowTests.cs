using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

            // Run the dispatcher normally and stop it once the dry run and the layout after it are done.
            Exception? failure = null;
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await SaveScreenshotAsync(window, "1-empty");
                    viewModel.Load(SamplePath);
                    viewModel.Parameters.Single(p => p.Name == "InstallDir").Value = Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "missing");
                    viewModel.Parameters.Single(p => p.Name == "DbConnection").Value = "asd";
                    await viewModel.DryRunCommand.ExecuteAsync(null);
                    await SaveScreenshotAsync(window, "2-dry-run");
                    await SaveDialogScreenshotsAsync();
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

    /// <summary>Saves what the dialogs look like, so a design change can be reviewed from CI.</summary>
    private static async Task SaveDialogScreenshotsAsync()
    {
        var inputs = new InputsWindow("Install service OrdersService", new Dictionary<string, InputField>
        {
            ["Account"] = new() { Label = "Service account", Default = @"NT SERVICE\OrdersService" },
            ["Password"] = new() { Label = "Password", Type = InputType.Secret },
            ["Environment"] = new() { Label = "Environment", Type = InputType.Choice, Options = ["Production", "Staging"] },
            ["LogFolder"] = new() { Label = "Log folder", Type = InputType.Folder, Default = @"D:\Logs\Orders" },
            ["Archive"] = new() { Label = "Archive old orders", Type = InputType.YesNo },
        });
        inputs.Show();
        await SaveScreenshotAsync(inputs, "3-inputs");
        inputs.Close();

        var review = new MergeViewModel(SampleConfigReview(), isReadOnly: false);
        var merge = new MergeWindow(review);
        merge.Show();
        await SaveScreenshotAsync(merge, "4-config-review");
        review.SelectedFile = review.Files.Single(file => file.FileName == "appsettings.Production.json");
        await SaveScreenshotAsync(merge, "5-config-override");
        merge.Close();
    }

    /// <summary>A server running version 2.3 with its own override, and a package for 2.4 that renames a setting and adds one.</summary>
    private static IReadOnlyList<ConfigFileUpdate> SampleConfigReview()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "review", Path.GetRandomFileName())).FullName;
        void Write(string path, string text)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        const string v23 = """{ "ConnectionStrings": { "Orders": "Server=localhost;Database=Orders" }, "Logging": { "LogLevel": { "Default": "Information" } }, "Smtp": { "Server": "localhost", "Port": 25 }, "Cleanup": { "KeepDays": 30 } }""";
        Write("server/appsettings.json", v23.Replace("\"KeepDays\": 30", "\"KeepDays\": 14"));
        Write("server/.deploy/base/appsettings.json", v23);
        Write("server/appsettings.Production.json", """{ "ConnectionStrings": { "Orders": "Server=sql01;Database=Orders" }, "Smtp": { "Server": "mail.contoso.com" } }""");
        Write("server/OrdersService.exe.config", """<configuration><appSettings><add key="Region" value="East" /><add key="Batch:Size" value="500" /></appSettings></configuration>""");
        Write("package/appsettings.json", """{ "ConnectionStrings": { "Orders": "Server=localhost;Database=Orders" }, "Logging": { "LogLevel": { "Default": "Warning" } }, "Smtp": { "Host": "localhost", "Port": 25 }, "Cleanup": { "KeepDays": 30 }, "Payments": { "ApiKey": "" } }""");
        Write("package/OrdersService.exe.config", """<configuration><appSettings><add key="Region" value="West" /><add key="Batch:Size" value="250" /><add key="Batch:Retry" value="3" /></appSettings></configuration>""");

        return ConfigUpdates.Plan(Path.Combine(root, "package"), Path.Combine(root, "server"),
            ["appsettings.json", "OrdersService.exe.config"], new MergeOptions(["Logging"]));
    }

    private static async Task SaveScreenshotAsync(Window window, string name)
    {
        // ContextIdle runs after layout and the queued log scrolls. ApplicationIdle never comes on a CI desktop.
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var content = (FrameworkElement)window.Content;
        var size = new Size(content.ActualWidth, content.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(size));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        using var file = File.Create(Path.Combine(folder.FullName, name + ".png"));
        new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(file);
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

        public Task<bool> ReviewConfigsAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
