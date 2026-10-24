using System.IO;
using NetDeploy.App.ViewModels;
using NetDeploy.Core.Config;

namespace NetDeploy.App.Tests;

public class ConfigFileViewModelTests
{
    // The texts use \n whatever line endings the files were checked out with.
    private static readonly string Base = """
        {
          "Db": "local",
          "Timeout": 30,
          "Smtp": { "Host": "localhost", "Port": 25 }
        }
        """.ReplaceLineEndings("\n");

    private static readonly string Server = """
        {
          "Db": "prod",
          "Timeout": 30,
          "Smtp": { "Host": "localhost", "Port": 25 }
        }
        """.ReplaceLineEndings("\n");

    private static readonly string Package = """
        {
          "Db": "local",
          "Timeout": 60,
          "Smtp": { "Host": "localhost", "Port": 587 },
          "ApiKey": ""
        }
        """.ReplaceLineEndings("\n");

    private static ConfigFileViewModel Review(string? package = null, Action<MergePlan>? adjust = null, Action? changed = null)
    {
        package ??= Package;
        var plan = ConfigMerger.Plan(Base, Server, package, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        adjust?.Invoke(plan);
        return new ConfigFileViewModel(new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, package), changed ?? (() => { }));
    }

    // The text a mark covers, and the server's text it replaces.
    private static (string Text, string Removed) Of(ConfigFileViewModel file, ChangeMark mark) => (file.Text.Substring(mark.Start, mark.Length), mark.Removed);

    private static MarkAction Action(ChangeMark mark, string name) => mark.Actions.Single(action => action.Name == name);

    [Fact]
    public void The_text_is_the_package_file_with_the_servers_values_and_marks_what_changes_on_the_server()
    {
        var file = Review();

        Assert.Equal(Package.Replace("\"local\"", "\"prod\""), file.Text);
        var changes = file.Marks.Where(mark => mark.Kind == MarkKind.Change).Select(mark => Of(file, mark)).ToList();
        Assert.Equal([("60", "30"), ("587", "25"), (",\n  \"ApiKey\": \"\"", "")], changes);
        Assert.Equal(3, file.ChangeCount);
        Assert.Equal((1, 0, 2), (file.AddedCount, file.RemovedCount, file.ModifiedCount));

        // The server's Db is kept, and the package's other value can be taken from where it is
        var kept = Assert.Single(file.Marks, mark => mark.Kind == MarkKind.PackageDiffers);
        Assert.Equal("prod", file.Text.Substring(kept.Start, kept.Length));
        Assert.Contains("local", kept.Note);
    }

    [Fact]
    public void Each_change_can_be_undone_or_take_the_packages_text()
    {
        var changes = 0;
        var file = Review(changed: () => changes++);
        var timeout = file.Marks.First(mark => Of(file, mark).Text == "60");

        Action(timeout, "Keep the server's").Command.Execute(null);

        Assert.Contains("\"Timeout\": 30", file.Text);
        Assert.DoesNotContain(file.Marks, mark => mark.Kind == MarkKind.Change && mark.Removed == "30");
        Assert.True(changes > 0);

        var db = Assert.Single(file.Marks, mark => mark.Kind == MarkKind.PackageDiffers && Of(file, mark).Text == "prod");
        Action(db, "Use the package's").Command.Execute(null);

        Assert.Contains("\"Db\": \"local\"", file.Text);
        Assert.Equal(MergeChoice.TakeIncoming, file.Update.Plan.Rows.Single(row => row.Path == "Db").Choice);
        Assert.Contains(file.Marks, mark => mark.Kind == MarkKind.Change && Of(file, mark) == ("local", "prod"));
    }

    [Fact]
    public void A_setting_that_needs_a_value_is_marked_and_blocks_until_the_text_gives_it_one()
    {
        var plan = ConfigMerger.Plan(Base, Server, Package, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        plan.Rows.Single(row => row.Path == "ApiKey").Choice = null;
        var review = new MergeViewModel([new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, Package)], isReadOnly: false);
        var file = review.Files[0];

        var choice = Assert.Single(file.Choices);
        Assert.Equal("ApiKey", choice.Path);
        var mark = Assert.Single(file.Marks, mark => mark.Kind == MarkKind.NeedsChoice);
        Assert.Equal("\"\"", file.Text.Substring(mark.Start, mark.Length));
        Assert.False(review.CanApply);

        file.Text = file.Text.Replace("\"ApiKey\": \"\"", "\"ApiKey\": \"k-123\"");

        Assert.Empty(file.Choices);
        Assert.True(review.CanApply);
        Assert.Contains("\"ApiKey\": \"k-123\"", plan.BuildResult());
    }

    [Fact]
    public void A_conflict_can_take_either_side_and_stays_settled()
    {
        var file = Review(Package.Replace("\"local\"", "\"test\""));
        var choice = Assert.Single(file.Choices);
        Assert.Equal("Db", choice.Path);

        choice.Actions.Single(action => action.Name == "Keep the server's").Command.Execute(null);
        Assert.Contains("\"Db\": \"prod\"", file.Text);
        Assert.False(file.HasConflicts);

        file.Text = file.Text.Replace("60", "61");
        Assert.False(file.HasConflicts);
    }

    [Fact]
    public void Text_that_cant_be_read_says_why_and_blocks_writing_and_undo_all_starts_over()
    {
        var plan = ConfigMerger.Plan(Base, Server, Package, ConfigFormats.For("appsettings.json"), new MergeOptions([]));
        var review = new MergeViewModel([new ConfigFileUpdate(@"C:\App\appsettings.json", ConfigFileKind.Update, plan, Package)], isReadOnly: false);
        var file = review.Files[0];
        var start = file.Text;

        file.Text = "{ \"Db\": ";

        Assert.True(file.HasTextError);
        Assert.False(review.CanApply);
        Assert.NotEmpty(file.Marks);

        file.UndoAllCommand.Execute(null);

        Assert.False(file.HasTextError);
        Assert.True(review.CanApply);
        Assert.Equal(start, file.Text);
    }

    [Fact]
    public void A_bad_word_in_the_text_is_one_short_line_that_says_where_it_is()
    {
        var file = Review();
        var text = file.Text.Replace("\"prod\"", "nul");

        file.Text = text;

        Assert.StartsWith("Line 2, column ", file.TextError);
        Assert.DoesNotContain("\n", file.TextError);
        Assert.Contains("'nul' is an invalid JSON literal", file.TextError);
        Assert.Equal(text.IndexOf("nul", StringComparison.Ordinal) + 3, file.TextProblem!.Offset);

        file.UndoAllCommand.Execute(null);

        Assert.Null(file.TextProblem);
        Assert.Null(file.TextError);
    }

    [Fact]
    public void Any_text_file_on_the_server_is_edited_the_same_way_and_written_as_typed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netdeploy-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "alpha\nbeta\n");
        try
        {
            var file = new ConfigFileViewModel(ConfigUpdates.Open(path), () => { });
            Assert.Empty(file.Marks);

            file.Text = "alpha\nBETA\ngamma";

            Assert.Equal([("BETA", "beta"), ("gamma", "")], file.Marks.Select(mark => Of(file, mark)));
            Assert.True(file.HasChanges);
            Assert.Equal("Undo", Assert.Single(file.Marks[0].Actions).Name);

            ConfigUpdates.Write([file.Update]);
            Assert.Equal("alpha\nBETA\ngamma", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_that_isnt_text_doesnt_open()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netdeploy-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, [1, 0, 2]);
        try
        {
            Assert.Throws<InvalidDataException>(() => ConfigUpdates.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
