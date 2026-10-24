using System.IO;
using System.IO.Compression;
using NetDeploy.App.ViewModels;

namespace NetDeploy.App.Tests;

public class LogsViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "NetDeployUiTest", "logs", Path.GetRandomFileName())).FullName;
    private readonly List<IDisposable> _open = [];

    public LogsViewModelTests()
    {
        Write("main/Orders.exe", "");
        Write("main/nlog.config", """
            <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <targets async="true">
                <target xsi:type="File" name="file" fileName="${basedir}/logs/orders.log"
                        archiveFileName="${basedir}/logs/archive/orders.{#}.zip" archiveAboveSize="1048576" />
              </targets>
            </nlog>
            """);
        Write("main/logs/orders.log", "2026-10-24 10:00:00|INFO|Started\n2026-10-24 10:00:05|INFO|ORD-2001 placed\n");
        Zip("main/logs/archive/orders.1.zip", "orders.1.log", "2026-10-23 09:00:00|ERROR|ORD-1042 payment declined\n2026-10-23 09:00:01|WARN|ORD-1042 retried\n");
        File.SetLastWriteTime(Path.Combine(_root, "main/logs/archive/orders.1.zip"), DateTime.Now.AddDays(-1));
    }

    public void Dispose()
    {
        foreach (var open in _open)
            open.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string Write(string path, string text)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    private void Append(string path, string text)
    {
        using var file = new FileStream(Path.Combine(_root, path), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        file.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private void Zip(string path, string entry, string text)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var zip = ZipFile.Open(full, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
        writer.Write(text);
    }

    private async Task<LogsViewModel> OpenAsync()
    {
        var logs = new LogsViewModel(Path.Combine(_root, "main", "nlog.config"));
        _open.Add(logs);
        await logs.Listing;
        await logs.Loading;
        await logs.Finding;
        return logs;
    }

    private static AppLogViewModel Log(LogsViewModel logs, string name) => logs.Logs.Single(log => log.Name == name);

    [Fact]
    public async Task The_live_log_opens_first_with_the_archived_ones_listed_below_it()
    {
        var logs = await OpenAsync();

        Assert.Equal(["LIVE", "orders.log", "ARCHIVED", "orders.1.log"],
            logs.Rows.Select(row => row is LogSectionViewModel section ? section.Title : ((AppLogViewModel)row).Name));
        Assert.Equal("orders.log", logs.Open!.Name);
        Assert.True(logs.Open.IsOpen);
        Assert.True(logs.Follow);
        Assert.Contains("ORD-2001 placed", logs.Text);
        Assert.Equal("orders.1.zip", Log(logs, "orders.1.log").Zip);
        Assert.Null(logs.Problem);
    }

    [Fact]
    public async Task Typing_a_search_highlights_its_matches_in_the_open_log_and_steps_through_them()
    {
        var logs = await OpenAsync();

        logs.Query = "info";
        await logs.Finding;

        Assert.Equal(2, logs.Matches.Count);
        Assert.Equal("1 of 2", logs.MatchText);
        Assert.Equal("INFO", logs.Text.Substring(logs.CurrentMatch!.Value.Start, logs.CurrentMatch.Value.Length));
        Assert.False(logs.Follow);
        logs.NextMatchCommand.Execute(null);
        Assert.Equal("2 of 2", logs.MatchText);
        logs.NextMatchCommand.Execute(null);
        Assert.Equal("1 of 2", logs.MatchText);

        logs.MatchCase = true;
        await logs.Finding;
        Assert.Equal("No matches", logs.MatchText);
    }

    [Fact]
    public async Task A_regular_expression_that_isnt_valid_says_so_and_cant_be_searched_for()
    {
        var logs = await OpenAsync();

        logs.IsRegex = true;
        logs.Query = @"ORD-(\d+";
        await logs.Finding;

        Assert.StartsWith("Not a regular expression", logs.QueryError);
        Assert.Empty(logs.Matches);
        Assert.False(logs.SearchAllCommand.CanExecute(null));

        logs.Query = @"ORD-\d+";
        await logs.Finding;
        Assert.Null(logs.QueryError);
        Assert.Single(logs.Matches);
        Assert.True(logs.SearchAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Search_all_marks_the_logs_with_matches_and_a_new_search_clears_the_marks()
    {
        var logs = await OpenAsync();
        logs.Query = "ORD-1042";
        await logs.Finding;
        Assert.Equal("No matches", logs.MatchText);

        logs.SearchAllCommand.Execute(null);
        await logs.Searching;

        Assert.False(logs.IsSearching);
        Assert.Equal(2, Log(logs, "orders.1.log").Hits);
        Assert.True(Log(logs, "orders.1.log").HasHits);
        Assert.Equal("orders.1.log in orders.1.zip, 2 matches", Log(logs, "orders.1.log").AutomationName);
        Assert.True(Log(logs, "orders.log").IsMiss);
        Assert.Equal("Found in 1 of 2 logs", logs.SearchStatus);

        // Opening the archive shows its first match.
        logs.OpenLogCommand.Execute(Log(logs, "orders.1.log"));
        await logs.Loading;
        await logs.Finding;
        Assert.Contains("payment declined", logs.Text);
        Assert.Equal("1 of 2", logs.MatchText);

        logs.Query = "ORD-2001";
        await logs.Finding;
        Assert.All(logs.Logs, log => Assert.Null(log.Hits));
        Assert.Null(logs.SearchStatus);
    }

    [Fact]
    public async Task The_live_log_shows_what_the_app_adds_and_its_matches()
    {
        var logs = await OpenAsync();
        logs.Query = "ORD-";
        await logs.Finding;
        logs.Follow = true;
        var added = new List<string>();
        logs.Appended += (_, text) => added.Add(text);

        Append("main/logs/orders.log", "2026-10-24 10:00:10|INFO|ORD-2002 placed\n");
        await logs.FollowAsync();

        Assert.Equal(["2026-10-24 10:00:10|INFO|ORD-2002 placed\n"], added);
        Assert.EndsWith("ORD-2002 placed\n", logs.Text);
        Assert.Equal(2, logs.Matches.Count);
        Assert.Equal("2 matches", logs.MatchText);
    }

    [Fact]
    public async Task When_nlog_starts_a_new_log_it_is_read_again()
    {
        var logs = await OpenAsync();

        // NLog moved the log to an archive and started a new one.
        Write("main/logs/orders.log", "2026-10-24 11:00:00|INFO|Fresh\n");
        await logs.FollowAsync();
        await logs.Listing;
        await logs.Loading;

        Assert.Equal("2026-10-24 11:00:00|INFO|Fresh\n", logs.Text);
    }

    [Fact]
    public async Task A_config_without_file_targets_or_without_logs_yet_says_why()
    {
        Write("main/nlog.config", """<nlog><targets><target type="Console" name="console" /></targets></nlog>""");
        var logs = await OpenAsync();
        Assert.Contains("doesn't write logs to files", logs.Problem);
        Assert.False(logs.HasLogs);

        Write("main/nlog.config", """<nlog><targets><target type="File" name="file" fileName="${basedir}/none/app.log" /></targets></nlog>""");
        logs.RefreshCommand.Execute(null);
        await logs.Listing;
        Assert.Equal($"No logs yet. NLog writes them to {Path.Combine("none", "app.log")}.", logs.Problem);
        Assert.False(logs.HasOpen);
    }

    [Fact]
    public async Task The_folder_view_has_logs_inside_an_app_with_an_nlog_config()
    {
        var folder = new InstallFolderViewModel(_root, null, ["Orders.exe"]);
        _open.Add(folder);
        await folder.Listing;
        Assert.False(folder.HasLogs);

        folder.GoIntoCommand.Execute(folder.Rows.OfType<FolderNodeViewModel>().Single(row => row.Name == "main"));
        await folder.Listing;
        Assert.True(folder.HasLogs);
        var logs = folder.Logs!;
        await logs.Listing;
        await logs.Loading;

        // Deeper into the same app keeps its logs; out of it, there are none.
        folder.GoIntoCommand.Execute(folder.Rows.OfType<FolderNodeViewModel>().Single(row => row.Name == "logs"));
        await folder.Listing;
        Assert.Same(logs, folder.Logs);
        folder.GoUpCommand.Execute(null);
        folder.GoUpCommand.Execute(null);
        await folder.Listing;
        Assert.False(folder.HasLogs);
    }
}
