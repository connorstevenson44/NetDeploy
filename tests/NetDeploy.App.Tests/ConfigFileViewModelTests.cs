using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core.Config;

namespace NetDeploy.App.Tests;

public class ConfigFileViewModelTests
{
    private const string Base = """{ "Db": "local", "Timeout": 30, "Smtp": { "Host": "localhost", "Port": 25 } }""";
    private const string Server = """{ "Db": "prod", "Timeout": 30, "Smtp": { "Host": "localhost", "Port": 25 } }""";
    private const string Package = """{ "Db": "local", "Timeout": 60, "Smtp": { "Host": "localhost", "Port": 587 }, "ApiKey": "" }""";

    private static ConfigFileViewModel Review(Action? changed = null)
    {
        var plan = ConfigMerger.Plan(Base, Server, Package, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        return new ConfigFileViewModel(new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, Package), changed ?? (() => { }));
    }

    private static SettingViewModel Setting(ConfigFileViewModel file, string path) =>
        file.Items.OfType<SettingViewModel>().Single(setting => setting.Path == path);

    [Fact]
    public void Each_setting_shows_the_server_value_and_the_value_to_write()
    {
        var file = Review();

        var db = Setting(file, "Db");
        Assert.Equal("prod", db.Now);
        Assert.Equal("prod", db.Value);
        Assert.Equal(SettingStatus.Kept, db.Status);

        var timeout = Setting(file, "Timeout");
        Assert.Equal("30", timeout.Now);
        Assert.Equal("60", timeout.Value);
        Assert.Equal(SettingStatus.Changed, timeout.Status);
    }

    [Fact]
    public void Unchanged_settings_are_hidden_and_each_section_shows_once()
    {
        var file = Review();

        Assert.DoesNotContain(file.Items.OfType<SettingViewModel>(), setting => setting.Path == "Smtp:Host");
        Assert.Single(file.Items.OfType<SectionViewModel>(), section => section.Name == "Smtp");

        file.ShowUnchanged(true);
        Assert.Contains(file.Items.OfType<SettingViewModel>(), setting => setting.Path == "Smtp:Host");
        Assert.Single(file.Items.OfType<SectionViewModel>(), section => section.Name == "Smtp");
    }

    [Fact]
    public void Typing_a_value_from_either_side_picks_that_side_and_anything_else_is_an_edit()
    {
        var file = Review();
        var timeout = Setting(file, "Timeout");

        timeout.Value = "30";
        Assert.Equal(SettingStatus.Kept, timeout.Status);

        timeout.Value = "45";
        Assert.Equal(SettingStatus.Edited, timeout.Status);

        timeout.ResetCommand.Execute(null);
        Assert.Equal("60", timeout.Value);
        Assert.Equal(SettingStatus.Changed, timeout.Status);
    }

    [Fact]
    public void A_setting_that_needs_a_value_blocks_until_one_is_given()
    {
        var plan = ConfigMerger.Plan(Base, Server, Package, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        plan.Rows.Single(row => row.Path == "ApiKey").Choice = null;
        var review = new MergeViewModel([new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, Package)], isReadOnly: false);
        var apiKey = Setting(review.Files[0], "ApiKey");

        Assert.Equal(SettingStatus.NeedsValue, apiKey.Status);
        Assert.False(review.CanApply);

        apiKey.Value = "k-123";

        Assert.Equal(SettingStatus.Edited, apiKey.Status);
        Assert.True(review.CanApply);
        Assert.Contains("\"ApiKey\": \"k-123\"", plan.BuildResult());
    }

    [Fact]
    public void A_conflict_can_take_either_side()
    {
        var plan = ConfigMerger.Plan(Base, Server, Package.Replace("\"local\"", "\"test\""), ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        var file = new ConfigFileViewModel(new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, Package), () => { });
        var db = Setting(file, "Db");

        Assert.Equal(SettingStatus.NeedsValue, db.Status);
        Assert.Equal("", db.Value);
        Assert.True(db.CanKeepNow);
        Assert.True(db.CanReset);

        db.KeepNowCommand.Execute(null);
        Assert.Equal("prod", db.Value);

        db.ResetCommand.Execute(null);
        Assert.Equal("test", db.Value);
    }

    [Fact]
    public void An_override_value_the_new_settings_still_have_is_unchanged()
    {
        var plan = ConfigMerger.Plan(null, Server, Server, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        var row = new MergeRow("Db", null, "\"prod\"", "\"prod\"", MergeChoice.KeepCurrent, "Overrides appsettings.json");
        var setting = new SettingViewModel(row, plan.Format, () => { });

        Assert.Equal(SettingStatus.Unchanged, setting.Status);
        Assert.False(setting.IsNotable);
        Assert.False(setting.CanReset);
    }

    [Fact]
    public void Removing_and_restoring_a_setting()
    {
        var file = Review();
        var db = Setting(file, "Db");

        db.RemoveCommand.Execute(null);
        Assert.True(db.IsRemoved);
        Assert.Equal(SettingStatus.Removed, db.Status);

        db.KeepNowCommand.Execute(null);
        Assert.False(db.IsRemoved);
        Assert.Equal("prod", db.Value);
    }

    [Fact]
    public void The_editor_starts_unchanged_and_undo_returns_to_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + "-appsettings.json");
        File.WriteAllText(path, Server);
        try
        {
            var update = ConfigUpdates.Open(path);
            var file = new ConfigFileViewModel(update, () => { });
            file.ShowUnchanged(true);
            var db = Setting(file, "Db");

            db.Value = "staging";
            Assert.Equal(SettingStatus.Edited, db.Status);
            Assert.True(update.Plan.ChangesStartFile);

            db.ResetCommand.Execute(null);
            Assert.Equal("prod", db.Value);
            Assert.False(update.Plan.ChangesStartFile);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
