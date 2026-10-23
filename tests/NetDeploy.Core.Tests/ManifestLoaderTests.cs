using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Tests;

public class ManifestLoaderTests
{
    private static string Manifest(string steps) => $$"""
        {
          "schemaVersion": 1,
          "app": { "name": "Orders", "version": "1.0.0" },
          "steps": [ {{steps}} ]
        }
        """;

    [Fact]
    public void The_sample_manifest_loads_with_every_step_type()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "OrdersService", "deploy.json");

        var manifest = ManifestLoader.Parse(File.ReadAllText(path));

        Assert.Equal("OrdersService", manifest.App.Name);
        Assert.True(manifest.RequiresAdmin);
        Assert.Equal(InputType.Secret, manifest.Parameters["DbConnection"].Type);
        var types = manifest.Steps.Select(s => s.GetType()).ToHashSet();
        Assert.Contains(typeof(CheckPrerequisiteStep), types);
        Assert.Contains(typeof(ServiceStep), types);
        Assert.Contains(typeof(BackupStep), types);
        Assert.Contains(typeof(CopyFilesStep), types);
        Assert.Contains(typeof(MergeConfigStep), types);
        Assert.Contains(typeof(RunExeStep), types);
        Assert.Contains(typeof(ScheduledTaskStep), types);
        Assert.Contains(typeof(IfStep), types);
        Assert.Contains(typeof(PowerShellStep), manifest.Steps.OfType<IfStep>().SelectMany(s => s.Then).Select(s => s.GetType()));
    }

    [Fact]
    public void Type_does_not_have_to_come_first()
    {
        var manifest = ManifestLoader.Parse(Manifest("""{ "from": "app", "to": "C:\\Apps", "type": "copyFiles" }"""));

        Assert.IsType<CopyFilesStep>(Assert.Single(manifest.Steps));
    }

    [Fact]
    public void Conditions_and_inputs_load()
    {
        var manifest = ManifestLoader.Parse(Manifest("""
            { "type": "runExe", "path": "m.exe",
              "when": { "all": [ { "ask": "Migrate?" }, { "not": { "value": "{Env}", "equals": "Dev" } } ] },
              "inputs": { "Env": { "type": "choice", "options": ["Dev", "Prod"] } } }
            """));

        var step = Assert.Single(manifest.Steps);
        Assert.Equal("Ask: Migrate? and not ({Env} is Dev)", step.When!.Describe());
        Assert.Equal(InputType.Choice, step.Inputs!["Env"].Type);
    }

    [Theory]
    [InlineData("""{ "type": "copyFiles", "from": "app" }""", "missing required properties")]
    [InlineData("""{ "type": "copyFiles", "from": "app", "to": "x", "prserve": [] }""", "prserve")]
    [InlineData("""{ "type": "teleport" }""", "teleport")]
    [InlineData("""{ "type": "powershell" }""", "either 'file' or 'script'")]
    [InlineData("""{ "type": "runExe", "path": "a.exe", "when": { "ask": "x", "value": "y" } }""", "exactly one of")]
    [InlineData("""{ "type": "service", "action": "install", "service": "Orders" }""", "needs 'binary'")]
    [InlineData("""{ "type": "scheduledTask", "action": "createOrUpdate", "task": "T" }""", "'definition', or 'program' and 'schedule'")]
    [InlineData("""{ "type": "checkPrerequisite", "check": { "kind": "dotnetRuntime" } }""", "'framework' and 'minVersion'")]
    [InlineData("""{ "type": "if", "condition": { "ask": "x" }, "then": [ { "type": "powershell" } ] }""", "steps[0]: then[0]: A powershell step")]
    public void Problems_are_reported_clearly(string step, string expected)
    {
        var error = Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Manifest(step)));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void An_unsupported_schema_version_is_rejected()
    {
        var json = Manifest("""{ "type": "backup", "path": "x" }""").Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

        var error = Assert.Throws<ManifestException>(() => ManifestLoader.Parse(json));

        Assert.Contains("schemaVersion 2", error.Message);
    }
}
