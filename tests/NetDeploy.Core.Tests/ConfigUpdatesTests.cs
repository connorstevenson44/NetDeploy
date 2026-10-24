using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

public class ConfigUpdatesTests
{
    private static MergeRow Row(MergePlan plan, string path) => plan.Rows.Single(row => row.Path == path);

    [Fact]
    public void An_override_value_follows_a_renamed_setting()
    {
        var plan = ConfigUpdates.PlanOverride(
            overrideText: """{ "Smtp": { "Server": "mail.prod" } }""",
            oldBaseText: """{ "Smtp": { "Server": "localhost", "Port": 25 } }""",
            newBaseText: """{ "Smtp": { "Host": "localhost", "Port": 25 } }""");

        Assert.Equal(MergeChoice.Remove, Row(plan, "Smtp:Server").Choice);
        Assert.Equal(MergeChoice.TakeIncoming, Row(plan, "Smtp:Host").Choice);
        Assert.Equal("""{"Smtp":{"Host":"mail.prod"}}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void An_override_of_a_removed_setting_is_removed()
    {
        var plan = ConfigUpdates.PlanOverride(
            """{ "Legacy": true, "Db": "prod" }""",
            oldBaseText: """{ "Legacy": false, "Db": "local" }""",
            newBaseText: """{ "Db": "local" }""");

        Assert.Equal(MergeChoice.Remove, Row(plan, "Legacy").Choice);
        Assert.False(Row(plan, "Db").IsChanged);
        Assert.Equal("""{"Db":"prod"}""", Compact(plan.BuildResult()));
    }

    [Fact]
    public void An_override_value_where_the_settings_now_have_a_section_needs_a_choice()
    {
        var plan = ConfigUpdates.PlanOverride(
            """{ "Cache": "redis:6379" }""",
            oldBaseText: """{ "Cache": "memory" }""",
            newBaseText: """{ "Cache": { "Kind": "memory" } }""");

        Assert.Null(Row(plan, "Cache").Choice);
    }

    [Fact]
    public void A_setting_only_in_the_override_is_kept_but_shown()
    {
        var plan = ConfigUpdates.PlanOverride("""{ "ConectionStrings": { "Db": "x" } }""", oldBaseText: null, newBaseText: "{}");

        var row = Row(plan, "ConectionStrings:Db");
        Assert.Equal(MergeChoice.KeepCurrent, row.Choice);
        Assert.True(row.IsChanged);
        Assert.False(plan.ChangesStartFile);
    }

    [Theory]
    [InlineData("", true, true)]
    [InlineData("", false, false)]
    [InlineData("CHANGE_ME", false, true)]
    [InlineData("<your api key>", false, true)]
    [InlineData("__API_KEY__", false, true)]
    [InlineData("Server=sql01", true, false)]
    public void Placeholders_are_recognised(string value, bool isNew, bool expected) =>
        Assert.Equal(expected, ConfigUpdates.IsPlaceholder(value, isNew));

    [Fact]
    public void A_new_setting_with_a_placeholder_needs_a_value()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("appsettings.json", """{ "Db": "local", "ApiKey": "" }""");
        target.File("appsettings.json", """{ "Db": "prod" }""");

        var update = ConfigUpdates.Plan(package.Path, target.Path, ["appsettings.json"], new MergeOptions([])).Single();

        Assert.Null(Row(update.Plan, "ApiKey").Choice);
        Assert.Equal("Needs a value for this server", Row(update.Plan, "ApiKey").Reason);
    }

    [Fact]
    public void Every_setting_in_a_new_file_is_added()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("appsettings.json", """{ "Db": "local", "Smtp": { "Port": 25 } }""");

        var update = ConfigUpdates.Plan(package.Path, target.Path, ["appsettings.json"], new MergeOptions([])).Single();

        Assert.Equal(ConfigFileKind.New, update.Kind);
        Assert.All(update.Plan.Rows, row => Assert.Null(row.Current));
        Assert.All(update.Plan.Rows, row => Assert.True(row.IsChanged));
        Assert.False(update.Plan.ChangesStartFile);
    }

    [Fact]
    public void Variables_in_the_package_are_filled_from_the_deploy_settings_or_their_default()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("appsettings.json", """{ "Db": "{{DbConnection|Server=localhost}}", "Region": "{{Region|East}}", "Key": "{{ApiKey}}" }""");
        package.File("Orders.exe.config", """<configuration><appSettings><add key="Db" value="{{DbConnection}}" /></appSettings></configuration>""");

        var updates = ConfigUpdates.Plan(package.Path, target.Path, ["appsettings.json", "Orders.exe.config"], new MergeOptions([]),
            new Dictionary<string, string> { ["DbConnection"] = "Server=sql01" });
        var json = updates[0].Plan;

        // The default stands in for the value before, so the review shows the change to the deploy's value.
        Assert.Equal("\"Server=localhost\"", Row(json, "Db").Current);
        Assert.Equal("\"Server=sql01\"", Row(json, "Db").Result);
        Assert.Equal("Filled from the deploy setting DbConnection", Row(json, "Db").Reason);
        Assert.Equal("\"East\"", Row(json, "Region").Result);
        Assert.Null(Row(json, "Key").Choice);
        Assert.Contains("Server=sql01", Row(updates[1].Plan, "appSettings:Db").Result);

        Row(json, "Key").Choice = MergeChoice.Remove;
        Assert.Equal("""{"Db":"Server=sql01","Region":"East"}""", Compact(json.BuildResult()));
    }

    [Fact]
    public void A_server_value_wins_over_a_variable_the_package_already_had()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("appsettings.json", """{ "Db": "{{DbConnection|Server=localhost}}" }""");
        target.File("appsettings.json", """{ "Db": "Server=prod" }""");
        target.File(".deploy/base/appsettings.json", """{ "Db": "{{DbConnection|Server=localhost}}" }""");

        var update = ConfigUpdates.Plan(package.Path, target.Path, ["appsettings.json"], new MergeOptions([]),
            new Dictionary<string, string> { ["DbConnection"] = "Server=sql01" }).Single();

        Assert.Equal(MergeChoice.KeepCurrent, Row(update.Plan, "Db").Choice);
    }

    [Fact]
    public void A_placeholder_set_by_an_override_needs_nothing()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("appsettings.json", """{ "ApiKey": "CHANGE_ME" }""");
        target.File("appsettings.json", """{ "ApiKey": "CHANGE_ME" }""");
        target.File("appsettings.Production.json", """{ "ApiKey": "k-123" }""");

        var updates = ConfigUpdates.Plan(package.Path, target.Path, ["appsettings.json"], new MergeOptions([]));

        Assert.Equal(MergeChoice.TakeIncoming, Row(updates[0].Plan, "ApiKey").Choice);
        Assert.Equal(ConfigFileKind.Override, updates[1].Kind);
        Assert.DoesNotContain(updates, update => update.NeedsReview);
    }

    [Fact]
    public void An_override_the_package_ships_is_merged_not_checked()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("appsettings.json", "{}");
        package.File("appsettings.Production.json", """{ "Db": "prod" }""");
        target.File("appsettings.json", "{}");
        target.File("appsettings.Production.json", """{ "Db": "prod" }""");

        var updates = ConfigUpdates.Plan(package.Path, target.Path, ["appsettings.json", "appsettings.Production.json"], new MergeOptions([]));

        Assert.All(updates, update => Assert.Equal(ConfigFileKind.Update, update.Kind));
    }

    [Fact]
    public void An_opened_file_is_only_rewritten_when_a_setting_changes()
    {
        using var folder = new TempFolder();
        var path = folder.File("appsettings.json", """{ "Db": "prod",   "Timeout": 30 }""");

        var unchanged = ConfigUpdates.Open(path);
        ConfigUpdates.Write([unchanged]);
        Assert.Equal("""{ "Db": "prod",   "Timeout": 30 }""", File.ReadAllText(path));

        var edited = ConfigUpdates.Open(path);
        var db = Row(edited.Plan, "Db");
        db.EditedValue = "\"staging\"";
        db.Choice = MergeChoice.Edit;
        ConfigUpdates.Write([edited]);

        Assert.Equal("""{"Db":"staging","Timeout":30}""", Compact(File.ReadAllText(path)));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, ConfigUpdates.StateFolder)));
    }

    private static string Compact(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!.ToJsonString();
}
