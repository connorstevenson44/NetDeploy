using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Tests;

public sealed class InstallTests : IDisposable
{
    private readonly TempFolder _package = new();
    private readonly TempFolder _server = new();
    private readonly FakeUser _user = new();
    private readonly TestLog _log = new();

    public InstallTests()
    {
        _package.File("app/Orders.exe", "v2");
        _package.File("app/Orders.dll", "v2");
        _package.File("app/appsettings.json", """{ "Db": "local" }""");
        _package.File("deploy.json", """
            {
              "schemaVersion": 1,
              "app": { "name": "Orders", "version": "2.0.0" },
              "install": { "folder": "{InstallDir}" },
              "parameters": { "InstallDir": { "type": "folder" }, "Password": { "type": "secret" } },
              "steps": [
                { "type": "backup", "path": "{InstanceDir}" },
                { "type": "copyFiles", "from": "app", "to": "{InstanceDir}",
                  "cleanup": [ { "type": "deleteFolder", "path": "{InstanceDir}/cache-{Password}" } ] },
                { "type": "scheduledTask", "action": "createOrUpdate", "task": "Orders{InstanceSuffix}", "program": "x.exe",
                  "schedule": { "kind": "daily" }, "when": { "value": "a", "equals": "b" } }
              ]
            }
            """);
    }

    public void Dispose()
    {
        _package.Dispose();
        _server.Dispose();
        foreach (var backups in Directory.GetDirectories(Path.GetDirectoryName(_server.Path)!, Path.GetFileName(_server.Path) + "*.backups"))
            Directory.Delete(backups, recursive: true);
    }

    private async Task<DeployOutcome> DeployAsync(string instance, string? seedFrom = null)
    {
        using var package = DeployPackage.Open(_package.Path);
        var values = new Dictionary<string, string> { ["InstallDir"] = _server.Path, ["Password"] = "s3cret" };
        var options = new DeployOptions { Instance = instance, SeedFrom = seedFrom };
        return await new DeployRunner(_user, _log).RunAsync(package, values, dryRun: false, options, TestContext.Current.CancellationToken);
    }

    private IReadOnlyList<Installation> Find()
    {
        using var package = DeployPackage.Open(_package.Path);
        return Installations.Find(_server.Path, Installations.AppFiles(package));
    }

    private async Task<DeployResult> RemoveAsync(Installation install)
    {
        using var package = DeployPackage.Open(_package.Path);
        var values = new Dictionary<string, string> { ["InstallDir"] = _server.Path };
        return await new InstallRemover(_user, _log).RemoveAsync(install, package, values, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_install_gets_its_own_folder_and_nothing_extra_is_saved()
    {
        var outcome = await DeployAsync("east");

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Equal("v2", _server.Read("east/Orders.exe"));
        var stateFiles = Directory.GetFiles(_server.Combine("east/.deploy"), "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(_server.Combine("east"), file).Replace('\\', '/'));
        Assert.Equal([".deploy/base/appsettings.json"], stateFiles);
    }

    [Fact]
    public async Task A_copy_of_an_install_that_cant_be_made_fails_the_deploy()
    {
        await DeployAsync("east");
        _server.File("west", "a file where the new install's folder goes");

        var outcome = await DeployAsync("west", seedFrom: _server.Combine("east"));

        Assert.Equal(DeployResult.Failed, outcome.Result);
        Assert.Null(outcome.FailedStep);
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void Services_and_tasks_undo_themselves_with_the_install_name_filled_in()
    {
        var values = new Dictionary<string, string> { ["InstanceSuffix"] = "-east", ["InstanceDir"] = @"C:\Apps\Orders\east" };

        var service = new ServiceStep { Action = ServiceAction.Install, Service = "Orders{InstanceSuffix}", Binary = @"{InstanceDir}\Orders.exe" };
        var undoService = (ServiceStep)service.CleanupSteps().Single().WithValues(values);
        var task = new ScheduledTaskStep { Action = ScheduledTaskAction.CreateOrUpdate, TaskName = @"\Orders\Nightly{InstanceSuffix}" };
        var undoTask = (ScheduledTaskStep)task.CleanupSteps().Single().WithValues(values);

        Assert.Equal((ServiceAction.Uninstall, "Orders-east"), (undoService.Action, undoService.Service));
        Assert.Contains("sc.exe delete Orders-east", undoService.Explain());
        Assert.Equal((ScheduledTaskAction.Delete, @"\Orders\Nightly-east"), (undoTask.Action, undoTask.TaskName));
        Assert.Equal(@"C:\Apps\Orders\east\Orders.exe", ((ServiceStep)service.WithValues(values)).Binary);
        Assert.Empty(new ServiceStep { Action = ServiceAction.Start, Service = "x" }.CleanupSteps());
        Assert.Empty(new ServiceStep { Action = ServiceAction.Install, Service = "x", Cleanup = new(null, []) }.CleanupSteps());
    }

    [Fact]
    public void Cleanup_powershell_runs_after_the_built_in_undo_and_cleanup_steps_replace_it()
    {
        var manifest = ManifestLoader.Parse("""
            { "schemaVersion": 1, "app": { "name": "Orders", "version": "1.0" }, "steps": [
              { "type": "scheduledTask", "action": "createOrUpdate", "task": "Nightly", "program": "a.exe", "schedule": { "kind": "hourly" },
                "cleanup": "Remove-Item 'D:\\Data\\{Instance}' -Recurse" },
              { "type": "service", "action": "install", "service": "Orders", "binary": "a.exe",
                "cleanup": [ { "type": "deleteFolder", "path": "D:\\Cache" } ] } ] }
            """);

        var task = manifest.Steps[0];
        Assert.Equal(["Deletes the scheduled task \"Nightly\": schtasks /Delete /TN \"Nightly\" /F."], task.AutomaticCleanup());
        var undo = task.CleanupSteps().ToList();
        Assert.IsType<ScheduledTaskStep>(undo[0]);
        Assert.Equal(@"Remove-Item 'D:\Data\east' -Recurse", ((PowerShellStep)undo[1].WithValues(new Dictionary<string, string> { ["Instance"] = "east" })).Script);
        Assert.IsType<DeleteFolderStep>(Assert.Single(manifest.Steps[1].CleanupSteps()));
    }

    [Theory]
    [InlineData(@"""C:\Apps\My App\Orders.exe"" --service", @"C:\Apps\My App\Orders.exe")]
    [InlineData(@"C:\Apps\Orders\Orders.exe -k netsvcs", @"C:\Apps\Orders\Orders.exe")]
    [InlineData(@"C:\Apps\Orders\Orders", @"C:\Apps\Orders\Orders")]
    public void A_service_program_is_read_from_its_command_line(string imagePath, string program) =>
        Assert.Equal(program, WindowsServices.ProgramPath(imagePath));

    [Fact]
    public async Task Installs_are_found_from_the_folder_layout_including_hand_made_ones()
    {
        _server.File("Orders.exe", "not an install: installs are the named folders inside");
        _server.File("plugins/Payments.dll", "not an install");
        _server.File("west/Orders.exe", "hand copied");
        await DeployAsync("east");
        Directory.CreateDirectory(_server.Combine("logs"));

        Assert.Equal(["east", "west"], Find().Select(install => install.Name));
        Assert.Empty(Installations.Find(_server.Combine("missing"), ["Orders.exe"]));
    }

    [Fact]
    public void Every_install_is_in_the_service_lookup_even_with_no_services()
    {
        var folders = new[] { _server.Combine("main"), _server.Combine("blue") };

        var services = Installations.Services(folders);

        Assert.Equal(folders, services.Keys);
        // No test service runs from a fresh temp folder, on any system.
        Assert.All(services.Values, Assert.Empty);
    }

    [Fact]
    public async Task A_clone_starts_from_the_source_files_so_server_edits_are_kept()
    {
        await DeployAsync("east");
        _server.File("east/appsettings.json", """{ "Db": "prod-east" }""");
        _server.File("east/data/orders.db", "rows");

        var outcome = await DeployAsync("west", seedFrom: _server.Combine("east"));

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Contains("prod-east", _server.Read("west/appsettings.json"));
        Assert.Equal("rows", _server.Read("west/data/orders.db"));
    }

    [Fact]
    public async Task A_backup_from_an_earlier_deploy_can_be_put_back()
    {
        await DeployAsync("east");
        Assert.Empty(Installations.Backups(_server.Combine("east")));
        _server.File("east/Orders.exe", "edited on the server");
        await DeployAsync("east");
        Assert.Equal("v2", _server.Read("east/Orders.exe"));

        var backup = Assert.Single(Installations.Backups(_server.Combine("east")));
        Installations.Restore(backup, _server.Combine("east"), _log);

        Assert.Equal("edited on the server", _server.Read("east/Orders.exe"));
        Assert.True((DateTime.Now - backup.Taken).Duration() < TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData("2.0.0", "2.5.0", VersionChange.Upgrade)]
    [InlineData("2.5", "2.5.0.0", VersionChange.Same)]
    [InlineData("v3.1.0", "2.5.0", VersionChange.Downgrade)]
    [InlineData("2.5.0-preview.1", "2.5.0", VersionChange.Same)]
    [InlineData("custom build", "2.5.0", null)]
    [InlineData(null, "2.5.0", null)]
    public void A_package_version_is_compared_with_the_install_it_updates(string? installed, string package, VersionChange? change) =>
        Assert.Equal(change, Installations.Compare(installed, package));

    [Fact]
    public async Task A_new_install_needs_an_empty_folder_and_a_plain_name()
    {
        await DeployAsync("east");

        var noName = await DeployAsync("");
        Assert.Equal(DeployResult.Failed, noName.Result);
        Assert.Contains("Name the install", noName.Error!.Message);

        Assert.Equal(DeployResult.Failed, (await DeployAsync("west", seedFrom: _server.Combine("missing"))).Result);
        Assert.Equal(DeployResult.Failed, (await DeployAsync("east", seedFrom: _server.Path)).Result);
        var badName = await DeployAsync("../up");
        Assert.Equal(DeployResult.Failed, badName.Result);
        Assert.Contains("letters, digits", badName.Error!.Message);
    }

    [Fact]
    public async Task Removing_an_install_undoes_newest_first_then_deletes_its_folder()
    {
        await DeployAsync("east");
        var folder = _server.Combine("east");
        Directory.CreateDirectory(Path.Combine(folder, "cache-s3cret"));
        _user.InputAnswers["Password"] = "s3cret";
        _user.CleanupDecisions.Enqueue(CleanupDecision.Skip); // the task, which was never created

        var result = await RemoveAsync(Find().Single());

        Assert.Equal(DeployResult.Succeeded, result);
        Assert.Equal(InputType.Secret, _user.InputRequests.Single()["Password"].Type);
        Assert.Equal(4, _user.CleanupPrompts.Count);
        Assert.Contains("\"Orders-east\"", _user.CleanupPrompts[0].Explanation);
        Assert.Contains("cache-s3cret", _user.CleanupPrompts[1].Explanation);
        Assert.Contains(".backups", _user.CleanupPrompts[2].Explanation);
        Assert.Contains("everything in it", _user.CleanupPrompts[3].Explanation);
        Assert.False(Directory.Exists(folder));
        Assert.DoesNotContain("s3cret", _log.Text);
    }

    [Fact]
    public async Task A_skipped_step_is_left_alone_and_cancel_stops_the_removal()
    {
        await DeployAsync("east");
        _user.CleanupDecisions.Enqueue(CleanupDecision.Skip);
        _user.CleanupDecisions.Enqueue(CleanupDecision.Cancel);

        var result = await RemoveAsync(Find().Single());

        Assert.Equal(DeployResult.Cancelled, result);
        Assert.Equal(2, _user.CleanupPrompts.Count);
        Assert.True(_server.Exists("east/Orders.exe"));
    }
}
