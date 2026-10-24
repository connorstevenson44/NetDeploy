using System.IO;
using System.Text.Json.Nodes;
using NetDeploy.App.ViewModels;
using NetDeploy.Core;
using NetDeploy.Core.Editing;

namespace NetDeploy.App.Tests;

public sealed class EditorViewModelTests : IDisposable
{
    private static readonly string SampleConfig = Path.Combine(AppContext.BaseDirectory, "samples", "OrdersService", "deploy.json");

    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployEditorTest", Path.GetRandomFileName())).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static EditorViewModel OpenSample()
    {
        var editor = new EditorViewModel();
        editor.Open(SampleConfig);
        return editor;
    }

    private static EditorRowViewModel Row(EditorViewModel editor, string title) => editor.Rows.Single(row => row.Title == title);

    private static T Field<T>(EditorViewModel editor, string name) where T : EditorFieldViewModel =>
        (T)editor.Fields.Single(field => field.Field.Name == name);

    [Fact]
    public void The_outline_lists_the_steps_with_the_branches_of_an_if_under_it()
    {
        var editor = OpenSample();

        Assert.Equal(EditorRowKind.Settings, editor.Rows[0].Kind);
        Assert.Contains(editor.Rows, row => row.Title == "Migrate database");
        var then = editor.Rows.Single(row => row.Kind == EditorRowKind.Branch && row.Branch == "then");
        var nested = editor.Rows[editor.Rows.IndexOf(then) + 1];
        Assert.Equal("powershell", ManifestDocument.TypeOf(nested.Step!));
        Assert.True(nested.Indent.Left > then.Indent.Left);
        Assert.False(editor.IsDirty);
        Assert.False(editor.HasProblems);
    }

    [Fact]
    public void Editing_a_field_changes_the_config_and_its_title()
    {
        var editor = OpenSample();
        editor.Selected = Row(editor, "Migrate database");

        Field<ValueFieldViewModel>(editor, "name").Text = "Upgrade the database";
        Field<ValueFieldViewModel>(editor, "timeoutMinutes").Text = "20";
        Field<ConditionFieldViewModel>(editor, "when").Mode = ConditionMode.Always;

        var step = JsonNode.Parse(editor.Json)!["steps"]!.AsArray().Single(step => (string?)step!["name"] == "Upgrade the database")!;
        Assert.Equal(20, (int)step["timeoutMinutes"]!);
        Assert.Null(step["when"]);
        Assert.Equal("Upgrade the database", editor.Selected!.Title);
        Assert.True(editor.IsDirty);
        Assert.EndsWith(" •", editor.Title);
    }

    [Fact]
    public void A_check_shows_only_the_settings_for_its_kind()
    {
        var editor = OpenSample();
        editor.Selected = Row(editor, ".NET 10 runtime");
        var check = Field<GroupFieldViewModel>(editor, "check");
        EditorFieldViewModel Child(string name) => check.Fields.Single(field => field.Field.Name == name);
        var kind = (ValueFieldViewModel)Child("kind");

        Assert.Contains(kind.Options, option => option is { Value: "fileExists", Label: "File exists" });
        Assert.True(Child("framework").IsShown);
        Assert.False(Child("path").IsShown);

        kind.Text = "fileExists";

        Assert.False(Child("framework").IsShown);
        Assert.True(Child("path").IsShown);
        var saved = JsonNode.Parse(editor.Json)!["steps"]![0]!["check"]!;
        Assert.Equal("""{"kind":"fileExists"}""", saved.ToJsonString());
    }

    [Fact]
    public void Cleanup_shows_what_is_undone_by_itself_and_takes_powershell()
    {
        var editor = OpenSample();
        editor.Selected = Row(editor, "Install service OrdersService{InstanceSuffix}");
        var cleanup = Field<CleanupFieldViewModel>(editor, "cleanup");
        Assert.Contains("sc.exe delete OrdersService{InstanceSuffix}", cleanup.Automatic);

        cleanup.Script = "Remove-Item 'D:\\Data\\{Instance}' -Recurse";
        Field<ValueFieldViewModel>(editor, "action").Text = "start";

        var step = JsonNode.Parse(editor.Json)!["steps"]!.AsArray().Single(step => (string?)step!["action"] == "start" && step["binary"] is not null)!;
        Assert.Equal(@"Remove-Item 'D:\Data\{Instance}' -Recurse", (string?)step["cleanup"]);
        Assert.Equal("Nothing is undone by itself for this step.", cleanup.Automatic);
        Assert.False(editor.HasProblems);
    }

    [Fact]
    public void Steps_are_added_after_the_selection_or_into_a_branch_and_can_be_moved_and_deleted()
    {
        var editor = OpenSample();
        var backup = ManifestSchema.For("backup")!;
        editor.Selected = Row(editor, "Back up {InstanceDir}");
        var existing = editor.Selected.Step!;

        editor.AddStepCommand.Execute(backup);
        var added = editor.Selected!.Step!;
        var steps = (JsonArray)existing.Parent!;
        Assert.Equal(steps.IndexOf(existing) + 1, steps.IndexOf(added));

        editor.MoveUpCommand.Execute(null);
        Assert.Equal(steps.IndexOf(existing) - 1, steps.IndexOf(added));
        editor.DeleteCommand.Execute(null);
        Assert.DoesNotContain(added, steps);
        Assert.Same(existing, editor.Selected!.Step);

        editor.Selected = editor.Rows.Single(row => row.Kind == EditorRowKind.Branch && row.Branch == "else");
        editor.AddStepCommand.Execute(backup);
        Assert.Equal("else", editor.Selected!.Step!.Parent!.GetPropertyName());
        Assert.False(editor.MoveUpCommand.CanExecute(null));
    }

    [Fact]
    public void A_problem_marks_its_step_and_picking_it_selects_the_step()
    {
        var editor = OpenSample();
        editor.Selected = Row(editor, "Migrate database");
        var migrate = editor.Selected;

        Field<ValueFieldViewModel>(editor, "path").Text = "";

        var problem = Assert.Single(editor.Problems);
        Assert.Contains("path", problem.Message);
        Assert.True(migrate.HasProblem);
        editor.Selected = editor.Rows[0];
        editor.SelectProblemCommand.Execute(problem);
        Assert.Same(migrate, editor.Selected);
    }

    [Fact]
    public void Saving_keeps_what_the_form_does_not_show()
    {
        var editor = OpenSample();
        editor.AppVersion = "2.5.0";
        var path = Path.Combine(_folder, "deploy.json");

        editor.Save(path);

        var expected = JsonNode.Parse(File.ReadAllText(SampleConfig))!;
        expected["app"]!["version"] = "2.5.0";
        expected.AsObject().Insert(2, "netdeployVersion", ManifestLoader.NetDeployVersion);
        Assert.Equal(expected.ToJsonString(), JsonNode.Parse(File.ReadAllText(path))!.ToJsonString());
        Assert.Equal(path, editor.FilePath);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void A_new_config_is_ready_to_use_and_parameters_are_edited_as_rows()
    {
        var editor = new EditorViewModel();
        editor.New();

        Assert.False(editor.HasProblems);
        Assert.Null(editor.FilePath);
        editor.Parameters!.AddCommand.Execute(null);
        var row = editor.Parameters.Rows.Last();
        row.Name = "InstallDir";
        Assert.Equal("Two values have this name.", row.Problem);
        row.Name = "Environment";
        row.Type = "choice";
        row.Options = "Production, Staging";

        var parameter = JsonNode.Parse(editor.Json)!["parameters"]!["Environment"]!;
        Assert.Equal("choice", (string?)parameter["type"]);
        Assert.Equal(["Production", "Staging"], parameter["options"]!.AsArray().Select(option => (string?)option));
    }
}
