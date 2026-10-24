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

    private static FolderNodeViewModel Child(FolderNodeViewModel node, string name) =>
        node.Children.OfType<FolderNodeViewModel>().Single(child => child.Name == name);

    private async Task<InstallFolderViewModel> OpenAsync(InstallViewModel? target = null)
    {
        var folder = new InstallFolderViewModel(_root, target, ["Orders.exe"]);
        await folder.Listing;
        return folder;
    }

    private InstallViewModel Target()
    {
        var target = new InstallViewModel(new InstallInfo { Folder = "{InstallDir}", Instances = true }, "2.5.0", ["Orders.exe"]);
        target.Refresh(_root);
        return target;
    }

    [Fact]
    public async Task The_folder_lists_its_installs_and_files_but_not_netdeploys_own()
    {
        var folder = await OpenAsync();

        Assert.Equal(["Single install", "east"], folder.Installs.Select(install => install.Label));
        Assert.DoesNotContain(folder.Items.OfType<FolderFileViewModel>(), file => file.RelativePath.StartsWith(".deploy"));
        Assert.True(File(folder, "appsettings.json").IsConfig);
        Assert.True(File(folder, "Orders.exe").CanRun);
    }

    [Fact]
    public async Task The_tree_shows_only_folders_with_installs_marked_and_reads_subfolders_when_opened()
    {
        Write("logs/2026/october.log", "x");
        var folder = await OpenAsync();
        var root = folder.Tree.Single();

        Assert.True(root.IsExpanded);
        Assert.Equal("single install", root.Kind);
        Assert.Equal(["east", "logs"], root.Children.OfType<FolderNodeViewModel>().Select(node => node.Name));
        Assert.Equal("instance", Child(root, "east").Kind);
        Assert.False(Child(root, "logs").IsInstall);

        // A folder with subfolders shows an expander, and reads them only when it is opened.
        var logs = Child(root, "logs");
        Assert.Same(LoadingNode.Instance, Assert.Single(logs.Children));
        logs.IsExpanded = true;
        Assert.Equal(["2026"], logs.Children.OfType<FolderNodeViewModel>().Select(node => node.Name));
        Assert.Empty(Child(root, "east").Children);
    }

    [Fact]
    public async Task Picking_a_folder_lists_its_files_from_there()
    {
        var folder = await OpenAsync();
        Assert.Same(folder.Tree[0], folder.SelectedFolder);

        Child(folder.Tree[0], "logs").IsSelected = true;
        await folder.Listing;

        Assert.False(folder.Tree[0].IsSelected);
        Assert.Equal("logs", folder.SelectedFolder!.Name);
        Assert.Equal(["old.log"], folder.Items.OfType<FolderFileViewModel>().Select(file => file.RelativePath));
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
    public async Task Picking_an_install_makes_it_the_deploys_target()
    {
        var target = Target();
        var folder = await OpenAsync(target);
        var root = folder.Tree[0];
        Assert.True(root.IsTarget);

        Child(root, "east").IsSelected = true;

        Assert.Equal("east", target.Selected!.Install!.Name);
        Assert.Equal(InstallPlan.Update, target.Plan);
        Assert.True(Child(root, "east").IsTarget);
        Assert.False(root.IsTarget);
    }

    [Fact]
    public async Task The_last_row_names_a_new_instance_while_the_installs_stay_in_view()
    {
        var target = Target();
        var folder = await OpenAsync(target);
        var row = Assert.IsType<NewInstallRowViewModel>(folder.Tree[0].Children[^1]);
        Assert.False(row.IsTarget);
        Assert.Equal("", row.Name);

        row.Name = "west";

        Assert.True(row.IsTarget);
        Assert.Equal(InstallPlan.NewInstance, target.Plan);
        Assert.Equal("west", target.Name);
        Assert.DoesNotContain(folder.Tree[0].Loaded(), node => node.IsTarget);

        // Picking an install and coming back keeps what was typed.
        Child(folder.Tree[0], "east").IsSelected = true;
        Assert.Equal("west", row.Name);
        row.IsSelected = true;
        Assert.Equal("west", target.Name);
        Assert.Equal(InstallPlan.NewInstance, target.Plan);
    }

    [Fact]
    public async Task Reading_the_folder_again_keeps_the_open_folders_the_pick_and_the_target()
    {
        Write("logs/2026/october.log", "x");
        var target = Target();
        var folder = await OpenAsync(target);
        Child(folder.Tree[0], "east").IsSelected = true;
        var logs = Child(folder.Tree[0], "logs");
        logs.IsExpanded = true;
        Child(logs, "2026").IsSelected = true;
        await folder.Listing;

        folder.Reload();
        await folder.Listing;

        Assert.True(Child(folder.Tree[0], "logs").IsExpanded);
        Assert.Equal("2026", folder.SelectedFolder!.Name);
        Assert.Equal(["october.log"], folder.Items.OfType<FolderFileViewModel>().Select(file => file.Name));
        Assert.Equal("east", target.Selected!.Install!.Name);
    }

    [Fact]
    public async Task Edits_and_deletes_wait_for_finalize_then_are_made_together()
    {
        var folder = await OpenAsync();
        folder.OpenCommand.Execute(File(folder, "appsettings.json"));
        folder.OpenFile!.Items.OfType<SettingViewModel>().Single(setting => setting.Key == "Db").Value = "prod2";
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

        Child(folder.Tree[0], "logs").IsSelected = true;
        await folder.Listing;

        Assert.True(File(folder, "old.log").IsStagedForDelete);
        Assert.Equal(1, folder.DeleteCount);
    }

    [Fact]
    public async Task Discard_drops_every_staged_change()
    {
        var folder = await OpenAsync();
        folder.OpenCommand.Execute(File(folder, "appsettings.json"));
        folder.OpenFile!.Items.OfType<SettingViewModel>().Single(setting => setting.Key == "Timeout").Value = "60";
        folder.ToggleDeleteCommand.Execute(File(folder, "Orders.exe"));

        folder.DiscardCommand.Execute(null);

        Assert.False(folder.HasPending);
        Assert.Null(folder.OpenFile);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Orders.exe")));
    }
}
