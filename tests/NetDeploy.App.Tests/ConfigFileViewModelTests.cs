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
        Assert.Single(file.Items.OfType<SectionLine>(), section => section.Name == "Smtp" && !section.IsClosing);
        Assert.Contains(file.Items, line => line is GapLine);

        file.ShowUnchanged(true);
        Assert.Contains(file.Items.OfType<SettingViewModel>(), setting => setting.Path == "Smtp:Host");
        Assert.Single(file.Items.OfType<SectionLine>(), section => section.Name == "Smtp" && !section.IsClosing);
        Assert.DoesNotContain(file.Items, line => line is GapLine);
    }

    [Fact]
    public void Both_sides_read_like_the_real_files_with_numbers_and_commas()
    {
        var file = Review();
        file.ShowUnchanged(true);

        // Left, the server:  { "Db": "prod", "Timeout": 30, "Smtp": { "Host": ..., "Port": 25 } }
        // Right, the result: the same with Timeout 60, Port 587 and a new ApiKey at the end.
        string Left(DiffLine line) => line switch
        {
            SectionLine section => section.Text + section.LeftSeparator,
            SettingViewModel setting => setting.NowLine + setting.LeftSeparator,
            _ => "",
        };
        var left = file.Items.Where(line => line.IsOnLeft).Select(Left).ToList();
        Assert.Equal(["{", "\"Db\": \"prod\",", "\"Timeout\": 30,", "\"Smtp\": {", "\"Host\": \"localhost\",", "\"Port\": 25", "}", "}"], left);
        Assert.Equal(Enumerable.Range(1, 8).Cast<int?>(), file.Items.Where(line => line.IsOnLeft).Select(line => line.LeftNumber));

        var apiKey = Setting(file, "ApiKey");
        Assert.False(apiKey.IsOnLeft);
        Assert.Equal(8, apiKey.RightNumber);
        Assert.Equal("", apiKey.RightSeparator);
        Assert.Equal(",", file.Items.OfType<SectionLine>().Single(section => section.Name == "Smtp" && section.IsClosing).RightSeparator);
        Assert.Equal(("\"ApiKey\": \"", "\""), (apiKey.Before, apiKey.After));
    }

    [Fact]
    public void An_exe_config_shows_its_xml_lines_without_commas()
    {
        const string server = """<configuration><appSettings><add key="Region" value="East" /></appSettings></configuration>""";
        const string package = """<configuration><appSettings><add key="Region" value="West" /><add key="Retry" value="3" /></appSettings></configuration>""";
        var plan = ConfigMerger.Plan(null, server, package, ConfigFormats.For("Orders.exe.config"), new MergeOptions([]));
        var file = new ConfigFileViewModel(new ConfigFileUpdate(@"C:\App\Orders.exe.config", ConfigFileKind.Update, plan, package), () => { });
        file.ShowUnchanged(true);

        Assert.Equal(["<configuration>", "<appSettings>", "</appSettings>", "</configuration>"],
            file.Items.OfType<SectionLine>().Select(section => section.Text + section.RightSeparator));
        var retry = Setting(file, "appSettings:Retry");
        Assert.Equal(("<add key=\"Retry\" value=\"", "\" />"), (retry.Before, retry.After));
        Assert.Equal("3", retry.Value);
        Assert.Equal("", Setting(file, "appSettings:Region").RightSeparator);
    }

    [Fact]
    public void Removing_a_setting_takes_its_line_off_the_right_and_renumbers()
    {
        var file = Review();
        file.ShowUnchanged(true);
        var apiKey = Setting(file, "ApiKey");
        var smtpClose = file.Items.OfType<SectionLine>().Single(section => section.Name == "Smtp" && section.IsClosing);

        apiKey.RemoveCommand.Execute(null);

        Assert.False(apiKey.IsOnRight);
        Assert.Equal("", smtpClose.RightSeparator);
        Assert.Equal(8, file.Items.OfType<SectionLine>().Single(section => section.Name is null && section.IsClosing).RightNumber);
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

    [Fact]
    public void The_whole_file_can_be_edited_as_text_and_the_diff_shows_what_it_changes()
    {
        var changes = 0;
        var file = Review(() => changes++);

        file.EditAsTextCommand.Execute(null);
        Assert.True(file.IsTextMode);
        Assert.Equal(Server, file.NowText);
        Assert.Contains("\"Timeout\": 60", file.Text);

        // An array and a new section, which the lines can't add
        file.Text = file.Text.TrimEnd().TrimEnd('}') + ", \"Hosts\": [\"a\", \"b\"], \"Alerts\": { \"Email\": \"ops@x\" } }";
        Assert.Null(file.TextError);
        Assert.True(changes > 0);

        file.ShowDiffCommand.Execute(null);
        Assert.False(file.IsTextMode);
        Assert.Equal("[\"a\",\"b\"]", Setting(file, "Hosts").Value);
        Assert.Equal(SettingStatus.Edited, Setting(file, "Alerts:Email").Status);
        Assert.Single(file.Items.OfType<SectionLine>(), section => section.Name == "Alerts" && !section.IsClosing);
    }

    [Fact]
    public void Text_that_cant_be_read_says_why_and_blocks_writing_the_files()
    {
        var plan = ConfigMerger.Plan(Base, Server, Package, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        var review = new MergeViewModel([new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, Package)], isReadOnly: false);
        var file = review.Files[0];
        Setting(file, "ApiKey").Value = "k-1";
        file.EditAsTextCommand.Execute(null);

        file.Text = "{ \"Db\": ";

        Assert.True(file.HasTextError);
        Assert.False(review.CanApply);

        file.DropTextCommand.Execute(null);
        Assert.False(file.HasTextError);
        Assert.True(review.CanApply);
        Assert.Equal("k-1", Setting(file, "ApiKey").Value);
    }

    [Fact]
    public void Dropping_the_text_puts_the_settings_back()
    {
        var file = Review();
        file.EditAsTextCommand.Execute(null);
        file.Text = "{ \"Db\": \"other\" }";
        Assert.Equal(SettingStatus.Removed, Setting(file, "Timeout").Status);

        file.DropTextCommand.Execute(null);

        Assert.False(file.IsTextMode);
        Assert.Equal("prod", Setting(file, "Db").Value);
        Assert.Equal(SettingStatus.Changed, Setting(file, "Timeout").Status);
    }
}
