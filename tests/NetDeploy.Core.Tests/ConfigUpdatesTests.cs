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

    private static string Compact(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!.ToJsonString();
}
