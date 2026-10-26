using NetDeploy.Core.Manifest;
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
        Assert.Contains(typeof(RunExeStep), types);
        Assert.Contains(typeof(ScheduledTaskStep), types);
        Assert.Contains(typeof(IfStep), types);
        Assert.Contains(typeof(PowerShellStep), manifest.Steps.OfType<IfStep>().SelectMany(s => s.Then).Select(s => s.GetType()));
    }

    [Fact]
    public void A_package_with_settings_this_version_does_not_know_opens_without_them()
    {
        var json = """
            {
              "schemaVersion": 1,
              "netdeployVersion": "0.9.0",
              "notes": "from a later editor",
              "app": { "name": "Orders", "version": "1.0.0" },
              "steps": [
                { "type": "checkPrerequisite", "check": { "kind": "fileExists", "path": "C:\\x", "retries": 2 } },
                { "type": "copyFiles", "from": "app", "to": "C:\\Apps", "verify": true }
              ]
            }
            """;
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(json));

        var (manifest, warnings) = ManifestLoader.ParseLeniently(json);

        Assert.Equal(2, manifest.Steps.Count);
        Assert.StartsWith("This deploy.json was saved by NetDeploy 0.9.0; this is " + ManifestLoader.NetDeployVersion, warnings[0]);
        Assert.Equal(
            ["Left out notes", "Left out steps[0].check.retries", "Left out steps[1].verify"],
            warnings.Skip(1).Select(warning => warning[..warning.IndexOf(':')]).Order());
        Assert.Throws<ManifestException>(() => ManifestLoader.ParseLeniently(Manifest("""{ "type": "copyFiles", "from": "app" }""")));
    }

    [Fact]
    public void A_file_from_a_newer_netdeploy_says_so()
    {
        Assert.Null(ManifestLoader.VersionWarning(null));
        Assert.Null(ManifestLoader.VersionWarning(ManifestLoader.NetDeployVersion));
        Assert.Contains("newer than this one", ManifestLoader.VersionWarning("99.0.0"));
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
