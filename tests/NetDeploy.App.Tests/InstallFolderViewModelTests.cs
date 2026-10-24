using System.IO;
using NetDeploy.App.ViewModels;

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

    [Fact]
    public void The_folder_lists_its_installs_and_files_but_not_netdeploys_own()
    {
        var folder = new InstallFolderViewModel(_root);

        Assert.Equal(["Single install", "east"], folder.Installs.Select(install => install.Label));
        Assert.DoesNotContain(folder.Items.OfType<FolderFileViewModel>(), file => file.RelativePath.StartsWith(".deploy"));
        Assert.True(File(folder, "appsettings.json").IsConfig);
        Assert.True(File(folder, "Orders.exe").CanRun);
    }

    [Fact]
    public void Edits_and_deletes_wait_for_finalize_then_are_made_together()
    {
        var folder = new InstallFolderViewModel(_root);
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
    public void Discard_drops_every_staged_change()
    {
        var folder = new InstallFolderViewModel(_root);
        folder.OpenCommand.Execute(File(folder, "appsettings.json"));
        folder.OpenFile!.Items.OfType<SettingViewModel>().Single(setting => setting.Key == "Timeout").Value = "60";
        folder.ToggleDeleteCommand.Execute(File(folder, "Orders.exe"));

        folder.DiscardCommand.Execute(null);

        Assert.False(folder.HasPending);
        Assert.Null(folder.OpenFile);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Orders.exe")));
    }
}
