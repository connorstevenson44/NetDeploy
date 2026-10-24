using System.Text.Json.Nodes;
using NetDeploy.Core.Editing;

namespace NetDeploy.Core.Tests;

public class EditingTests
{
    [Fact]
    public void The_schema_gives_each_step_type_a_form_with_the_name_first()
    {
        var copy = ManifestSchema.For("copyFiles")!;
        var kinds = copy.Fields.ToDictionary(field => field.Name, field => field.Kind);

        Assert.Equal(9, ManifestSchema.Steps.Count);
        Assert.Equal("name", copy.Fields[0].Name);
        Assert.Equal(FieldKind.Text, kinds["from"]);
        Assert.Equal(FieldKind.YesNo, kinds["deleteExtra"]);
        Assert.Equal(FieldKind.Lines, kinds["preserve"]);
        Assert.Equal(FieldKind.Condition, kinds["when"]);
        Assert.Equal(FieldKind.Inputs, kinds["inputs"]);
        Assert.Equal(FieldKind.Cleanup, kinds["cleanup"]);
        Assert.DoesNotContain("type", kinds.Keys);
        Assert.True(copy.Fields.Single(field => field.Name == "to").Required);
        Assert.Contains("merged", copy.Description);

        var check = ManifestSchema.For("checkPrerequisite")!.Fields.Single(field => field.Name == "check");
        Assert.Equal(FieldKind.Group, check.Kind);
        Assert.Equal(["dotnetRuntime", "installedProgram", "registryValue", "fileExists", "powershell"], check.Fields[0].Options);
        var path = check.Fields.Single(field => field.Name == "path");
        Assert.Equal(["fileExists"], path.Kinds);
        Assert.Equal("A file or folder.", path.Description);
        Assert.Equal(["dotnetRuntime", "installedProgram"], check.Fields.Single(field => field.Name == "minVersion").Kinds);
        Assert.True(check.Fields[0].AppliesTo("fileExists"));
        Assert.Equal("File exists", ManifestSchema.Label("fileExists"));
        Assert.Equal("Timeout minutes", ManifestSchema.Label("timeoutMinutes"));
        Assert.Equal("Run only when", ManifestSchema.For("if")!.Fields.Single(field => field.Name == "when").Label);
        Assert.DoesNotContain(ManifestSchema.For("if")!.Fields, field => field.Name is "then" or "else");
    }

    [Fact]
    public void Every_new_step_reads_as_its_step_type()
    {
        foreach (var schema in ManifestSchema.Steps)
        {
            var step = schema.NewStep();
            Assert.Equal(schema.Type, ManifestDocument.TypeOf(step));
            Assert.NotEqual("Unknown step", ManifestDocument.TitleOf(step));
        }
        Assert.Equal("install", ManifestSchema.For("service")!.NewStep()["action"]!.GetValue<string>());
        Assert.Equal("dotnetRuntime", ManifestSchema.For("checkPrerequisite")!.NewStep()["check"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void A_new_config_is_ready_to_deploy()
    {
        var document = ManifestDocument.New();

        Assert.Empty(document.Problems());
        Assert.Equal("Copy app to {InstanceDir}", ManifestDocument.TitleOf(document.Steps[0]!.AsObject()));
    }

    [Fact]
    public void Saving_keeps_everything_the_editor_does_not_show()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/OrdersService/deploy.json"));
        var document = ManifestDocument.Parse(json);

        var saved = document.ToJson();

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(saved)));
        Assert.StartsWith("{\n  \"$schema\"", saved.ReplaceLineEndings("\n"));
        Assert.Contains(@"C:\\Apps\\OrdersService", saved);
        Assert.Contains("\"Run the database migration now?\"", saved);
    }

    [Fact]
    public void A_problem_points_at_its_step_even_inside_an_if()
    {
        var document = ManifestDocument.New();
        var backup = ManifestSchema.For("backup")!.NewStep();
        var broken = new JsonObject { ["type"] = "runExe" };
        document.Steps.Add(new JsonObject
        {
            ["type"] = "if",
            ["condition"] = new JsonObject { ["ask"] = "Go on?" },
            ["then"] = new JsonArray(backup, broken),
        });

        var problem = Assert.Single(document.Problems());
        Assert.Same(broken, problem.Step);
        Assert.Equal("Fill in 'path'.", problem.Message);
        Assert.Equal("Run an exe", ManifestDocument.TitleOf(broken));

        broken["path"] = "tool.exe";
        backup["keep"] = 0;
        problem = Assert.Single(document.Problems());
        Assert.Same(backup, problem.Step);
        Assert.Equal("'keep' must be at least 1.", problem.Message);

        document.Steps.Clear();
        problem = Assert.Single(document.Problems());
        Assert.Null(problem.Step);
    }

    [Fact]
    public void A_step_is_titled_by_its_name_then_by_its_settings()
    {
        var step = new JsonObject { ["type"] = "service", ["action"] = "start", ["service"] = "Orders" };

        Assert.Equal("Start service Orders", ManifestDocument.TitleOf(step));
        step["name"] = "Start it";
        Assert.Equal("Start it", ManifestDocument.TitleOf(step));
    }

    [Fact]
    public void A_file_that_is_not_a_json_object_is_a_problem()
    {
        Assert.Throws<ManifestException>(() => ManifestDocument.Parse("[1, 2]"));
        Assert.Throws<ManifestException>(() => ManifestDocument.Parse("{ \"app\": "));
    }
}
