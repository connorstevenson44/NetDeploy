using System.IO.Compression;
using System.Text;
using NetDeploy.Core.Logs;

namespace NetDeploy.Core.Tests;

public sealed class AppLogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Config(string targets, string extra = "") => Write("app/nlog.config", $"""
        <?xml version="1.0" encoding="utf-8"?>
        <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          {extra}
          <targets>
            {targets}
          </targets>
          <rules><logger name="*" minlevel="Info" writeTo="file" /></rules>
        </nlog>
        """);

    // With the path's separators as Windows writes them, as the logs are found.
    private string Write(string relativePath, string content) => Path.GetFullPath(_folder.File(relativePath, content));

    private string App(string relative) => Path.Combine(_folder.Path, "app", relative.Replace('/', Path.DirectorySeparatorChar));

    private string Zip(string relativePath, params (string Name, string Text)[] entries) => Zip(relativePath, 0, entries);

    private string Zip(string relativePath, int days, params (string Name, string Text)[] entries)
    {
        var path = Path.GetFullPath(_folder.Combine(relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            var entry = zip.CreateEntry(name);
            entry.LastWriteTime = DateTimeOffset.Now.AddDays(-days);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(text);
        }
        return path;
    }

    private static void Age(string path, int days) => File.SetLastWriteTime(path, DateTime.Now.AddDays(-days));

    // ===== nlog.config =====

    [Fact]
    public void File_targets_are_read_with_their_paths_worked_out_from_outside_the_app()
    {
        var config = Config("""
            <target xsi:type="AsyncWrapper" name="async">
              <target xsi:type="File" name="file" fileName="${var:logDir}/${shortdate}.log"
                      archiveFileName="${logDir}/archive/app.{###}.zip" archiveEvery="Day" />
            </target>
            <target type="File" name="errors" fileName="logs/errors-${level:uppercase=true}.txt" />
            <target xsi:type="Console" name="console" />
            <target xsi:type="File" name="nowhere" />
            """, """<variable name="logDir" value="${basedir}/logs" />""");

        var targets = NLogConfig.Read(config);

        Assert.Equal(["file", "errors"], targets.Select(target => target.Name));
        Assert.Equal(App("logs/*.log"), targets[0].FilePattern);
        Assert.Equal([App("logs/archive/app.*.zip")], targets[0].ArchivePatterns);
        // A relative path is under the app's folder, as NLog writes it.
        Assert.Equal(App("logs/errors-*.txt"), targets[1].FilePattern);
        Assert.Empty(targets[1].ArchivePatterns);
    }

    [Fact]
    public void A_target_that_archives_without_an_archive_name_numbers_its_logs_beside_it()
    {
        var config = Config("""
            <target xsi:type="File" name="file" fileName="${basedir:dir=logs}/app.log" archiveAboveSize="10485760" enableArchiveFileCompression="True" />
            """);

        var target = Assert.Single(NLogConfig.Read(config));

        Assert.Equal(App("logs/app.log"), target.FilePattern);
        Assert.Equal([App("logs/app*.log"), App("logs/app*.zip")], target.ArchivePatterns);
    }

    [Fact]
    public void Default_target_parameters_and_included_files_count_and_what_changes_at_run_time_is_a_star()
    {
        Write("app/nlog.targets.config", """
            <nlog>
              <targets>
                <target xsi:type="File" name="trace" fileName="${basedir}/trace/${processid}/${date:format=yyyy\:MM}.log" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" />
              </targets>
            </nlog>
            """);
        var config = Config("""
            <default-target-parameters xsi:type="File" archiveFileName="${basedir}/old/{#}.log" />
            <target xsi:type="File" name="file" fileName="${basedir}/app.log" />
            """, """<include file="${basedir}/nlog.targets.config" /><include file="missing.config" ignoreErrors="true" />""");

        var targets = NLogConfig.Read(config);

        // In the order they are written, the include first.
        Assert.Equal(["trace", "file"], targets.Select(target => target.Name));
        Assert.Equal(App("trace/*/*.log"), targets[0].FilePattern);
        Assert.Equal([App("old/*.log")], targets[1].ArchivePatterns);
    }

    [Fact]
    public void An_nlog_config_is_found_whatever_its_case_and_one_that_isnt_xml_says_so()
    {
        var path = Write("app/NLog.config", "<nlog><targets>");

        Assert.Equal(path, NLogConfig.Find(_folder.Combine("app")));
        Assert.Null(NLogConfig.Find(_folder.Path));
        Assert.Contains("isn't valid XML", Assert.Throws<InvalidDataException>(() => NLogConfig.Read(path)).Message);
    }

    // ===== Finding the logs =====

    [Fact]
    public void The_newest_file_is_live_and_the_rest_and_the_zipped_archives_are_listed_newest_first()
    {
        var today = Write("app/logs/2026-10-24.log", "today");
        Age(Write("app/logs/2026-10-23.log", "yesterday"), 1);
        // Zipped logs keep the time they were last written.
        var zip = Zip("app/logs/archive/app.1.zip", 3, ("2026-10-20.log", "old"), ("2026-10-21.log", "older"));
        var targets = new[] { new NLogFileTarget("file", App("logs/*.log"), [App("logs/archive/app.*.zip")]) };

        var logs = AppLogs.Find(targets, cancellation: Token);

        Assert.Equal(today, logs[0].Path);
        Assert.True(logs[0].IsLive);
        Assert.Equal(["2026-10-24.log", "2026-10-23.log"], logs.Take(2).Select(log => log.Name));
        Assert.False(logs[1].IsLive);
        Assert.Equal(["2026-10-20.log", "2026-10-21.log"], logs.Skip(2).Select(log => log.Name).Order());
        Assert.All(logs.Skip(2), log => Assert.True(log.IsZipped));
        Assert.All(logs.Skip(2), log => Assert.Equal(zip, log.Path));
        Assert.Equal(4, logs.Select(log => log.Key).Distinct().Count());
    }

    [Fact]
    public void Listing_again_reads_a_zip_again_only_once_it_has_changed()
    {
        var zip = Zip("app/logs/app.1.zip", ("one.log", "1"));
        var targets = new[] { new NLogFileTarget("file", App("logs/app.log"), [App("logs/*.zip")]) };
        var zips = new ZipContents();

        var first = AppLogs.Find(targets, zips, Token);
        Assert.Same(first[0], AppLogs.Find(targets, zips, Token)[0]);

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            archive.CreateEntry("two.log");
        File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(["one.log", "two.log"], AppLogs.Find(targets, zips, Token).Select(log => log.Name).Order());
    }

    [Fact]
    public void A_target_whose_folder_isnt_there_yet_has_no_logs()
    {
        var targets = new[] { new NLogFileTarget("file", App("logs/*.log"), [App("logs/app.log")]) };

        Assert.Empty(AppLogs.Find(targets, cancellation: Token));
    }

    // ===== Reading a log =====

    [Fact]
    public void A_log_is_read_whole_and_a_long_one_from_the_first_whole_line_near_its_end()
    {
        var path = Write("app/app.log", "line one\nline two\nline three\n");
        var log = new AppLog(path, null, "file", true, DateTime.Now, new FileInfo(path).Length);

        var whole = LogReader.Read(log, cancellation: Token);
        var end = LogReader.Read(log, maxBytes: 14, cancellation: Token);

        Assert.Equal("line one\nline two\nline three\n", whole.Text);
        Assert.False(whole.IsCut);
        Assert.Equal("line three\n", end.Text);
        Assert.True(end.IsCut);
    }

    [Fact]
    public void A_zipped_log_is_read_out_of_its_zip()
    {
        var text = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i}"));
        var zip = Zip("app/logs/old.zip", ("old.log", text));
        var log = Assert.Single(AppLogs.Find([new NLogFileTarget("file", App("app.log"), [App("logs/*.zip")])], cancellation: Token));

        Assert.Equal(zip, log.Path);
        Assert.Equal(text, LogReader.Read(log, cancellation: Token).Text);
        Assert.Null(LogReader.Read(log, cancellation: Token).Tail);
        Assert.Equal("line 50", LogReader.Read(log, maxBytes: 10, cancellation: Token).Text);
    }

    [Fact]
    public void A_log_written_in_utf16_is_read_as_utf16()
    {
        var path = _folder.Combine("app.log");
        File.WriteAllText(path, "Grüße\n", Encoding.Unicode);
        var log = new AppLog(path, null, "file", true, DateTime.Now, new FileInfo(path).Length);

        Assert.Equal("Grüße\n", LogReader.Read(log, cancellation: Token).Text);
    }

    [Fact]
    public void The_tail_reads_what_the_app_adds_even_a_character_split_between_writes()
    {
        var path = Write("app.log", "first\n");
        var tail = LogReader.Read(new AppLog(path, null, "file", true, DateTime.Now, 6), cancellation: Token).Tail!;
        Assert.Equal("", tail.ReadNew());

        var euro = Encoding.UTF8.GetBytes("€");
        using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            file.Write(Encoding.UTF8.GetBytes("second "));
            file.Write(euro, 0, 1);
        }
        Assert.Equal("second ", tail.ReadNew());
        using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            file.Write([.. euro[1..], .. Encoding.UTF8.GetBytes("5\n")]);
        Assert.Equal("€5\n", tail.ReadNew());

        // NLog moved it to an archive and started a new one.
        File.WriteAllText(path, "new\n");
        Assert.Null(tail.ReadNew());
        File.Delete(path);
        Assert.Null(tail.ReadNew());
    }

    // ===== Searching =====

    [Fact]
    public void Matches_are_found_a_line_at_a_time_as_typed_or_as_a_regular_expression()
    {
        const string text = "INFO started order 1.5\r\nERROR order 125 failed\nWARN Order 2 slow";

        Assert.Equal([new LogMatch(13, 5), new LogMatch(30, 5), new LogMatch(52, 5)], LogSearch.Matches(text, LogSearch.Pattern("order", false, false), cancellation: Token));
        Assert.Equal([new LogMatch(13, 5), new LogMatch(30, 5)], LogSearch.Matches(text, LogSearch.Pattern("order", false, matchCase: true), cancellation: Token));
        // As typed, the dot is a dot; as a regular expression it is any character.
        Assert.Single(LogSearch.Matches(text, LogSearch.Pattern("1.5", false, false), cancellation: Token));
        Assert.Equal(2, LogSearch.Matches(text, LogSearch.Pattern("1.5", true, false), cancellation: Token).Count);
        // $ is the end of a line, before its \r.
        Assert.Equal([new LogMatch(19, 3)], LogSearch.Matches(text, LogSearch.Pattern(@"\d\.\d$", true, false), cancellation: Token));
        // A pattern that can match nothing only counts where it matches something.
        Assert.Empty(LogSearch.Matches(text, LogSearch.Pattern("x*", true, false), cancellation: Token));
        Assert.ThrowsAny<ArgumentException>(() => LogSearch.Pattern("order(", true, false));
    }

    [Fact]
    public void Matches_from_part_way_start_at_the_start_of_that_line_and_stop_at_the_most_asked_for()
    {
        const string text = "order 1\norder 2\norder 3\n";

        Assert.Equal([new LogMatch(8, 5), new LogMatch(16, 5)], LogSearch.Matches(text, LogSearch.Pattern("order", false, false), from: 10, cancellation: Token));
        Assert.Equal(2, LogSearch.Matches(text, LogSearch.Pattern("order", false, false), max: 2, cancellation: Token).Count);
    }

    [Fact]
    public async Task Searching_every_log_counts_the_matches_in_each_including_zipped_ones()
    {
        var live = Write("app/logs/app.log", "ORD-1 placed\nORD-2 placed\n");
        Zip("app/logs/app.1.zip", ("app.1.log", "ORD-1 shipped\nnothing\n"));
        Write("app/logs/app.2.log", "nothing here\n");
        var logs = AppLogs.Find([new NLogFileTarget("file", live, [App("logs/app.*")])], cancellation: Token);
        var reports = new List<LogSearchProgress>();

        var counts = await LogSearch.SearchAsync(logs, LogSearch.Pattern(@"ORD-\d", true, false), new SynchronousProgress(reports.Add), Token);

        Assert.Equal(2, counts[logs.Single(log => log.Name == "app.log").Key]);
        Assert.Equal(1, counts[logs.Single(log => log.Name == "app.1.log").Key]);
        Assert.Equal(0, counts[logs.Single(log => log.Name == "app.2.log").Key]);
        Assert.Equal(1, reports.Max(report => report.Fraction), 3);
        Assert.Equal(3, reports.Count(report => report.Searched is not null));
    }

    [Fact]
    public async Task A_search_of_every_log_can_be_cancelled()
    {
        var live = Write("app/app.log", "x\n");
        var logs = AppLogs.Find([new NLogFileTarget("file", live, [])], cancellation: Token);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LogSearch.SearchAsync(logs, LogSearch.Pattern("x", false, false), null, cancellation.Token));
    }

    private sealed class SynchronousProgress(Action<LogSearchProgress> report) : IProgress<LogSearchProgress>
    {
        private readonly Lock _lock = new();

        public void Report(LogSearchProgress value)
        {
            lock (_lock)
                report(value);
        }
    }
}
