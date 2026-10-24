using System.IO;
using NetDeploy.App.Services;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;
using NetDeploy.Core.Config;

namespace NetDeploy.App.Tests;

public sealed class UserSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "settings", Path.GetRandomFileName())).FullName;

    public UserSettingsTests()
    {
        Write("package/app/Orders.exe", "");
        Write("package/deploy.json", """
            {
              "schemaVersion": 1,
              "app": { "name": "Orders", "version": "2.0.0" },
              "install": { "folder": "{InstallDir}" },
              "parameters": {
                "InstallDir": { "type": "folder", "default": "C:\\Apps\\Orders" },
                "Environment": { "default": "Production" },
                "Password": { "type": "secret" }
              },
              "steps": [ { "type": "copyFiles", "from": "app", "to": "{InstanceDir}" } ]
            }
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string At(string relative) => Path.Combine(_root, relative);

    private void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(At(relative))!);
        File.WriteAllText(At(relative), text);
    }

    [Fact]
    public void Settings_are_read_back_after_a_restart()
    {
        var file = At("settings/settings.json");
        var settings = UserSettings.Load(file);
        settings.AddRecent(At("package"), RecentKind.Package);
        settings.SaveValues("Orders", @"C:\Apps\East\", new Dictionary<string, string> { ["Environment"] = "Staging" });

        var again = UserSettings.Load(file);

        Assert.Equal((At("package"), RecentKind.Package), (again.Recent.Single().Path, again.Recent.Single().Kind));
        Assert.Equal(@"C:\Apps\East\", again.LastFolder("orders"));
        Assert.Equal("Staging", again.Values("ORDERS", @"c:\apps\east")["environment"]);
        Assert.Empty(again.Values("Orders", @"C:\Apps\West"));
    }

    [Fact]
    public void The_recent_list_is_newest_first_without_repeats_and_kept_short()
    {
        var settings = UserSettings.InMemory();
        for (var i = 0; i < UserSettings.MaxRecent + 3; i++)
            settings.AddRecent(At($"folder{i}"), RecentKind.Folder);
        settings.AddRecent(At("folder5"), RecentKind.Folder);

        Assert.Equal(UserSettings.MaxRecent, settings.Recent.Count);
        Assert.Equal(At("folder5"), settings.Recent[0].Path);
        Assert.Single(settings.Recent, item => item.Path == At("folder5"));
    }

    [Fact]
    public void A_file_that_cant_be_read_starts_afresh()
    {
        Write("settings.json", "not json");

        Assert.Empty(UserSettings.Load(At("settings.json")).Recent);
    }

    [Fact]
    public void Opening_a_package_fills_in_the_folder_and_values_it_was_last_deployed_with_but_never_a_secret()
    {
        var settings = UserSettings.InMemory();
        var east = At("server/east");
        settings.SaveValues("Orders", east, new Dictionary<string, string> { ["InstallDir"] = east, ["Environment"] = "Staging", ["Password"] = "s3cret" });
        using var viewModel = new MainViewModel(new NoUser(), settings);

        viewModel.Load(At("package"));

        Assert.Equal(east, viewModel.FolderField!.Value);
        Assert.Equal("Staging", viewModel.Parameters.Single(parameter => parameter.Name == "Environment").Value);
        Assert.Equal("", viewModel.Parameters.Single(parameter => parameter.Name == "Password").Value);
        Assert.Equal(At("package"), viewModel.Recent.Single().Path);
    }

    [Fact]
    public void Each_install_gets_back_the_values_it_was_last_deployed_with()
    {
        var settings = UserSettings.InMemory();
        var east = At("server/east");
        Write("server/east/main/Orders.exe", "");
        Write("server/east/blue/Orders.exe", "");
        settings.SaveValues("Orders", east, new Dictionary<string, string> { ["InstallDir"] = east, ["Environment"] = "Staging" }, Path.Combine(east, "main"));
        settings.SaveValues("Orders", east, new Dictionary<string, string> { ["InstallDir"] = east, ["Environment"] = "Production" }, Path.Combine(east, "blue"));
        using var viewModel = new MainViewModel(new NoUser(), settings);
        var environment = () => viewModel.Parameters.Single(parameter => parameter.Name == "Environment").Value;
        InstallTarget Target(string? name) => viewModel.Install!.Targets.Single(target => target.Install?.Name == name);

        viewModel.Load(At("package"));
        Assert.Equal("blue", viewModel.Install!.Selected?.Label);
        Assert.Equal("Production", environment());

        viewModel.Install.Selected = Target("main");
        Assert.Equal("Staging", environment());

        // What was typed for an install stays while it is still the one picked.
        viewModel.Parameters.Single(parameter => parameter.Name == "Environment").Value = "Test";
        viewModel.Install.Selected = Target("main");
        Assert.Equal("Test", environment());

        // A new install that starts from a copy of blue starts from blue's values.
        viewModel.Install.Selected = Target(null);
        viewModel.Install.CopyFrom = viewModel.Install.CopySources.Single(source => source.Install?.Name == "blue");
        Assert.Equal("Production", environment());
        Assert.Equal(east, viewModel.FolderField!.Value);
    }

    [Fact]
    public void A_remembered_value_the_package_no_longer_takes_is_left_out()
    {
        Write("choices/app/Orders.exe", "");
        Write("choices/deploy.json", """
            {
              "schemaVersion": 1,
              "app": { "name": "Orders", "version": "3.0.0" },
              "parameters": {
                "Environment": { "type": "choice", "options": ["Staging", "Production"] },
                "Workers": { "type": "number", "default": "4" },
                "Region": { "default": "east" }
              },
              "steps": [ { "type": "copyFiles", "from": "app", "to": "C:\\Apps\\Orders" } ]
            }
            """);
        var settings = UserSettings.InMemory();
        settings.SaveValues("Orders", "", new Dictionary<string, string> { ["Environment"] = "QA", ["Workers"] = "many", ["Region"] = "west" });
        using var viewModel = new MainViewModel(new NoUser(), settings);

        viewModel.Load(At("choices"));

        Assert.Equal(["Staging", "4", "west"], viewModel.Parameters.Select(parameter => parameter.Value));
    }

    private sealed class NoUser : IUserInteraction
    {
        public Task<bool> AskYesNoAsync(string question, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>?> AskInputsAsync(
            string title, IReadOnlyDictionary<string, InputField> fields, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(null);

        public Task<bool> ReviewConfigsAsync(IReadOnlyList<ConfigFileUpdate> files, bool readOnly, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<CleanupDecision> ConfirmCleanupAsync(string title, string explanation, int number, int total, CancellationToken cancellationToken) =>
            Task.FromResult(CleanupDecision.Cancel);
    }
}
