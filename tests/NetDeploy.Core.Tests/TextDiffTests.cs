using NetDeploy.Core.Text;

namespace NetDeploy.Core.Tests;

public class TextDiffTests
{
    // Each change as the text taken out and the text put in.
    private static List<(string Old, string New)> Changes(string oldText, string newText) =>
        TextDiff.Compare(oldText, newText).Select(change =>
            (oldText.Substring(change.OldStart, change.OldLength), newText.Substring(change.NewStart, change.NewLength))).ToList();

    [Fact]
    public void A_changed_value_is_one_change_of_just_that_word()
    {
        const string before = "{\n  \"Default\": \"Information\",\n  \"Port\": 25\n}\n";
        const string after = "{\n  \"Default\": \"Warning\",\n  \"Port\": 25\n}\n";

        var change = Assert.Single(TextDiff.Compare(before, after));

        Assert.Equal(("Information", "Warning"), (before.Substring(change.OldStart, change.OldLength), after.Substring(change.NewStart, change.NewLength)));
        Assert.Equal(before.IndexOf("Information", StringComparison.Ordinal), change.OldStart);
        Assert.Equal(after.IndexOf("Warning", StringComparison.Ordinal), change.NewStart);
    }

    [Fact]
    public void Added_and_removed_lines_are_whole_lines()
    {
        Assert.Equal([("", "  \"Hosts\": [\"a\"],\n")], Changes("{\n  \"Db\": 1,\n  \"Port\": 2\n}\n", "{\n  \"Db\": 1,\n  \"Hosts\": [\"a\"],\n  \"Port\": 2\n}\n"));
        Assert.Equal([("b\n", "")], Changes("a\nb\nc\n", "a\nc\n"));
    }

    [Fact]
    public void A_line_typed_in_on_a_changed_line_is_a_whole_new_line()
    {
        Assert.Equal([("", "  \"Hosts\": [],\n"), ("30", "14")],
            Changes("{\n  \"Cleanup\": { \"KeepDays\": 30 }\n}", "{\n  \"Hosts\": [],\n  \"Cleanup\": { \"KeepDays\": 14 }\n}"));
    }

    [Fact]
    public void Changes_on_separate_lines_stay_separate_and_words_split_only_by_spaces_join()
    {
        Assert.Equal([("1", "2"), ("x", "y")], Changes("a = 1\nb = x\n", "a = 2\nb = y\n"));
        Assert.Equal([("old words", "new text")], Changes("say old words here\n", "say new text here\n"));
    }

    [Fact]
    public void Words_split_only_by_punctuation_are_one_change()
    {
        Assert.Equal([("first: line, of text", "another; thing: entirely")], Changes("first: line, of text\n", "another; thing: entirely\n"));
    }

    [Fact]
    public void A_comma_added_at_the_end_of_a_line_goes_with_the_line_added_after_it()
    {
        Assert.Equal([("25", "587"), ("", ",\n  \"ApiKey\": \"\"")],
            Changes("{\n  \"Smtp\": { \"Port\": 25 }\n}", "{\n  \"Smtp\": { \"Port\": 587 },\n  \"ApiKey\": \"\"\n}"));
    }

    [Fact]
    public void The_same_text_has_no_changes_and_any_line_ending_works()
    {
        Assert.Empty(TextDiff.Compare("a\r\nb", "a\r\nb"));
        Assert.Equal([("b", "c")], Changes("a\r\nb", "a\r\nc"));
        Assert.Equal([("", "x")], Changes("", "x"));
    }

    [Fact]
    public void Map_finds_where_old_positions_land_in_the_new_text()
    {
        const string before = "keep old keep";
        const string after = "keep newer keep";
        var changes = TextDiff.Compare(before, after);

        Assert.Equal(2, TextDiff.Map(changes, 2));
        Assert.Equal(5, TextDiff.Map(changes, 5));
        Assert.Equal(10, TextDiff.Map(changes, 8, atEnd: true));
        Assert.Equal(12, TextDiff.Map(changes, 10));
    }
}
