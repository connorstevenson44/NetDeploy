using System.IO.Compression;
using NetDeploy.Core.Steps;

namespace NetDeploy.Core.Tests;

public class StepTests
{
    [Fact]
    public async Task CopyFiles_with_deleteExtra_keeps_preserved_files_configs_and_tool_state()
    {
        using var package = new TempFolder();
        using var target = new TempFolder();
        package.File("app/app.dll", "new");
        target.File("stale.dll", "old");
        target.File("data/orders.db", "data");
        target.File("appsettings.Production.json", "{}");
        target.File(".deploy/base/appsettings.json", "{}");

        var step = new CopyFilesStep { From = "app", To = target.Path, Preserve = ["data/**"], DeleteExtra = true };
        await step.RunAsync(Context(package.Path), TestContext.Current.CancellationToken);

        Assert.True(target.Exists("app.dll"));
        Assert.False(target.Exists("stale.dll"));
        Assert.True(target.Exists("data/orders.db"));
        Assert.True(target.Exists("appsettings.Production.json"));
        Assert.True(target.Exists(".deploy/base/appsettings.json"));
    }

    [Fact]
    public async Task Backup_keeps_only_the_newest_copies()
    {
        using var install = new TempFolder();
        install.File("app.dll", "v1");
        var backups = install.Path + ".backups";
        Directory.CreateDirectory(Path.Combine(backups, "20200101-000000"));
        Directory.CreateDirectory(Path.Combine(backups, "20200102-000000"));

        try
        {
            var context = Context(install.Path);
            await new BackupStep { Folder = install.Path, Keep = 2 }.RunAsync(context, TestContext.Current.CancellationToken);

            var remaining = Directory.GetDirectories(backups).Select(Path.GetFileName).Order().ToList();
            Assert.Equal(2, remaining.Count);
            Assert.Equal("20200102-000000", remaining[0]);
            Assert.True(File.Exists(Path.Combine(backups, remaining[1]!, "app.dll")));
            Assert.True(context.Rollback.IsAvailable);
        }
        finally
        {
            Directory.Delete(backups, recursive: true);
        }
    }

    [Theory]
    [InlineData("10.0.1", true)]
    [InlineData("10.0.2", true)]
    [InlineData("10.0.3", false)]
    [InlineData("9.0.0", false)]
    public void A_dotnet_runtime_counts_only_within_the_same_major_version(string minimum, bool expected)
    {
        using var dotnet = new TempFolder();
        Directory.CreateDirectory(dotnet.Combine("shared/Microsoft.NETCore.App/10.0.2"));
        Directory.CreateDirectory(dotnet.Combine("shared/Microsoft.NETCore.App/11.0.0-preview.1"));

        var found = PrerequisiteCheck.HasDotnetRuntime("Microsoft.NETCore.App", PrerequisiteCheck.ParseVersion(minimum)!, dotnet.Path);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void A_zip_package_opens_and_cleans_up_its_temp_folder()
    {
        using var source = new TempFolder();
        source.File("deploy.json", """{ "schemaVersion": 1, "app": { "name": "A", "version": "1" }, "steps": [ { "type": "backup", "path": "x" } ] }""");
        source.File("app/a.dll", "x");
        var zip = source.Path + ".zip";
        ZipFile.CreateFromDirectory(source.Path, zip);

        try
        {
            string root;
            using (var package = DeployPackage.Open(zip))
            {
                root = package.RootPath;
                Assert.Equal("A", package.Manifest.App.Name);
                Assert.True(File.Exists(Path.Combine(root, "app", "a.dll")));
            }
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            File.Delete(zip);
        }
    }

    [Fact]
    public void A_package_without_a_manifest_is_rejected()
    {
        using var folder = new TempFolder();

        var error = Assert.Throws<ManifestException>(() => DeployPackage.Open(folder.Path));

        Assert.Contains("no deploy.json", error.Message);
    }

    [Fact]
    public void A_write_to_a_file_in_use_is_tried_again_until_the_file_is_free()
    {
        var tries = 0;

        FileSystem.WhenFree(() =>
        {
            if (++tries < 3)
                throw new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));
        }, waitMilliseconds: 1);

        Assert.Equal(3, tries);
    }

    [Fact]
    public void A_write_that_fails_for_another_reason_or_stays_locked_is_not_hidden()
    {
        var tries = 0;

        Assert.Throws<IOException>(() => FileSystem.WhenFree(() => { tries++; throw new IOException("The disk is full.", unchecked((int)0x80070070)); }, waitMilliseconds: 1));
        Assert.Equal(1, tries);
        Assert.Throws<IOException>(() => FileSystem.WhenFree(() => throw new IOException("In use.", unchecked((int)0x80070021)), tries: 3, waitMilliseconds: 1));
    }

    private static StepContext Context(string packageDirectory) =>
        new(new DeployRunner(new FakeUser(), new TestLog()))
        {
            PackageDirectory = packageDirectory,
            DryRun = false,
            Log = new TestLog(),
            User = new FakeUser(),
        };
}
