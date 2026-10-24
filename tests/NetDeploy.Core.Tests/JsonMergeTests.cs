using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

public class JsonMergeTests
{
    private static readonly IConfigFormat Json = ConfigFormats.For("appsettings.json");
    private static readonly MergeOptions Defaults = new([]);

    private static MergeRow Row(MergePlan plan, string path) => plan.Rows.Single(r => r.Path == path);

    [Fact]
    public void Without_a_base_server_values_win_and_the_package_decides_the_shape()
    {
        var current = """{ "Db": "prod", "Old": 1, "Same": true }""";
        var incoming = """{ "Db": "localhost", "New": 2, "Same": true }""";

        var plan = ConfigMerger.Plan(null, current, incoming, Json, Defaults);

        Assert.Equal(MergeChoice.KeepCurrent, Row(plan, "Db").Choice);
        Assert.Equal(MergeChoice.TakeIncoming, Row(plan, "New").Choice);
        Assert.Equal(MergeChoice.Remove, Row(plan, "Old").Choice);
        Assert.False(Row(plan, "Same").IsChanged);
        Assert.Equal(0, plan.ConflictCount);
        Assert.Equal("""{"Db":"prod","New":2,"Same":true}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void With_a_base_changed_defaults_are_taken_and_server_edits_are_kept()
    {
        var baseText = """{ "Timeout": 30, "Db": "localhost" }""";
        var current = """{ "Timeout": 30, "Db": "prod" }""";
        var incoming = """{ "Timeout": 60, "Db": "localhost" }""";

        var plan = ConfigMerger.Plan(baseText, current, incoming, Json, Defaults);

        Assert.Equal(MergeChoice.TakeIncoming, Row(plan, "Timeout").Choice);
        Assert.Equal("Changed in the package", Row(plan, "Timeout").Reason);
        Assert.Equal(MergeChoice.KeepCurrent, Row(plan, "Db").Choice);
        Assert.Equal("Changed on the server", Row(plan, "Db").Reason);
        Assert.Equal("""{"Timeout":60,"Db":"prod"}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void A_setting_changed_on_both_sides_is_a_conflict_until_the_user_chooses()
    {
        var plan = ConfigMerger.Plan("""{ "Port": 25 }""", """{ "Port": 2525 }""", """{ "Port": 587 }""", Json, Defaults);
        var port = Row(plan, "Port");

        Assert.True(port.IsConflict);
        Assert.Equal(1, plan.ConflictCount);
        Assert.Throws<InvalidOperationException>(() => plan.BuildResult());

        port.Choice = MergeChoice.KeepCurrent;
        Assert.Equal("""{"Port":2525}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void A_server_edit_reverting_to_the_package_value_is_not_a_conflict()
    {
        var plan = ConfigMerger.Plan("""{ "Port": 25 }""", """{ "Port": 587 }""", """{ "Port": 587 }""", Json, Defaults);

        Assert.False(Row(plan, "Port").IsChanged);
        Assert.Equal(0, plan.ConflictCount);
    }

    [Fact]
    public void TakeNew_makes_the_package_win_for_a_whole_section()
    {
        var current = """{ "Logging": { "LogLevel": { "Default": "Debug" } }, "Db": "prod" }""";
        var incoming = """{ "Logging": { "LogLevel": { "Default": "Warning" } }, "Db": "localhost" }""";

        var plan = ConfigMerger.Plan(null, current, incoming, Json, new MergeOptions(["Logging"]));

        Assert.Equal(MergeChoice.TakeIncoming, Row(plan, "Logging:LogLevel:Default").Choice);
        Assert.Equal(MergeChoice.KeepCurrent, Row(plan, "Db").Choice);
    }

    [Fact]
    public void Settings_the_package_dropped_are_removed_unless_kept()
    {
        var plan = ConfigMerger.Plan(null, """{ "Legacy": "x" }""", "{}", Json, Defaults);

        Assert.Equal(MergeChoice.Remove, Row(plan, "Legacy").Choice);
        Row(plan, "Legacy").Choice = MergeChoice.KeepCurrent;
        Assert.Equal("""{"Legacy":"x"}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void Arrays_are_one_value()
    {
        var plan = ConfigMerger.Plan(null, """{ "Hosts": ["a", "b"] }""", """{ "Hosts": ["a"] }""", Json, Defaults);

        var hosts = Assert.Single(plan.Rows);
        Assert.Equal("Hosts", hosts.Path);
        Assert.Equal(MergeChoice.KeepCurrent, hosts.Choice);
    }

    [Fact]
    public void Kept_settings_missing_from_the_package_are_added_with_their_parents()
    {
        var plan = ConfigMerger.Plan(null, """{ "Smtp": { "Host": "mail" } }""", "{}", Json, Defaults);
        Row(plan, "Smtp:Host").Choice = MergeChoice.KeepCurrent;

        Assert.Equal("""{"Smtp":{"Host":"mail"}}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void Keys_match_regardless_of_case_and_keep_the_package_casing()
    {
        var plan = ConfigMerger.Plan(null, """{ "connectionstrings": { "db": "prod" } }""", """{ "ConnectionStrings": { "Db": "local" } }""", Json, Defaults);

        Assert.Single(plan.Rows);
        Assert.Equal("""{"ConnectionStrings":{"Db":"prod"}}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void Comments_and_trailing_commas_are_accepted()
    {
        var plan = ConfigMerger.Plan(null, "{ // note\n \"A\": 1, }", "{ \"A\": 2 }", Json, Defaults);

        Assert.Equal(MergeChoice.KeepCurrent, Assert.Single(plan.Rows).Choice);
    }

    [Theory]
    [InlineData("\"old\"", "new text", "\"new text\"")]
    [InlineData("\"old\"", "42", "\"42\"")]
    [InlineData("10", "42", "42")]
    [InlineData("true", "false", "false")]
    [InlineData("10", "not a number", "\"not a number\"")]
    public void Edits_keep_the_original_kind_of_value(string original, string typed, string expected) =>
        Assert.Equal(expected, Json.ParseEdit(original, typed));

    [Fact]
    public void An_edited_value_is_written()
    {
        var plan = ConfigMerger.Plan(null, """{ "Name": "a" }""", """{ "Name": "b" }""", Json, Defaults);
        var row = Assert.Single(plan.Rows);

        row.Choice = MergeChoice.Edit;
        row.EditedValue = Json.ParseEdit(row.Current!, "c");

        Assert.Equal("""{"Name":"c"}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void Editing_the_whole_file_as_text_moves_every_setting_to_it_and_keeps_the_text_as_written()
    {
        var plan = ConfigMerger.Plan(null, """{ "Db": "prod", "Hosts": ["a"], "Old": 1 }""", """{ "Db": "local", "Hosts": ["a", "b"] }""", Json, Defaults);
        const string text = """
            {
              // Arrays and new sections, written by hand
              "Db": "local",
              "Hosts": ["a", "c"],
              "Old": 1,
              "Alerts": { "Emails": ["ops@x"] }
            }
            """;

        plan.Edit(text);

        Assert.Equal(MergeChoice.TakeIncoming, Row(plan, "Db").Choice);
        Assert.Equal(MergeChoice.Edit, Row(plan, "Hosts").Choice);
        Assert.Equal("""["a","c"]""", Row(plan, "Hosts").Result);
        Assert.Equal(MergeChoice.KeepCurrent, Row(plan, "Old").Choice);
        var added = Row(plan, "Alerts:Emails");
        Assert.Null(added.Current);
        Assert.Equal("""["ops@x"]""", added.Result);
        Assert.Equal(text, plan.BuildResult());
        Assert.Equal(text, plan.EditedText);
    }

    [Fact]
    public void A_setting_left_out_of_the_text_is_removed_and_a_later_change_applies_on_top_of_the_text()
    {
        var plan = ConfigMerger.Plan(null, """{ "Db": "prod", "Port": 25 }""", """{ "Db": "prod", "Port": 25 }""", Json, Defaults);

        plan.Edit("""{ "Db": "prod" }""");
        Assert.Equal(MergeChoice.Remove, Row(plan, "Port").Choice);

        Row(plan, "Db").Choice = MergeChoice.Edit;
        Row(plan, "Db").EditedValue = "\"test\"";
        Assert.Equal("""{"Db":"test"}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void A_setting_that_needs_a_value_still_needs_one_until_the_text_gives_it_one()
    {
        var plan = ConfigMerger.Plan("""{ "Port": 25 }""", """{ "Port": 2525 }""", """{ "Port": 587 }""", Json, Defaults);
        var text = plan.BuildText();
        Assert.Equal("""{"Port":587}""", Compact(text));

        plan.Edit(text);
        Assert.Null(Row(plan, "Port").Choice);

        plan.Edit("""{ "Port": 2525 }""");
        Assert.Equal(MergeChoice.KeepCurrent, Row(plan, "Port").Choice);
    }

    [Fact]
    public void Text_typed_and_taken_back_leaves_the_settings_as_they_were()
    {
        var plan = ConfigMerger.Plan("""{ "Port": 25 }""", """{ "Port": 2525 }""", """{ "Port": 587 }""", Json, Defaults);
        plan.StartTextEdit();
        var text = plan.BuildText();

        plan.Edit("""{ "Port": 5, "Extra": true }""");
        Assert.Equal(MergeChoice.Edit, Row(plan, "Port").Choice);
        Assert.Contains(plan.Rows, row => row.Path == "Extra");

        plan.Edit(text);
        Assert.Null(Row(plan, "Port").Choice);
        Assert.DoesNotContain(plan.Rows, row => row.Path == "Extra");
    }

    [Fact]
    public void Cancelling_a_text_edit_puts_back_the_settings_and_the_file()
    {
        var plan = ConfigMerger.Plan(null, """{ "Db": "prod" }""", """{ "Db": "local" }""", Json, Defaults);
        plan.StartTextEdit();
        plan.Edit("""{ "Db": "test", "Extra": 1 }""");

        plan.CancelTextEdit();

        Assert.Equal(MergeChoice.KeepCurrent, Row(plan, "Db").Choice);
        Assert.Single(plan.Rows);
        Assert.Null(plan.EditedText);
        Assert.Equal("""{"Db":"prod"}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void Text_that_isnt_json_is_refused_and_changes_nothing()
    {
        var plan = ConfigMerger.Plan(null, """{ "Db": "prod" }""", """{ "Db": "prod" }""", Json, Defaults);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => plan.Edit("""{ "Db": """));
        Assert.Null(plan.EditedText);
        Assert.Equal(MergeChoice.TakeIncoming, Row(plan, "Db").Choice);
    }

    [Fact]
    public void The_plan_remembers_the_servers_file()
    {
        const string current = """{ "Db": "prod" }""";
        Assert.Equal(current, ConfigMerger.Plan(null, current, """{ "Db": "local" }""", Json, Defaults).CurrentText);
        Assert.Null(ConfigMerger.PlanNew(current, Json).CurrentText);
    }

    [Fact]
    public void Display_shows_strings_without_quotes() =>
        Assert.Equal("Server=x;Database='y'", Json.Display("\"Server=x;Database='y'\""));

    [Fact]
    public void A_setting_line_splits_around_its_value_so_it_can_be_edited_in_place()
    {
        Assert.Equal(("\"Host\": \"", "\""), Json.Around("Host", "\"mail\""));
        Assert.Equal(("\"Port\": ", ""), Json.Around("Port", "25"));
        Assert.Equal(",", Json.Separator);
    }

    private static string Compact(string json) =>
        System.Text.Json.Nodes.JsonNode.Parse(json)!.ToJsonString();
}
