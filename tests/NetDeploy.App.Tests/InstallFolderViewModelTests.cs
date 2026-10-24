using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;

namespace NetDeploy.App.Tests;

public class InstallFolderViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "folder", Path.GetRandomFileName())).FullName;

    public InstallFolderViewModelTests()
    {
        Write("Orders.exe", "");
        Write("appsettings.json", """{ "Db": "prod", "Timeout": 30 }""");
        Write("logs/old.log", "x");
        Write(".deploy/base/appsettings.json", "{}");
        Write("east/Orders.exe", "");
        Write("main/Orders.exe", "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string path, string text)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, text);
    }

    private static FolderFileViewModel File(InstallFolderViewModel folder, string path) =>
        folder.Items.OfType<FolderFileViewModel>().Single(file => file.RelativePath == path);

    private static FolderNodeViewModel Row(InstallFolderViewModel folder, string name) =>
        folder.Rows.OfType<FolderNodeViewModel>().Single(row => row.Name == name);

    private static async Task GoIntoAsync(InstallFolderViewModel folder, string name)
    {
        folder.GoIntoCommand.Execute(Row(folder, name));
        await folder.Listing;
    }

    private async Task<InstallFolderViewModel> OpenAsync(InstallViewModel? target = null)
    {
        var folder = new InstallFolderViewModel(_root, target, ["Orders.exe"]);
        await folder.Listing;
        return folder;
    }

    private InstallViewModel Target()
    {
        var target = new InstallViewModel("2.5.0", ["Orders.exe"]);
        target.Refresh(_root);
        return target;
    }

    [Fact]
    public async Task The_folder_lists_its_installs_and_files_but_not_netdeploys_own()
    {
        var folder = await OpenAsync();

        Assert.Equal(["east", "main"], folder.Installs.Select(install => install.Label));
        Assert.DoesNotContain(folder.Items.OfType<FolderFileViewModel>(), file => file.RelativePath.StartsWith(".deploy"));
        Assert.True(File(folder, "appsettings.json").IsConfig);
        Assert.True(File(folder, "Orders.exe").CanRun);
    }

    [Fact]
    public async Task The_view_lists_the_folders_in_the_one_it_is_in_and_goes_into_and_up_out_of_them()
    {
        Write("logs/2026/october.log", "x");
        var folder = await OpenAsync();

        Assert.Equal(_root, folder.CurrentFolder!.FullPath);
        Assert.False(folder.CurrentFolder.IsInstall);
        Assert.False(folder.CanGoUp);
        Assert.Equal(["east", "logs", "main"], folder.Rows.OfType<FolderNodeViewModel>().Select(row => row.Name));
        Assert.Equal("install", Row(folder, "east").Kind);
        Assert.False(Row(folder, "logs").IsInstall);

        await GoIntoAsync(folder, "logs");
        Assert.True(folder.CanGoUp);
        Assert.EndsWith(" › logs", folder.Location);
        Assert.Equal(["2026"], folder.Rows.OfType<FolderNodeViewModel>().Select(row => row.Name));
        Assert.Equal(["old.log", "october.log"], folder.Items.OfType<FolderFileViewModel>().Select(file => file.Name));

        await GoIntoAsync(folder, "2026");
        Assert.Empty(folder.Rows);
        folder.GoUpCommand.Execute(null);
        folder.GoUpCommand.Execute(null);
        await folder.Listing;

        Assert.Equal(_root, folder.CurrentFolder!.FullPath);
        Assert.False(folder.GoUpCommand.CanExecute(null));
        Assert.Contains("Orders.exe", folder.Items.OfType<FolderFileViewModel>().Select(file => file.Name));
    }

    [Fact]
    public async Task The_files_are_a_tree_whose_folders_fold_and_a_filter_narrows_them()
    {
        Write("logs/2026/october.log", "x");
        var folder = await OpenAsync();
        var logs = folder.Items.OfType<FolderViewModel>().Single(heading => heading.Path == "logs");
        Assert.True(logs.IsExpanded);
        Assert.Contains(folder.Items.OfType<FolderFileViewModel>(), file => file.RelativePath == "logs/2026/october.log");

        logs.ToggleCommand.Execute(null);
        Assert.False(folder.Items.OfType<FolderViewModel>().Single(heading => heading.Path == "logs").IsExpanded);
        Assert.DoesNotContain(folder.Items, item => item is FolderViewModel { Path: "logs/2026" });
        Assert.DoesNotContain(folder.Items.OfType<FolderFileViewModel>(), file => file.Folder.StartsWith("logs"));

        folder.FileFilter = "october";
        Assert.Equal(["logs", "logs/2026"], folder.Items.OfType<FolderViewModel>().Select(heading => heading.Path));
        Assert.Equal(["logs/2026/october.log"], folder.Items.OfType<FolderFileViewModel>().Select(file => file.RelativePath));

        folder.FileFilter = "nothing like this";
        Assert.False(folder.HasFiles);
        Assert.Equal("No files match the filter.", folder.NoFilesText);
    }

    [Fact]
    public async Task An_install_with_no_service_shows_a_question_mark()
    {
        var folder = await OpenAsync();
        await folder.CheckServices;

        Assert.Equal(ServiceState.NotFound, Row(folder, "east").ServiceState);
        Assert.Equal(ServiceState.Unknown, Row(folder, "logs").ServiceState);
    }

    [Fact]
    public async Task A_huge_folder_lists_only_the_first_files()
    {
        for (var i = 0; i <= InstallFolderViewModel.MaxFiles; i++)
            Write($"cache/{i}.tmp", "");
        var folder = await OpenAsync();

        Assert.True(folder.IsCapped);
        Assert.Equal(InstallFolderViewModel.MaxFiles, folder.Items.OfType<FolderFileViewModel>().Count());
    }

    [Fact]
    public async Task Going_into_an_install_makes_it_the_deploys_target()
    {
        var target = Target();
        var folder = await OpenAsync(target);
        Assert.Equal("east", target.Selected!.Label);

        await GoIntoAsync(folder, "logs");
        Assert.Equal("east", target.Selected!.Label);
        folder.GoUpCommand.Execute(null);
        await GoIntoAsync(folder, "main");

        Assert.Equal("main", target.Selected!.Install!.Name);
        Assert.Equal(InstallPlan.Update, target.Plan);
        Assert.Equal("Update", target.DeployLabel);
    }

    [Fact]
    public async Task A_target_picked_elsewhere_moves_the_view_to_it()
    {
        var target = Target();
        var folder = await OpenAsync(target);

        target.Selected = target.Targets.Single(choice => choice.Label == "main");
        await folder.Listing;
        Assert.Equal("main", folder.CurrentFolder!.Name);

        // Clone picks the new install for the user to name, and its row is in the install folder.
        target.Selected = target.Targets.Single(choice => choice.IsNew);
        await folder.Listing;

        Assert.Equal(_root, folder.CurrentFolder!.FullPath);
        Assert.True(Assert.IsType<NewInstallRowViewModel>(folder.Rows[^1]).IsTarget);
        Assert.Equal("Add", target.DeployLabel);
    }

    [Fact]
    public async Task The_last_row_names_a_new_install_while_the_installs_stay_in_view()
    {
        var target = Target();
        var folder = await OpenAsync(target);
        var row = Assert.IsType<NewInstallRowViewModel>(folder.Rows[^1]);
        Assert.False(row.IsTarget);
        Assert.Equal("", row.Name);

        row.Name = "west";

        Assert.True(row.IsTarget);
        Assert.Equal(InstallPlan.New, target.Plan);
        Assert.Equal("west", target.Name);

        // Picking an install and coming back keeps what was typed.
        await GoIntoAsync(folder, "east");
        Assert.Equal("west", row.Name);
        folder.GoUpCommand.Execute(null);
        row.PickCommand.Execute(null);
        Assert.Equal("west", target.Name);
        Assert.Equal(InstallPlan.New, target.Plan);
    }

    [Fact]
    public async Task Reading_the_folder_again_stays_in_the_same_folder_and_keeps_the_target()
    {
        Write("logs/2026/october.log", "x");
        var target = Target();
        var folder = await OpenAsync(target);
        await GoIntoAsync(folder, "main");
        folder.GoUpCommand.Execute(null);
        await GoIntoAsync(folder, "logs");
        await GoIntoAsync(folder, "2026");

        folder.Reload();
        await folder.Listing;

        Assert.Equal("2026", folder.CurrentFolder!.Name);
        Assert.Equal(["october.log"], folder.Items.OfType<FolderFileViewModel>().Select(file => file.Name));
        Assert.Equal("main", target.Selected!.Install!.Name);
    }

    [Fact]
    public async Task An_installs_deploys_are_listed_under_it()
    {
        var history = new DeployHistory(Path.Combine(_root, "history"));
        var east = Path.Combine(_root, "east");
        history.TryAdd(new DeployRecord("Orders", "2.4.0", "east", east, DateTimeOffset.Now.AddDays(-1), @"CONTOSO\dana", DeployResult.Succeeded, false, null));
        history.TryAdd(new DeployRecord("Orders", "2.5.0", "east", east, DateTimeOffset.Now, @"CONTOSO\dana", DeployResult.Failed, true, null));
        var folder = new InstallFolderViewModel(_root, null, ["Orders.exe"], history);
        await folder.Listing;
        Assert.False(folder.HasHistory);

        await GoIntoAsync(folder, "east");

        Assert.Equal(["2.5.0 failed, rolled back", "2.4.0 deployed"], folder.History.Select(entry => entry.Label));
        Assert.EndsWith(@"by CONTOSO\dana", folder.History[0].Detail);
        Assert.False(folder.History[0].HasLog);
    }

    [Fact]
    public async Task Edits_and_deletes_wait_for_finalize_then_are_made_together()
    {
        var folder = await OpenAsync();
        folder.OpenCommand.Execute(File(folder, "appsettings.json"));
        folder.OpenFile!.Text = folder.OpenFile.Text.Replace("\"prod\"", "\"prod2\"");
        folder.ToggleDeleteCommand.Execute(File(folder, "logs/old.log"));

        Assert.Equal((1, 1), (folder.EditCount, folder.DeleteCount));
        Assert.True(File(folder, "appsettings.json").HasEdits);
        Assert.Contains("Delete logs/old.log", folder.PendingSummary);
        Assert.Contains("prod\"", System.IO.File.ReadAllText(Path.Combine(_root, "appsettings.json")));

        folder.Apply();

        Assert.Contains("prod2", System.IO.File.ReadAllText(Path.Combine(_root, "appsettings.json")));
        Assert.False(System.IO.File.Exists(Path.Combine(_root, "logs/old.log")));
        Assert.False(folder.HasPending);
    }

    [Fact]
    public async Task Staged_changes_survive_picking_another_folder()
    {
        var folder = await OpenAsync();
        folder.ToggleDeleteCommand.Execute(File(folder, "logs/old.log"));

        await GoIntoAsync(folder, "logs");

        Assert.True(File(folder, "old.log").IsStagedForDelete);
        Assert.Equal(1, folder.DeleteCount);
    }

    [Fact]
    public async Task Discard_drops_every_staged_change()
    {
        var folder = await OpenAsync();
        folder.OpenCommand.Execute(File(folder, "appsettings.json"));
        folder.OpenFile!.Text = folder.OpenFile.Text.Replace("30", "60");
        folder.ToggleDeleteCommand.Execute(File(folder, "Orders.exe"));

        folder.DiscardCommand.Execute(null);

        Assert.False(folder.HasPending);
        Assert.Null(folder.OpenFile);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Orders.exe")));
    }
}
