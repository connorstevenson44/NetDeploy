using NetDeploy.Core.Cli;

namespace NetDeploy.Core.Tests;

public sealed class CliTests : IDisposable
{
    private readonly TempFolder _package = new();
    private readonly TempFolder _installs = new();
    private readonly TempFolder _historyFolder = new();
    private readonly StringWriter _output = new();
    private readonly DeployHistory _history;

    public CliTests()
    {
        _history = new DeployHistory(_historyFolder.Path);
        _package.File("app/Orders.exe", "v2");
        _package.File("app/appsettings.json", """{ "Db": "localhost" }""");
        Manifest("""{ "type": "copyFiles", "from": "app", "to": "{InstanceDir}" }""");
    }

    public void Dispose()
    {
        _package.Dispose();
        _installs.Dispose();
        _historyFolder.Dispose();
    }

    private void Manifest(string steps, bool requiresAdmin = false) => _package.File("deploy.json", $$"""
        {
          "schemaVersion": 1,
          "app": { "name": "Orders", "version": "2.0.0" },
          "requiresAdmin": {{(requiresAdmin ? "true" : "false")}},
          "install": { "folder": "{InstallDir}" },
          "parameters": {
            "InstallDir": { "type": "folder" },
            "Environment": { "type": "choice", "options": ["Staging", "Production"] }
          },
          "steps": [ {{steps}} ]
        }
        """);

    private Task<int> RunAsync(string arguments, bool isAdministrator = true) =>
        new CliRunner(_output, _history, isAdministrator).RunAsync(
            ["deploy", _package.Path, "--param", $"InstallDir={_installs.Path}", .. arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            TestContext.Current.CancellationToken);

    private string Output => _output.ToString();

    [Fact]
    public void The_options_are_read_from_the_command_line()
    {
        var options = CliOptions.Parse(["deploy", "app.zip", "--install", "east", "--param", "Db=Server=sql;Pooling=true", "--dry-run", "--yes"])!;

        Assert.Equal(("app.zip", "east", true, true, false), (options.Package, options.Instance, options.DryRun, options.Yes, options.NoRollback));
        Assert.Equal("Server=sql;Pooling=true", options.ValueOf("db"));
        Assert.Null(CliOptions.Parse(["help"]));
        Assert.True(CliOptions.IsCommand(["Deploy", "app.zip"]));
        Assert.False(CliOptions.IsCommand([@"C:\packages\app.zip"]));
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("deploy a.zip b.zip")]
    [InlineData("deploy a.zip --param Db")]
    [InlineData("deploy a.zip --install")]
    [InlineData("deploy a.zip --force")]
    public void A_mistake_in_the_command_is_explained(string arguments)
    {
        var error = Assert.Throws<CliException>(() => CliOptions.Parse(arguments.Split(' ')));

        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public async Task A_deploy_adds_the_install_and_records_it_in_the_history()
    {
        var exitCode = await RunAsync("--install east");

        Assert.Equal(CliRunner.Finished, exitCode);
        Assert.Equal("v2", _installs.Read("east/Orders.exe"));
        Assert.Contains("Deploy finished.", Output);
        Assert.Contains("Log: ", Output);
        var record = Assert.Single(_history.For(_installs.Combine("east")));
        Assert.Equal(("Orders", "2.0.0", "east", DeployResult.Succeeded, false), (record.App, record.Version, record.Install, record.Result, record.RolledBack));
        Assert.True(File.Exists(record.Log));
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_and_isnt_recorded()
    {
        var exitCode = await RunAsync("--install east --dry-run");

        Assert.Equal(CliRunner.Finished, exitCode);
        Assert.False(Directory.Exists(_installs.Combine("east")));
        Assert.Empty(_history.For(_installs.Combine("east")));
    }

    [Fact]
    public async Task Without_an_install_name_the_installs_there_are_listed()
    {
        await RunAsync("--install east");
        _output.GetStringBuilder().Clear();

        var exitCode = await RunAsync("");

        Assert.Equal(CliRunner.Mistake, exitCode);
        Assert.Contains("has east", Output);
    }

    [Fact]
    public async Task Values_the_package_cant_use_stop_it_before_anything_runs()
    {
        var exitCode = await RunAsync("--install east --param Environment=Test --param Enviroment=Staging");

        Assert.Equal(CliRunner.Mistake, exitCode);
        Assert.Contains("Environment must be one of Staging, Production.", Output);
        Assert.Contains("no parameter or input called Enviroment", Output);
        Assert.False(Directory.Exists(_installs.Combine("east")));
    }

    [Fact]
    public async Task A_package_that_needs_administrator_rights_says_so()
    {
        Manifest("""{ "type": "copyFiles", "from": "app", "to": "{InstanceDir}" }""", requiresAdmin: true);

        Assert.Equal(CliRunner.Mistake, await RunAsync("--install east", isAdministrator: false));
        Assert.Contains("administrator", Output);
        Assert.Equal(CliRunner.Finished, await RunAsync("--install east --dry-run", isAdministrator: false));
    }

    [Fact]
    public async Task A_question_needs_yes_to_go_on()
    {
        Manifest("""{ "type": "copyFiles", "from": "app", "to": "{InstanceDir}", "when": { "ask": "Copy the app?" } }""");

        Assert.Equal(CliRunner.Failed, await RunAsync("--install east"));
        Assert.Contains("--yes", Output);
        Assert.False(Directory.Exists(_installs.Combine("east")));

        Assert.Equal(CliRunner.Finished, await RunAsync("--install east --yes"));
        Assert.True(_installs.Exists("east/Orders.exe"));
    }

    [Fact]
    public async Task Step_inputs_are_answered_from_the_command_line()
    {
        Manifest("""
            { "type": "copyFiles", "from": "app", "to": "{InstanceDir}/{Region}", "inputs": { "Region": { "type": "text" } } }
            """);

        Assert.Equal(CliRunner.Failed, await RunAsync("--install east"));
        Assert.Contains("Region needs a value", Output);

        Assert.Equal(CliRunner.Finished, await RunAsync("--install east --param Region=west"));
        Assert.True(_installs.Exists("east/west/Orders.exe"));
    }

    [Fact]
    public async Task A_setting_that_needs_a_value_stops_the_deploy_and_rolls_it_back()
    {
        await RunAsync("--install east");
        _package.File("app/Orders.exe", "v3");
        _package.File("app/appsettings.json", """{ "Db": "localhost", "ApiKey": "CHANGE_ME" }""");
        Manifest("""
            { "type": "backup", "path": "{InstanceDir}" },
            { "type": "copyFiles", "from": "app", "to": "{InstanceDir}" }
            """);

        Assert.Equal(CliRunner.Failed, await RunAsync("--install east --dry-run"));
        Assert.Contains("appsettings.json ApiKey: Needs a value for this server", Output);
        Assert.Contains("Dry run finished", Output);

        Assert.Equal(CliRunner.Failed, await RunAsync("--install east"));
        Assert.Contains("Rolled back.", Output);
        Assert.Equal("v2", _installs.Read("east/Orders.exe"));
        var latest = _history.For(_installs.Combine("east"))[0];
        Assert.Equal((DeployResult.Cancelled, true), (latest.Result, latest.RolledBack));

        Directory.Delete(_installs.Combine("east.backups"), recursive: true);
    }
}
