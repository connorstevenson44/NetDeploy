using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;

namespace NetDeploy.App.Tests;

public class InstallViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "installs", Path.GetRandomFileName())).FullName;

    public InstallViewModelTests()
    {
        // Two installs copied by hand, and an exe at the top that isn't one: installs are the named folders.
        File.WriteAllText(Path.Combine(_folder, "Orders.exe"), "");
        foreach (var name in new[] { "east", "main" })
        {
            Directory.CreateDirectory(Path.Combine(_folder, name));
            File.WriteAllText(Path.Combine(_folder, name, "Orders.exe"), "");
        }
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private InstallViewModel Picker()
    {
        var picker = new InstallViewModel("2.5.0", ["Orders.exe"]);
        picker.Refresh(_folder);
        return picker;
    }

    [Fact]
    public void Installs_are_listed_from_the_folder_and_the_first_is_updated()
    {
        var picker = Picker();

        Assert.Equal(["east", "main", "New"], picker.Targets.Select(target => target.Label));
        Assert.Equal(InstallPlan.Update, picker.Plan);
        Assert.Null(picker.Problem);
        Assert.Null(picker.Replaced);
    }

    [Fact]
    public void Renaming_an_install_copies_it_then_replaces_it()
    {
        var picker = Picker();
        Assert.Equal("east", picker.Name);
        Assert.True(picker.CanRename);

        picker.Name = "west";

        Assert.Equal((InstallPlan.Rename, "Rename"), (picker.Plan, picker.DeployLabel));
        Assert.Equal(Path.Combine(_folder, "east"), picker.Replaced!.Directory);
        var options = picker.Apply(DeployOptions.Default);
        Assert.Equal(("west", Path.Combine(_folder, "east")), (options.Instance, options.SeedFrom));
        Assert.Contains("then offers to remove east", picker.Summary);

        picker.Name = "";
        Assert.Equal("An install needs a name.", picker.Problem);
    }

    [Fact]
    public void A_new_install_needs_a_free_name_and_can_start_from_a_copy()
    {
        var picker = Picker();
        picker.Selected = picker.Targets.Single(target => target.IsNew);

        Assert.Equal((InstallPlan.New, "Add"), (picker.Plan, picker.DeployLabel));
        Assert.False(picker.CanRename);
        Assert.Equal("Name the new install.", picker.Problem);
        picker.Name = "east";
        Assert.Equal("There is already an install called east.", picker.Problem);

        picker.Name = "west";
        picker.CopyFrom = picker.CopySources.Single(source => source.Install?.Name == "east");
        Assert.Null(picker.Problem);
        Assert.Equal(("west", Path.Combine(_folder, "east")), (picker.Apply(DeployOptions.Default).Instance, picker.Apply(DeployOptions.Default).SeedFrom));
    }

    [Fact]
    public void A_different_folder_starts_from_its_first_install()
    {
        var picker = new InstallViewModel("2.5.0", ["Orders.exe"]);
        picker.Refresh(Path.Combine(_folder, "missing"));
        Assert.True(picker.Selected!.IsNew);

        picker.Refresh(_folder);

        Assert.Equal(InstallPlan.Update, picker.Plan);
    }

    [Fact]
    public void Looking_again_keeps_what_the_user_picked_and_typed()
    {
        var picker = Picker();
        picker.Selected = picker.Targets.Single(target => target.Label == "main");
        picker.Name = "blue";

        picker.Refresh(_folder);

        Assert.Equal(("main", "blue"), (picker.Selected!.Label, picker.Name));
    }
}
