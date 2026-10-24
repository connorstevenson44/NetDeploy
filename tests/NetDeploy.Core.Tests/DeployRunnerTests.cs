using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

public sealed class DeployRunnerTests : IDisposable
{
    private readonly TempFolder _package = new();
    private readonly TempFolder _target = new();
    private readonly FakeUser _user = new();
    private readonly TestLog _log = new();
    private readonly Dictionary<DeployStep, StepState> _states = [];

    public DeployRunnerTests()
    {
        _package.File("app/app.dll", "v2");
        _package.File("app/appsettings.json", """{ "Db": "localhost", "New": 1 }""");
        _target.File("app.dll", "v1");
        _target.File("appsettings.json", """{ "Db": "prod" }""");
    }

    public void Dispose()
    {
        _package.Dispose();
        _target.Dispose();
        if (Directory.Exists(_target.Path + ".backups"))
            Directory.Delete(_target.Path + ".backups", recursive: true);
    }

    private const string CopyApp = """{ "type": "copyFiles", "from": "app", "to": "{Target}" }""";

    private async Task<(DeployOutcome Outcome, DeployManifest Manifest)> RunAsync(
        string steps, bool dryRun = false, string parameters = "", Dictionary<string, string>? values = null)
    {
        _package.File("deploy.json", $$"""
            {
              "schemaVersion": 1,
              "app": { "name": "Orders", "version": "2.0.0" },
              "parameters": { {{parameters}} },
              "steps": [ {{steps}} ]
            }
            """);

        using var package = DeployPackage.Open(_package.Path);
        var runner = new DeployRunner(_user, _log);
        runner.StepChanged += (step, state) => _states[step] = state;

        var allValues = new Dictionary<string, string>(values ?? []) { ["Target"] = _target.Path };
        var outcome = await runner.RunAsync(package, allValues, dryRun, null, TestContext.Current.CancellationToken);
        return (outcome, package.Manifest);
    }

    [Fact]
    public async Task A_deploy_backs_up_copies_files_and_merges_settings()
    {
        var (outcome, _) = await RunAsync($$"""
            { "type": "backup", "path": "{Target}" },
            {{CopyApp}}
            """);

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Equal("v2", _target.Read("app.dll"));
        Assert.Equal("""{"Db":"prod","New":1}""", Compact(_target.Read("appsettings.json")));
        Assert.Equal(_package.Read("app/appsettings.json"), _target.Read(".deploy/base/appsettings.json"));
        Assert.Single(_user.Reviews);
        Assert.True(outcome.Rollback.IsAvailable);
        Assert.Single(Directory.GetDirectories(_target.Path + ".backups"));
    }

    [Fact]
    public async Task A_dry_run_changes_nothing_but_still_shows_the_merge()
    {
        var (outcome, _) = await RunAsync($$"""
            { "type": "backup", "path": "{Target}" },
            {{CopyApp}}
            """, dryRun: true);

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Equal("v1", _target.Read("app.dll"));
        Assert.Equal("""{ "Db": "prod" }""", _target.Read("appsettings.json"));
        Assert.False(Directory.Exists(_target.Path + ".backups"));
        Assert.Single(_user.Reviews);
        Assert.Contains("Would copy 1 files", _log.Text);
    }

    [Fact]
    public async Task Steps_whose_condition_is_false_are_skipped()
    {
        _user.YesNoAnswers.Enqueue(true);

        var (outcome, manifest) = await RunAsync("""
            { "type": "copyFiles", "from": "app", "to": "{Target}/prod", "when": { "value": "{Env}", "equals": "Prod" } },
            { "type": "copyFiles", "from": "app", "to": "{Target}/asked", "when": { "ask": "Copy to {Env}?" } }
            """, values: new() { ["Env"] = "Dev" });

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Equal(StepState.Skipped, _states[manifest.Steps[0]]);
        Assert.False(_target.Exists("prod/app.dll"));
        Assert.Equal("Copy to Dev?", Assert.Single(_user.Questions));
        Assert.True(_target.Exists("asked/app.dll"));
    }

    [Fact]
    public async Task Step_inputs_are_asked_before_the_step_and_usable_in_later_steps()
    {
        _user.InputAnswers["Folder"] = "chosen";

        var (outcome, _) = await RunAsync("""
            { "type": "copyFiles", "from": "app", "to": "{Target}/{Folder}",
              "inputs": { "Folder": { "label": "Folder", "default": "{Target}" } } },
            { "type": "copyFiles", "from": "app", "to": "{Target}/again-{Folder}" }
            """);

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Equal(_target.Path, Assert.Single(_user.InputRequests)["Folder"].Default);
        Assert.True(_target.Exists("chosen/app.dll"));
        Assert.True(_target.Exists("again-chosen/app.dll"));
    }

    [Fact]
    public async Task An_if_step_runs_one_branch_and_marks_the_other_skipped()
    {
        var (outcome, manifest) = await RunAsync("""
            { "type": "if", "condition": { "value": "yes", "equals": "YES" },
              "then": [ { "type": "copyFiles", "from": "app", "to": "{Target}/then" } ],
              "else": [ { "type": "copyFiles", "from": "app", "to": "{Target}/else" } ] }
            """);

        var ifStep = (Steps.IfStep)manifest.Steps[0];
        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.True(_target.Exists("then/app.dll"));
        Assert.False(_target.Exists("else/app.dll"));
        Assert.Equal(StepState.Skipped, _states[ifStep.Else[0]]);
    }

    [Fact]
    public async Task The_first_failure_stops_the_deploy_and_the_backup_restores_the_old_files()
    {
        var (outcome, manifest) = await RunAsync($$"""
            { "type": "backup", "path": "{Target}" },
            {{CopyApp}},
            { "type": "copyFiles", "from": "missing", "to": "{Target}" },
            { "type": "copyFiles", "from": "app", "to": "{Target}/never" }
            """);

        Assert.Equal(DeployResult.Failed, outcome.Result);
        Assert.Same(manifest.Steps[2], outcome.FailedStep);
        Assert.False(_states.ContainsKey(manifest.Steps[3]));
        Assert.Equal("v2", _target.Read("app.dll"));

        outcome.Rollback.Restore(_log);

        Assert.Equal("v1", _target.Read("app.dll"));
    }

    [Fact]
    public async Task ContinueOnError_lets_the_deploy_carry_on()
    {
        var (outcome, manifest) = await RunAsync($$"""
            { "type": "copyFiles", "from": "missing", "to": "{Target}", "continueOnError": true },
            {{CopyApp}}
            """);

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Equal(StepState.Failed, _states[manifest.Steps[0]]);
        Assert.Equal("v2", _target.Read("app.dll"));
    }

    [Fact]
    public async Task Secret_values_never_reach_the_log()
    {
        await RunAsync(
            """{ "type": "runExe", "path": "migrate.exe", "args": "--connection {Db}" }""",
            dryRun: true,
            parameters: """ "Db": { "type": "secret" } """,
            values: new() { ["Db"] = "Password=s3cret" });

        Assert.DoesNotContain("s3cret", _log.Text);
        Assert.Contains("--connection ****", _log.Text);
    }

    [Fact]
    public async Task Cancelling_the_config_review_cancels_the_deploy_without_writing()
    {
        _user.CancelReview = true;

        var (outcome, _) = await RunAsync(CopyApp);

        Assert.Equal(DeployResult.Cancelled, outcome.Result);
        Assert.Equal("""{ "Db": "prod" }""", _target.Read("appsettings.json"));
    }

    [Fact]
    public async Task Choices_made_in_the_review_are_written()
    {
        _user.ResolveReview = files => files[0].Plan.Rows.Single(r => r.Path == "Db").Choice = MergeChoice.TakeIncoming;

        await RunAsync(CopyApp);

        Assert.Equal("""{"Db":"localhost","New":1}""", Compact(_target.Read("appsettings.json")));
    }

    [Fact]
    public async Task A_conflict_left_unresolved_fails_the_step()
    {
        _target.File(".deploy/base/appsettings.json", """{ "Db": "base" }""");

        var (outcome, _) = await RunAsync(CopyApp);

        Assert.Equal(DeployResult.Failed, outcome.Result);
        Assert.Contains("needs a choice", outcome.Error!.Message);
        Assert.Equal("""{ "Db": "prod" }""", _target.Read("appsettings.json"));
    }

    [Fact]
    public async Task Identical_settings_do_not_open_the_review()
    {
        _target.File("appsettings.json", _package.Read("app/appsettings.json"));

        var (outcome, _) = await RunAsync(CopyApp);

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.Empty(_user.Reviews);
    }

    [Fact]
    public async Task A_config_the_server_lacks_is_shown_for_review_then_copied()
    {
        File.Delete(_target.Combine("appsettings.json"));

        await RunAsync(CopyApp);

        Assert.Equal(ConfigFileKind.New, Assert.Single(Assert.Single(_user.Reviews)).Kind);
        Assert.Equal(_package.Read("app/appsettings.json"), _target.Read("appsettings.json"));
    }

    [Fact]
    public async Task Server_overrides_are_reviewed_with_the_settings_they_override()
    {
        _target.File("appsettings.Production.json", """{ "Db": "prod-db" }""");

        await RunAsync(CopyApp);

        var files = Assert.Single(_user.Reviews);
        Assert.Equal(["appsettings.json", "appsettings.Production.json"], files.Select(f => f.FileName));
        Assert.Equal(ConfigFileKind.Override, files[1].Kind);
        Assert.Equal("""{ "Db": "prod-db" }""", _target.Read("appsettings.Production.json"));
    }

    [Fact]
    public async Task Cancelling_step_inputs_cancels_the_deploy()
    {
        _user.CancelInputs = true;

        var (outcome, _) = await RunAsync("""{ "type": "copyFiles", "from": "app", "to": "{X}", "inputs": { "X": {} } }""");

        Assert.Equal(DeployResult.Cancelled, outcome.Result);
    }

    [Fact]
    public async Task Package_paths_cannot_escape_the_package()
    {
        var (outcome, _) = await RunAsync("""{ "type": "copyFiles", "from": "../..", "to": "{Target}" }""");

        Assert.Equal(DeployResult.Failed, outcome.Result);
        Assert.Contains("outside the package", outcome.Error!.Message);
    }

    [Fact]
    public async Task The_whole_package_can_be_copied()
    {
        var (outcome, _) = await RunAsync("""{ "type": "copyFiles", "from": ".", "to": "{Target}/all" }""");

        Assert.Equal(DeployResult.Succeeded, outcome.Result);
        Assert.True(_target.Exists("all/app/app.dll"));
    }

    private static string Compact(string json) =>
        System.Text.Json.Nodes.JsonNode.Parse(json)!.ToJsonString();
}
