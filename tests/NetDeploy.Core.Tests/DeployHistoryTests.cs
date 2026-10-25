using NetDeploy.Core.Deployment;

namespace NetDeploy.Core.Tests;

public sealed class DeployHistoryTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static DeployRecord Record(string folder, string version, DateTimeOffset time, DeployResult result = DeployResult.Succeeded) =>
        new("Orders", version, "main", folder, time, @"CONTOSO\dana", result, RolledBack: false, Log: null);

    [Fact]
    public void An_installs_deploys_are_read_back_newest_first()
    {
        var history = new DeployHistory(_folder.Combine("history"));
        var main = Path.Combine(_folder.Path, "Apps", "main");
        var start = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
        Assert.True(history.TryAdd(Record(main, "1.0.0", start)));
        Assert.True(history.TryAdd(Record(main, "1.1.0", start.AddHours(1), DeployResult.Failed)));
        Assert.True(history.TryAdd(Record(Path.Combine(_folder.Path, "Apps", "blue"), "1.1.0", start.AddHours(2))));

        // Paths are told apart as Windows does, without a trailing separator.
        var records = history.For(main + Path.DirectorySeparatorChar);

        Assert.Equal([("1.1.0", DeployResult.Failed), ("1.0.0", DeployResult.Succeeded)], records.Select(record => (record.Version, record.Result)));
        Assert.Equal(@"CONTOSO\dana", records[0].User);
    }

    [Fact]
    public void A_folder_written_another_way_still_finds_its_deploys()
    {
        var history = new DeployHistory(_folder.Combine("history"));
        var main = Path.Combine(_folder.Path, "Apps", "main");
        // As a folder typed with the other slash, a trailing one and a "..", such as C:/Apps/x/../main/, is recorded.
        var typed = Path.Combine(_folder.Path, "Apps", "x", "..", "main").Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "/";
        Assert.True(history.TryAdd(Record(typed, "1.0.0", DateTimeOffset.Now)));
        // A file edited by hand can hold a folder that isn't a path at all.
        Assert.True(history.TryAdd(Record("not\0a path", "9.9.9", DateTimeOffset.Now)));

        Assert.Equal(["1.0.0"], history.For(main).Select(record => record.Version));
    }

    [Fact]
    public void A_missing_or_damaged_history_reads_as_empty()
    {
        var history = new DeployHistory(_folder.Combine("history"));
        Assert.Empty(history.For(_folder.Path));

        _folder.File("history/broken.json", "{ not json");
        Assert.Empty(history.For(_folder.Path));
    }

    [Fact]
    public void A_history_that_cant_be_written_only_loses_the_record()
    {
        // A file where the folder should be.
        var history = new DeployHistory(_folder.File("history", ""));

        Assert.False(history.TryAdd(Record(_folder.Path, "1.0.0", DateTimeOffset.Now)));
    }
}
