using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core.Deployment;
using NetDeploy.Core.Steps;

namespace NetDeploy.App.Tests;

public sealed class FilePlanViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "files", Path.GetRandomFileName())).FullName;
    private readonly CopyFilesStep _step = new() { From = "app", To = "{Target}", DeleteExtra = true };

    public FilePlanViewModelTests()
    {
        Write("package/app/app.dll", "v2");
        Write("package/app/same.dll", "same");
        Write("package/app/plugins/new.dll", "new");
        Write("server/app.dll", "v1");
        Write("server/same.dll", "same");
        Write("server/old.dll", "old");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string path, string text)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private FilePlanViewModel Plan() =>
        new(_step, "Copy app", _step.PlanFiles(Path.Combine(_root, "package"), new Dictionary<string, string> { ["Target"] = Path.Combine(_root, "server") }));

    private static FileRowViewModel Row(FilePlanViewModel plan, string path) =>
        plan.Items.OfType<FileRowViewModel>().Single(row => row.Path == path);

    [Fact]
    public void A_dry_runs_summary_counts_the_files_and_lists_the_steps_that_would_change_the_server()
    {
        var stop = new ServiceStep { Action = ServiceAction.Stop, Service = "Orders" };
        var check = new IfStep { Condition = new() { Ask = "Migrate?" }, Then = [new PowerShellStep { Script = "migrate" }] };
        var steps = StepViewModel.Flatten([stop, _step, check]).ToList();
        steps[0].State = StepState.Succeeded;
        steps[1].State = StepState.Succeeded;
        steps[2].State = StepState.Skipped;
        steps[3].State = StepState.Skipped;

        var summary = RunSummaryViewModel.For([Plan()], steps);

        Assert.Equal(["Files: 1 added, 1 replaced, 1 deleted.", "Stop service Orders", "2 steps skipped by their conditions."],
            summary.Lines.Select(line => line.Text));
    }

    [Fact]
    public void Identical_files_are_hidden_and_folders_head_their_files()
    {
        var plan = Plan();

        Assert.Equal(["app.dll", "old.dll", "plugins", "plugins/new.dll"],
            plan.Items.Select(item => item is FolderViewModel folder ? folder.Name : ((FileRowViewModel)item).Path));
        Assert.Equal((1, 1, 1, 1), (plan.NewCount, plan.ReplaceCount, plan.DeleteCount, plan.KeepCount));

        plan.ShowUnchanged(true);
        Assert.Contains(plan.Items, item => item is FileRowViewModel { Path: "same.dll" });
    }

    [Fact]
    public void Only_changed_choices_go_to_the_deploy()
    {
        var plan = Plan();
        var old = Row(plan, "old.dll");
        Assert.True(old.Options.Single(option => option.Action == FileAction.Delete).IsSelected);
        Assert.Equal([FileAction.Keep, FileAction.Delete], old.Options.Where(option => option.IsAvailable).Select(option => option.Action));

        old.Options.Single(option => option.Action == FileAction.Keep).ChooseCommand.Execute(null);

        Assert.True(old.IsChosen);
        Assert.Equal((0, 2), (plan.DeleteCount, plan.KeepCount));
        Assert.Equal(new Dictionary<string, FileAction> { ["old.dll"] = FileAction.Keep }, plan.ChosenActions);
    }
}
