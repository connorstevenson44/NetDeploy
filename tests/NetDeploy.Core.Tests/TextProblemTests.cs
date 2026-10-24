using NetDeploy.Core.Config;

namespace NetDeploy.Core.Tests;

public class TextProblemTests
{
    private static TextProblem ProblemReading(string fileName, string text)
    {
        var ex = Assert.ThrowsAny<Exception>(() => ConfigFormats.For(fileName).Read(text));
        return TextProblem.From(ex, text);
    }

    [Fact]
    public void A_bad_word_is_named_on_one_line_with_where_it_is_not_the_rest_of_the_file()
    {
        // The JSON reader quotes from the bad word to the end of the file.
        var rest = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"  \"Key{i}\": \"value {i}\","));
        var text = "{\n  \"Enabled\": tru\n" + rest + "\n}";

        var problem = ProblemReading("appsettings.json", text);

        // The reader finds out at the end of the word.
        Assert.Equal("Line 2, column 17: 'tru' is an invalid JSON literal. Expected the literal 'true'.", problem.Message);
        Assert.Equal(text.IndexOf("tru\n", StringComparison.Ordinal) + 3, problem.Offset);
    }

    [Fact]
    public void A_missing_comma_says_where_without_the_readers_own_position()
    {
        var text = "{\n  \"Db\": \"prod\"\n  \"Port\": 25\n}";

        var problem = ProblemReading("appsettings.json", text);

        Assert.StartsWith("Line 3, column 3: ", problem.Message);
        Assert.DoesNotContain("LineNumber", problem.Message);
        Assert.Equal(text.IndexOf("\"Port\"", StringComparison.Ordinal), problem.Offset);
    }

    [Fact]
    public void Columns_count_characters_not_bytes()
    {
        var text = "{ \"Name\": \"Café\" \"Next\": 1 }";

        var problem = ProblemReading("appsettings.json", text);

        Assert.Equal(text.IndexOf("\"Next\"", StringComparison.Ordinal), problem.Offset);
    }

    [Fact]
    public void An_xml_problem_says_where_without_the_readers_own_position()
    {
        var text = "<configuration>\n  <appSettings>\n</configuration>";

        var problem = ProblemReading("Orders.exe.config", text);

        Assert.StartsWith("Line 3, column 3: ", problem.Message);
        Assert.DoesNotMatch(@"Line 3, position \d+\.$", problem.Message);
        Assert.NotNull(problem.Offset);
    }

    [Fact]
    public void A_problem_with_no_place_keeps_its_message()
    {
        var problem = ProblemReading("appsettings.json", "[1, 2]");

        Assert.Equal("The JSON file's root must be an object.", problem.Message);
        Assert.Null(problem.Offset);
    }

    [Fact]
    public void A_long_message_is_cut_to_one_short_line()
    {
        var problem = TextProblem.From(new InvalidDataException(new string('x', 500) + "\nmore"), "");

        Assert.Equal(160, problem.Message.Length);
        Assert.EndsWith("…", problem.Message);
    }
}
