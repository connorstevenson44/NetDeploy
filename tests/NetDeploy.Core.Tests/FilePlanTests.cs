using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Tests;

public sealed class FilePlanTests : IDisposable
{
    private readonly TempFolder _package = new();
    private readonly TempFolder _target = new();
    private readonly FakeUser _user = new();

    public FilePlanTests()
    {
        _package.File("app/app.dll", "v2");
        _package.File("app/same.dll", "same");
        _package.File("app/new.dll", "new");
        _package.File("app/appsettings.json", """{ "Db": "local" }""");
        _package.File("app/data/seed.db", "seed");
        _target.File("app.dll", "v1");
        _target.File("same.dll", "same");
        _target.File("stale.dll", "old");
        _target.File("appsettings.json", """{ "Db": "prod" }""");
        _target.File("data/seed.db", "live data");
        _target.File(".deploy/base/appsettings.json", """{ "Db": "local" }""");
    }

    public void Dispose()
    {
        _package.Dispose();
        _target.Dispose();
    }

    private static CopyFilesStep Step(bool deleteExtra = false) =>
        new() { From = "app", To = "{Target}", Preserve = ["data/**"], DeleteExtra = deleteExtra };

    private Dictionary<string, string> Variables => new() { ["Target"] = _target.Path };

    private static FilePlanEntry Entry(FilePlan plan, string path) => plan.Entries.Single(entry => entry.Path == path);

    [Fact]
    public void The_plan_compares_every_file_and_picks_a_safe_default()
    {
        var plan = Step().PlanFiles(_package.Path, Variables);

        Assert.Equal((FileState.Changed, FileAction.Replace), (Entry(plan, "app.dll").State, Entry(plan, "app.dll").Action));
        Assert.Equal((FileState.Same, FileAction.Keep), (Entry(plan, "same.dll").State, Entry(plan, "same.dll").Action));
        Assert.Equal((FileState.New, FileAction.Replace), (Entry(plan, "new.dll").State, Entry(plan, "new.dll").Action));
        Assert.Equal((FileState.OnlyOnServer, FileAction.Keep), (Entry(plan, "stale.dll").State, Entry(plan, "stale.dll").Action));
        Assert.Equal(FileAction.Merge, Entry(plan, "appsettings.json").Action);
        Assert.Equal(FileAction.Keep, Entry(plan, "data/seed.db").Action);
        Assert.DoesNotContain(plan.Entries, entry => entry.Path.StartsWith(".deploy/", StringComparison.Ordinal));
    }

    [Fact]
    public void DeleteExtra_marks_server_only_files_for_deletion()
    {
        var plan = Step(deleteExtra: true).PlanFiles(_package.Path, Variables);

        Assert.Equal(FileAction.Delete, Entry(plan, "stale.dll").Action);
        Assert.Equal([FileAction.Keep, FileAction.Delete], Entry(plan, "stale.dll").Choices);
    }

    [Theory]
    [InlineData("{Missing}", "{Missing}, which is asked when the step runs")]
    [InlineData("", "Set the target folder first")]
    [InlineData("relative/folder", "isn't a full path")]
    public void A_target_that_is_not_known_yet_is_explained(string to, string message)
    {
        var step = new CopyFilesStep { From = "app", To = to };

        var error = Assert.Throws<InvalidOperationException>(() => step.PlanFiles(_package.Path, Variables));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task The_actions_picked_in_the_file_view_are_what_the_copy_does()
    {
        var step = Step();
        var options = new DeployOptions
        {
            FileActions = new Dictionary<DeployStep, IReadOnlyDictionary<string, FileAction>>
            {
                [step] = new Dictionary<string, FileAction>
                {
                    ["app.dll"] = FileAction.Keep,
                    ["new.dll"] = FileAction.Keep,
                    ["stale.dll"] = FileAction.Delete,
                    ["data/seed.db"] = FileAction.Replace,
                    ["appsettings.json"] = FileAction.Replace,
                    ["same.dll"] = FileAction.Merge, // not a choice for a plain file, so ignored
                },
            },
        };

        await step.RunAsync(Context(options), TestContext.Current.CancellationToken);

        Assert.Equal("v1", _target.Read("app.dll"));
        Assert.False(_target.Exists("new.dll"));
        Assert.False(_target.Exists("stale.dll"));
        Assert.Equal("seed", _target.Read("data/seed.db"));
        Assert.Equal("same", _target.Read("same.dll"));
        Assert.Equal("""{ "Db": "local" }""", _target.Read("appsettings.json"));
        Assert.Empty(_user.Reviews);
    }

    private StepContext Context(DeployOptions options)
    {
        var context = new StepContext(new DeployRunner(_user, new TestLog()))
        {
            PackageDirectory = _package.Path,
            DryRun = false,
            Log = new TestLog(),
            User = _user,
            Options = options,
        };
        context.Variables["Target"] = _target.Path;
        return context;
    }
}
