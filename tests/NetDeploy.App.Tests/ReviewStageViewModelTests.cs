using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core.Config;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.Tests;

public sealed class ReviewStageViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "stages", Path.GetRandomFileName())).FullName;

    public ReviewStageViewModelTests()
    {
        Write("package/app/app.dll", "v2");
        Write("package/app/same.dll", "same");
        Write("server/app.dll", "v1");
        Write("server/same.dll", "same");
        Write("matching/app.dll", "v2");
        Write("matching/same.dll", "same");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string path, string text)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private FilePlanViewModel Plan(string server)
    {
        var step = new CopyFilesStep { From = "app", To = "{Target}" };
        return new(step, "Copy app", step.PlanFiles(Path.Combine(_root, "package"), new Dictionary<string, string> { ["Target"] = Path.Combine(_root, server) }));
    }

    [Fact]
    public void The_files_stage_lists_each_copy_step_then_its_files()
    {
        var changed = Plan("server");
        var matching = Plan("matching");
        var error = new FilePlanError("Copy tools", "{ToolsDir} has no value.");

        var stage = new FilesStageViewModel([changed, matching, error], "Updates main from 1.0.0 to 2.0.0.", isDryRun: false);

        Assert.Equal(1, stage.Number);
        Assert.Equal("Updates main from 1.0.0 to 2.0.0.", stage.Description);
        Assert.Equal([changed, changed.Items.Single(), matching, new FilesMatch(), error], stage.Items);
    }

    [Fact]
    public void Showing_unchanged_files_adds_them_to_the_stage()
    {
        var plan = Plan("server");
        var stage = new FilesStageViewModel([plan], null, isDryRun: true);

        stage.ShowUnchanged = true;

        Assert.Contains(stage.Items, item => item is FileRowViewModel { Path: "same.dll" });
        Assert.Equal("Continue the dry run", stage.ContinueText);
    }

    [Fact]
    public async Task Continue_and_cancel_answer_the_waiting_deploy()
    {
        var go = new FilesStageViewModel([Plan("server")], null, isDryRun: false);
        go.ContinueCommand.Execute(null);
        Assert.True(await go.Decision);

        var stop = new FilesStageViewModel([Plan("server")], null, isDryRun: false);
        stop.CancelCommand.Execute(null);
        stop.ContinueCommand.Execute(null);
        Assert.False(await stop.Decision);
    }

    [Fact]
    public void The_config_stage_waits_until_every_setting_has_a_value()
    {
        const string server = """{ "Db": "prod" }""";
        var plan = ConfigMerger.Plan("""{ "Db": "local" }""", server, """{ "Db": "test" }""", ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        var update = new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, server);
        var stage = new ConfigStageViewModel(new MergeViewModel([update], isReadOnly: false));
        var changes = new List<string?>();
        stage.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.Equal(2, stage.Number);
        Assert.False(stage.ContinueCommand.CanExecute(null));

        stage.Review.SelectedFile.Choices.Single().Actions.Single(action => action.Name == "Keep the server's").Command.Execute(null);

        Assert.True(stage.ContinueCommand.CanExecute(null));
        Assert.Contains(nameof(ConfigStageViewModel.CanContinue), changes);
        Assert.Equal("Write files and continue", stage.ContinueText);
    }

    [Fact]
    public void A_dry_runs_config_stage_only_shows_the_merge()
    {
        const string server = """{ "Db": "prod" }""";
        var plan = ConfigMerger.Plan("""{ "Db": "local" }""", server, """{ "Db": "test" }""", ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        var stage = new ConfigStageViewModel(new MergeViewModel([new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, server)], isReadOnly: true));

        Assert.True(stage.IsDryRun);
        Assert.True(stage.CanContinue);
        Assert.Contains("Nothing is written", stage.Description);
    }
}
