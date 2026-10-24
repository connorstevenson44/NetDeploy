using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;

namespace NetDeploy.App.Tests;

public class InstallViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "installs", Path.GetRandomFileName())).FullName;

    public InstallViewModelTests()
    {
        // A single install and an instance, both copied by hand.
        File.WriteAllText(Path.Combine(_folder, "Orders.exe"), "");
        Directory.CreateDirectory(Path.Combine(_folder, "east"));
        File.WriteAllText(Path.Combine(_folder, "east", "Orders.exe"), "");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private InstallViewModel Picker()
    {
        var picker = new InstallViewModel(new InstallInfo { Folder = "{InstallDir}", Instances = true }, "2.5.0", ["Orders.exe"]);
        picker.Refresh(_folder);
        return picker;
    }

    [Fact]
    public void Installs_are_listed_from_the_folder_and_the_first_is_updated()
    {
        var picker = Picker();

        Assert.Equal(["Single install", "east", "New"], picker.Targets.Select(target => target.Label));
        Assert.Equal(InstallPlan.Update, picker.Plan);
        Assert.Null(picker.Problem);
        Assert.Null(picker.Replaced);
    }

    [Fact]
    public void Naming_the_single_install_turns_it_into_an_instance()
    {
        var picker = Picker();

        picker.Name = "main";

        Assert.Equal(InstallPlan.Convert, picker.Plan);
        Assert.Equal(_folder, picker.Replaced!.Directory);
        var options = picker.Apply(DeployOptions.Default);
        Assert.Equal(("main", _folder), (options.Instance, options.SeedFrom));
    }

    [Fact]
    public void Renaming_an_instance_copies_it_then_replaces_it()
    {
        var picker = Picker();
        picker.Selected = picker.Targets.Single(target => target.Label == "east");
        Assert.Equal("east", picker.Name);

        picker.Name = "west";

        Assert.Equal(InstallPlan.Rename, picker.Plan);
        Assert.Equal(Path.Combine(_folder, "east"), picker.Apply(DeployOptions.Default).SeedFrom);
        Assert.Contains("then offers to remove east", picker.Summary);
    }

    [Fact]
    public void A_new_instance_needs_a_free_name_and_can_start_from_a_copy()
    {
        var picker = Picker();
        picker.Selected = picker.Targets.Single(target => target.IsNew);

        Assert.Equal("There is already a single install here. Name the new instance.", picker.Problem);
        picker.Name = "east";
        Assert.Equal("There is already an install called east.", picker.Problem);

        picker.Name = "west";
        picker.CopyFrom = picker.CopySources.Single(source => source.Install?.Name == "east");
        Assert.Null(picker.Problem);
        Assert.Equal(("west", Path.Combine(_folder, "east")), (picker.Apply(DeployOptions.Default).Instance, picker.Apply(DeployOptions.Default).SeedFrom));
    }

    [Fact]
    public void Looking_again_keeps_what_the_user_picked_and_typed()
    {
        var picker = Picker();
        picker.Name = "main";

        picker.Refresh(_folder);

        Assert.Equal(("Single install", "main"), (picker.Selected!.Label, picker.Name));
    }
}
