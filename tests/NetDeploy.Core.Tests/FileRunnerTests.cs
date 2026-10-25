using NetDeploy.Core.Processes;

namespace NetDeploy.Core.Tests;

public sealed class FileRunnerTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly TestLog _log = new();

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("tool.bat", "@echo got %1 %2\r\n@exit /b 3")]
    [InlineData("tool.ps1", "Write-Output \"got $($args[0]) $($args[1])\"; exit 3")]
    public async Task Scripts_run_in_their_folder_with_the_arguments_given(string name, string script)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Runs Windows scripts.");
        var path = _folder.File(name, script);

        var exitCode = await FileRunner.RunAsync(path, "\"a b\" c", _log, TestContext.Current.CancellationToken);

        Assert.Equal(3, exitCode);
        Assert.Contains(_log.Entries, entry => entry.Message.Contains("got") && entry.Message.Contains('c'));
    }

    [Theory]
    [InlineData("Orders.exe", true)]
    [InlineData("setup.PS1", true)]
    [InlineData("go.cmd", true)]
    [InlineData("appsettings.json", false)]
    public void Only_programs_and_scripts_can_be_run(string name, bool canRun) => Assert.Equal(canRun, FileRunner.CanRun(name));
}
